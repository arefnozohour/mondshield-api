using MondShield.Application.Common.Models;
using MondShield.Application.Onboarding;
using MondShield.Domain.Accounts;
using MondShield.Domain.Ledger;
using MondShield.Domain.Money;
using MondShield.Domain.Withdrawals;

namespace MondShield.Application.Withdrawals;

public sealed class ProfitWithdrawalService : IProfitWithdrawalService
{
    private readonly IShieldAccountRepository _accounts;
    private readonly IProfitWithdrawalRepository _withdrawals;

    public ProfitWithdrawalService(IShieldAccountRepository accounts, IProfitWithdrawalRepository withdrawals)
    {
        _accounts = accounts;
        _withdrawals = withdrawals;
    }

    public async Task<Result<Guid>> RequestAsync(Guid accountId, decimal requestedAmount, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            return Result<Guid>.Failure("Account not found.");
        }

        if (account.Status != AccountStatus.Active || account.CurrentStage is not { } stage)
        {
            return Result<Guid>.Failure("Account must be active to request a withdrawal.");
        }

        if (requestedAmount > account.Composition.Total)
        {
            return Result<Guid>.Failure("Requested amount exceeds the available balance.");
        }

        var share = ProfitShareCalculator.Calculate(stage, requestedAmount, account.Composition.Profit);

        var withdrawal = new ProfitWithdrawal
        {
            AccountId = accountId,
            RequestedAmount = share.RequestedAmount,
            ProfitPortion = share.ProfitPortion,
            NonProfitPortion = share.NonProfitPortion,
            BrokerShareAmount = share.BrokerShareAmount,
            NetToTrader = share.NetToTrader,
        };

        await _withdrawals.AddAsync(withdrawal, ct);
        await _withdrawals.SaveChangesAsync(ct);

        return Result<Guid>.Success(withdrawal.Id);
    }

    public async Task<Result> CompleteAsync(Guid withdrawalId, CancellationToken ct = default)
    {
        var withdrawal = await _withdrawals.GetByIdAsync(withdrawalId, ct);
        if (withdrawal is null)
        {
            return Result.Failure("Withdrawal not found.");
        }

        if (withdrawal.Status != ProfitWithdrawalStatus.Requested)
        {
            return Result.Failure($"Cannot complete from status {withdrawal.Status}.");
        }

        var account = await _accounts.GetByIdAsync(withdrawal.AccountId, ct)
            ?? throw new InvalidOperationException($"Account {withdrawal.AccountId} not found for withdrawal {withdrawal.Id}.");

        if (withdrawal.RequestedAmount > account.Composition.Total)
        {
            return Result.Failure("Account balance is no longer sufficient to complete this withdrawal.");
        }

        // Debit against the account's CURRENT composition (not the frozen request-time
        // snapshot) — BalanceComposition.Withdraw applies the profit → compensation → capital
        // priority itself; we only need the before/after deltas to write accurate ledger entries.
        var before = account.Composition;
        account.Composition = before.Withdraw(withdrawal.RequestedAmount);
        var after = account.Composition;

        await WriteLedgerEntriesAsync(account.Id, before, after, withdrawal.Id, ct);

        withdrawal.Status = ProfitWithdrawalStatus.Completed;
        withdrawal.CompletedAtUtc = DateTime.UtcNow;

        await _accounts.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task WriteLedgerEntriesAsync(
        Guid accountId, BalanceComposition before, BalanceComposition after, Guid withdrawalId, CancellationToken ct)
    {
        var profitDelta = before.Profit - after.Profit;
        var compensationDelta = before.Compensation - after.Compensation;
        var capitalDelta = before.InsuredCapital - after.InsuredCapital;

        if (profitDelta > 0m)
        {
            await _accounts.AddLedgerEntryAsync(new LedgerEntry
            {
                AccountId = accountId,
                Bucket = BalanceBucket.Profit,
                Reason = LedgerEntryReason.Withdrawal,
                Amount = -profitDelta,
                RelatedRequestId = withdrawalId,
            }, ct);
        }

        if (compensationDelta > 0m)
        {
            await _accounts.AddLedgerEntryAsync(new LedgerEntry
            {
                AccountId = accountId,
                Bucket = BalanceBucket.Compensation,
                Reason = LedgerEntryReason.Withdrawal,
                Amount = -compensationDelta,
                RelatedRequestId = withdrawalId,
            }, ct);
        }

        if (capitalDelta > 0m)
        {
            await _accounts.AddLedgerEntryAsync(new LedgerEntry
            {
                AccountId = accountId,
                Bucket = BalanceBucket.InsuredCapital,
                Reason = LedgerEntryReason.Withdrawal,
                Amount = -capitalDelta,
                RelatedRequestId = withdrawalId,
            }, ct);
        }
    }
}
