using Microsoft.Extensions.Logging;
using MondShield.Application.Common.Models;
using MondShield.Application.Mt5;
using MondShield.Application.Onboarding;
using MondShield.Domain.Accounts;
using MondShield.Domain.Compensation;
using MondShield.Domain.Ledger;
using MondShield.Domain.Stages;

namespace MondShield.Application.Compensation;

public sealed class PayoutService : IPayoutService
{
    private readonly ICompensationRepository _compensation;
    private readonly IShieldAccountRepository _accounts;
    private readonly IMt5Client _mt5;
    private readonly ILogger<PayoutService> _logger;

    public PayoutService(
        ICompensationRepository compensation,
        IShieldAccountRepository accounts,
        IMt5Client mt5,
        ILogger<PayoutService> logger)
    {
        _compensation = compensation;
        _accounts = accounts;
        _mt5 = mt5;
        _logger = logger;
    }

    public async Task<int> ProcessDuePayoutsAsync(CancellationToken ct = default)
    {
        // A request stuck in Paying means a previous run died between the MT5 credit and the final
        // commit. We CANNOT tell from here whether the money actually moved, so we never auto-retry
        // (that risks a double credit); we surface it loudly for manual reconciliation instead.
        var inFlight = await _compensation.GetInFlightPayoutsAsync(ct);
        foreach (var stuck in inFlight)
        {
            _logger.LogError(
                "Compensation request {RequestId} (account {AccountId}, {Amount}) is stuck in Paying — a prior " +
                "payout run died mid-flight. It will NOT be auto-retried. Reconcile against MT5 deal history " +
                "(find the DEAL_BALANCE deal whose comment carries this request Id), then mark it Paid or reset " +
                "it to Approved by hand.",
                stuck.Id, stuck.AccountId, stuck.CappedAmount);
        }

        var due = await _compensation.GetDueForPayoutAsync(DateTime.UtcNow, ct);
        var paidCount = 0;

        foreach (var request in due)
        {
            try
            {
                await PayOneAsync(request, ct);
                paidCount++;
            }
            catch (Exception ex)
            {
                // On failure the request is either still Approved (the pre-credit claim commit failed —
                // safe to retry next run) or Paying (the failure was at/after the irreversible MT5
                // credit — left for manual reconciliation, never blindly re-credited).
                _logger.LogError(ex, "Failed to pay out compensation request {RequestId}", request.Id);
            }
        }

        return paidCount;
    }

    private async Task PayOneAsync(Domain.Compensation.CompensationRequest request, CancellationToken ct)
    {
        var account = await _accounts.GetByIdAsync(request.AccountId, ct)
            ?? throw new InvalidOperationException($"Account {request.AccountId} not found for compensation request {request.Id}.");

        if (account.CurrentStage is not { } currentStage || account.Mt5Login is not { } mt5Login)
        {
            throw new InvalidOperationException($"Account {account.Id} is not in a payable state (missing stage or MT5 login).");
        }

        // PHASE 1 — Claim: flip Approved → Paying and COMMIT before touching MT5. The due-query only
        // selects Approved requests, so once this commit lands the request can never be re-selected
        // and blindly credited a second time if a later step (or the whole process) dies. This commit
        // carries only the status change — nothing else is dirty yet.
        request.Status = CompensationRequestStatus.Paying;
        await _compensation.SaveChangesAsync(ct);

        // PHASE 2 — Credit MT5 (irreversible). The request Id is embedded in the comment so a stuck
        // Paying request can be reconciled to the exact DEAL_BALANCE deal on the MT5 side.
        await _mt5.CreditBalanceAsync(mt5Login, request.CappedAmount, Mt5Comments.Compensation(request.Id, request.StageAtRequest), ct);

        // PHASE 3 — Confirm: record the ledger, apply the down-transition, bump the cap tracker, and
        // mark the request Paid, all committed atomically.
        await ConfirmPaymentAsync(account, request, currentStage, ct);
    }

    /// <summary>
    /// Admin reconciliation: the MT5 credit for a stuck <see cref="CompensationRequestStatus.Paying"/>
    /// request has been VERIFIED to have landed (the DEAL_BALANCE deal exists on the server), so
    /// complete phase 3 — ledger, composition, down-transition, cap tracker, Paid — WITHOUT re-crediting
    /// MT5. Only valid from Paying; the account must still hold its stage and MT5 login.
    /// </summary>
    public async Task<Result> ConfirmStuckPayoutAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _compensation.GetRequestByIdAsync(requestId, ct);
        if (request is null)
        {
            return Result.Failure("Request not found.");
        }

        if (request.Status != CompensationRequestStatus.Paying)
        {
            return Result.Failure($"Only a request stuck in Paying can be confirmed paid; this one is {request.Status}.");
        }

        var account = await _accounts.GetByIdAsync(request.AccountId, ct);
        if (account is null)
        {
            return Result.Failure($"Account {request.AccountId} not found for compensation request {request.Id}.");
        }

        if (account.CurrentStage is not { } currentStage)
        {
            return Result.Failure($"Account {account.Id} has no current stage — cannot apply the down-transition.");
        }

        await ConfirmPaymentAsync(account, request, currentStage, ct);
        _logger.LogWarning("Manually confirmed stuck compensation payout {RequestId} as Paid (MT5 credit verified by admin).", request.Id);
        return Result.Success();
    }

    /// <summary>
    /// Admin reconciliation: the MT5 credit for a stuck <see cref="CompensationRequestStatus.Paying"/>
    /// request has been VERIFIED to have NOT landed, so revert Paying → Approved and let the next payout
    /// run credit it cleanly. Only valid from Paying. Do NOT use this without confirming against MT5 —
    /// resetting a request whose credit actually landed causes a double payment.
    /// </summary>
    public async Task<Result> ResetStuckPayoutAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _compensation.GetRequestByIdAsync(requestId, ct);
        if (request is null)
        {
            return Result.Failure("Request not found.");
        }

        if (request.Status != CompensationRequestStatus.Paying)
        {
            return Result.Failure($"Only a request stuck in Paying can be reset to Approved; this one is {request.Status}.");
        }

        request.Status = CompensationRequestStatus.Approved;
        await _compensation.SaveChangesAsync(ct);
        _logger.LogWarning("Manually reset stuck compensation payout {RequestId} to Approved (MT5 credit verified absent by admin).", request.Id);
        return Result.Success();
    }

    /// <summary>
    /// Phase 3 of a payout, shared by the automated job and admin reconciliation: record the ledger
    /// entry FIRST, update the local composition, apply the down-stage transition, bump the per-person
    /// cap tracker, and mark the request Paid — all committed as one atomic transaction. Never touches
    /// MT5; the caller owns the (already-completed) credit.
    /// </summary>
    private async Task ConfirmPaymentAsync(
        ShieldAccount account, Domain.Compensation.CompensationRequest request, StageLevel currentStage, CancellationToken ct)
    {
        await _accounts.AddLedgerEntryAsync(new LedgerEntry
        {
            AccountId = account.Id,
            Bucket = BalanceBucket.Compensation,
            Reason = LedgerEntryReason.Compensation,
            Amount = request.CappedAmount,
            RelatedRequestId = request.Id,
            Note = $"Compensation payout for {request.StageAtRequest}",
        }, ct);

        account.Composition = account.Composition.AddCompensation(request.CappedAmount);

        // Apply the down-stage transition and record it.
        var transition = StageMachine.ResolveDownAfterCompensation(currentStage);

        if (transition.Exited)
        {
            account.Status = AccountStatus.Exited;
            account.CurrentStage = null;
        }
        else
        {
            account.CurrentStage = transition.To;
        }

        await _accounts.AddStageTransitionAsync(new StageTransitionRecord
        {
            AccountId = account.Id,
            From = transition.From,
            To = transition.Exited ? null : transition.To,
            Direction = transition.Direction,
            Exited = transition.Exited,
            Reason = transition.Reason,
        }, ct);

        // Update the per-person lifetime cap tracker.
        var tracker = await _compensation.GetCapTrackerAsync(account.UserId, ct);
        if (tracker is null)
        {
            await _compensation.AddCapTrackerAsync(new CompensationCapTracker
            {
                UserId = account.UserId,
                LifetimeCompensationPaid = request.CappedAmount,
            }, ct);
        }
        else
        {
            tracker.LifetimeCompensationPaid += request.CappedAmount;
            tracker.UpdatedAtUtc = DateTime.UtcNow;
        }

        // Mark the request paid (Paying → Paid).
        request.Status = CompensationRequestStatus.Paid;
        request.PaidAtUtc = DateTime.UtcNow;

        // IShieldAccountRepository and ICompensationRepository share the same scoped DbContext,
        // so this single call commits every phase-3 change (ledger, composition, stage transition,
        // cap tracker, request status) as one atomic transaction.
        await _accounts.SaveChangesAsync(ct);
    }
}
