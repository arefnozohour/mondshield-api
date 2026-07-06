using MondShield.Application.Common.Models;
using MondShield.Application.Mt5;
using MondShield.Domain.Accounts;
using MondShield.Domain.Ledger;
using MondShield.Domain.Money;
using MondShield.Domain.Stages;

namespace MondShield.Application.Onboarding;

public sealed class OnboardingService : IOnboardingService
{
    private readonly IShieldAccountRepository _accounts;
    private readonly IMt5Client _mt5;

    public OnboardingService(IShieldAccountRepository accounts, IMt5Client mt5)
    {
        _accounts = accounts;
        _mt5 = mt5;
    }

    public async Task<Result<Guid>> CreateAccountForNewUserAsync(Guid userId, CancellationToken ct = default)
    {
        var existing = await _accounts.GetByUserIdAsync(userId, ct);
        if (existing is not null)
        {
            return Result<Guid>.Failure("An account already exists for this user.");
        }

        var account = new ShieldAccount { UserId = userId };
        await _accounts.AddAsync(account, ct);
        await _accounts.SaveChangesAsync(ct);

        return Result<Guid>.Success(account.Id);
    }

    public async Task<Result> ApproveKycAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            return Result.Failure("Account not found.");
        }

        if (account.Status != AccountStatus.PendingKyc)
        {
            return Result.Failure($"Cannot approve KYC from status {account.Status}.");
        }

        account.Status = AccountStatus.KycApproved;
        await _accounts.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<Mt5ProvisioningResult>> ProvisionMt5Async(
        Guid accountId, string fullName, string email, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            return Result<Mt5ProvisioningResult>.Failure("Account not found.");
        }

        if (account.Status != AccountStatus.KycApproved)
        {
            return Result<Mt5ProvisioningResult>.Failure($"Cannot provision MT5 from status {account.Status}.");
        }

        var creation = await _mt5.CreateAccountAsync(new Mt5AccountCreationRequest(fullName, email), ct);

        account.Mt5Login = creation.Login;
        account.Status = AccountStatus.Provisioned;
        await _accounts.SaveChangesAsync(ct);

        return Result<Mt5ProvisioningResult>.Success(
            new Mt5ProvisioningResult(creation.Login, creation.MainPassword, creation.InvestorPassword));
    }

    public async Task<Result> ActivateAsync(Guid accountId, decimal depositAmount, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            return Result.Failure("Account not found.");
        }

        if (account.Status != AccountStatus.Provisioned)
        {
            return Result.Failure($"Cannot activate from status {account.Status}.");
        }

        if (!StageActivationPolicy.CanActivate(depositAmount))
        {
            return Result.Failure(
                $"Deposit of {depositAmount:0.00} does not meet the {MoneyConstants.ActivationDepositAmount:0.00} activation requirement.");
        }

        account.Composition = account.Composition.AddInsuredCapital(depositAmount);
        account.CurrentStage = StageLevel.Stage1;
        account.Status = AccountStatus.Active;
        account.ActivatedAtUtc = DateTime.UtcNow;

        await _accounts.AddLedgerEntryAsync(new LedgerEntry
        {
            AccountId = account.Id,
            Bucket = BalanceBucket.InsuredCapital,
            Reason = LedgerEntryReason.Deposit,
            Amount = depositAmount,
            Note = "Stage 1 activation deposit",
        }, ct);

        await _accounts.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<string>> LevelUpAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            return Result<string>.Failure("Account not found.");
        }

        if (account.Status != AccountStatus.Active || account.CurrentStage is not { } currentStage)
        {
            return Result<string>.Failure("Account must be active to level up.");
        }

        // Admin confirms the profit target was met (the app doesn't poll trades to verify it).
        var transition = StageMachine.ResolveUp(currentStage, profitTargetMet: true);
        if (!transition.Moved)
        {
            return Result<string>.Failure(transition.Reason);
        }

        account.CurrentStage = transition.To;

        await _accounts.AddStageTransitionAsync(new StageTransitionRecord
        {
            AccountId = account.Id,
            From = transition.From,
            To = transition.To,
            Direction = transition.Direction,
            Exited = false,
            Reason = transition.Reason,
        }, ct);

        await _accounts.SaveChangesAsync(ct);
        return Result<string>.Success(transition.To.ToString());
    }
}
