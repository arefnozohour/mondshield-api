using Microsoft.Extensions.Logging;
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
                // Left as Approved (not Paid) on failure, so it's picked up again on the next run.
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

        // 1. Record the ledger entry FIRST, then update the local composition.
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

        // 2. Credit MT5 via the Manager API (stub for now).
        await _mt5.CreditBalanceAsync(mt5Login, request.CappedAmount, $"MondShield compensation - {request.StageAtRequest}", ct);

        // 3. Apply the down-stage transition and record it.
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

        // 4. Update the per-person lifetime cap tracker.
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

        // 5. Mark the request paid.
        request.Status = CompensationRequestStatus.Paid;
        request.PaidAtUtc = DateTime.UtcNow;

        // IShieldAccountRepository and ICompensationRepository share the same scoped DbContext,
        // so this single call commits every change above (ledger, composition, stage transition,
        // cap tracker, request status) as one atomic transaction.
        await _accounts.SaveChangesAsync(ct);
    }
}
