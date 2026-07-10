using MondShield.Application.Common.Models;
using MondShield.Application.Onboarding;
using MondShield.Domain.Ledger;

namespace MondShield.Application.Mt5;

public sealed class Mt5BalanceOperationService : IMt5BalanceOperationService
{
    private readonly IShieldAccountRepository _accounts;

    public Mt5BalanceOperationService(IShieldAccountRepository accounts)
    {
        _accounts = accounts;
    }

    public Task<IReadOnlyList<Mt5BalanceOperationView>> GetPendingAsync(CancellationToken ct = default) =>
        _accounts.GetPendingBalanceOperationsAsync(ct);

    public async Task<Result> ClassifyAsync(Guid operationId, BalanceBucket bucket, string? note, CancellationToken ct = default)
    {
        var op = await _accounts.GetBalanceOperationByIdAsync(operationId, ct);
        if (op is null)
        {
            return Result.Failure("Balance operation not found.");
        }

        if (op.Status != Mt5BalanceOperationStatus.PendingReview)
        {
            return Result.Failure($"Only a PendingReview balance operation can be classified; this one is {op.Status}.");
        }

        if (op.Amount <= 0m)
        {
            return Result.Failure("Only a positive (incoming) balance operation can be classified into a bucket; ignore withdrawals instead.");
        }

        if (!TryMapReason(bucket, out var reason))
        {
            return Result.Failure($"Balance operations cannot be classified into the {bucket} bucket.");
        }

        var account = await _accounts.GetByIdAsync(op.AccountId, ct);
        if (account is null)
        {
            return Result.Failure($"Account {op.AccountId} not found for balance operation {op.Id}.");
        }

        // Credit the chosen bucket in the local composition and write the matching append-only ledger
        // entry — the same shape every other money movement produces — so the ledger total moves back
        // into line with the MT5 balance this deal already changed.
        account.Composition = bucket switch
        {
            BalanceBucket.InsuredCapital => account.Composition.AddInsuredCapital(op.Amount),
            BalanceBucket.Compensation => account.Composition.AddCompensation(op.Amount),
            BalanceBucket.Profit => account.Composition.AddProfit(op.Amount),
            _ => account.Composition, // unreachable: TryMapReason already rejected other buckets
        };

        var entry = new LedgerEntry
        {
            AccountId = account.Id,
            Bucket = bucket,
            Reason = reason,
            Amount = op.Amount,
            Note = note is { Length: > 0 }
                ? $"MT5 balance op {op.DealId} classified: {note}"
                : $"MT5 balance op {op.DealId} classified as {bucket}",
        };
        await _accounts.AddLedgerEntryAsync(entry, ct);

        op.Status = Mt5BalanceOperationStatus.Applied;
        op.ClassifiedBucket = bucket;
        op.LedgerEntryId = entry.Id;
        op.ResolvedAtUtc = DateTime.UtcNow;
        op.ResolutionNote = note;

        await _accounts.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> IgnoreAsync(Guid operationId, string? note, CancellationToken ct = default)
    {
        var op = await _accounts.GetBalanceOperationByIdAsync(operationId, ct);
        if (op is null)
        {
            return Result.Failure("Balance operation not found.");
        }

        if (op.Status != Mt5BalanceOperationStatus.PendingReview)
        {
            return Result.Failure($"Only a PendingReview balance operation can be ignored; this one is {op.Status}.");
        }

        op.Status = Mt5BalanceOperationStatus.Ignored;
        op.ResolvedAtUtc = DateTime.UtcNow;
        op.ResolutionNote = note;

        await _accounts.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Maps an incoming-money bucket to its ledger reason. Commission is rejected — commission is
    /// deducted from trades, never deposited as a balance operation.
    /// </summary>
    private static bool TryMapReason(BalanceBucket bucket, out LedgerEntryReason reason)
    {
        switch (bucket)
        {
            case BalanceBucket.InsuredCapital:
                reason = LedgerEntryReason.Deposit;
                return true;
            case BalanceBucket.Compensation:
                reason = LedgerEntryReason.Compensation;
                return true;
            case BalanceBucket.Profit:
                reason = LedgerEntryReason.TradingProfit;
                return true;
            default:
                reason = default;
                return false;
        }
    }
}
