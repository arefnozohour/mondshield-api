using MondShield.Application.Common.Models;
using MondShield.Application.Onboarding;
using MondShield.Domain.Accounts;
using MondShield.Domain.Compensation;
using MondShield.Domain.Money;

namespace MondShield.Application.Compensation;

public sealed class CompensationService : ICompensationService
{
    private readonly IShieldAccountRepository _accounts;
    private readonly ICompensationRepository _compensation;

    public CompensationService(IShieldAccountRepository accounts, ICompensationRepository compensation)
    {
        _accounts = accounts;
        _compensation = compensation;
    }

    public async Task<Result<Guid>> SubmitAsync(
        Guid accountId, decimal totalTradingLoss, decimal commissionPaid, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            return Result<Guid>.Failure("Account not found.");
        }

        if (account.Status != AccountStatus.Active || account.CurrentStage is not { } stage)
        {
            return Result<Guid>.Failure("Account must be active to submit a compensation request.");
        }

        if (await _compensation.HasRequestForStageAsync(accountId, stage, ct))
        {
            return Result<Guid>.Failure($"A compensation request has already been submitted for stage {stage}.");
        }

        var tracker = await _compensation.GetCapTrackerAsync(account.UserId, ct);
        var alreadyPaid = tracker?.LifetimeCompensationPaid ?? 0m;

        CompensationResult calculation;
        try
        {
            calculation = CompensationCalculator.Calculate(stage, totalTradingLoss, commissionPaid, alreadyPaid);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }

        var request = new Domain.Compensation.CompensationRequest
        {
            AccountId = accountId,
            StageAtRequest = stage,
            LossAtRequest = totalTradingLoss,
            CommissionExcluded = commissionPaid,
            ComputedCoverage = calculation.ComputedCoverage,
            CappedAmount = calculation.PayableAmount,
            CapReached = calculation.CapReached,
        };

        await _compensation.AddRequestAsync(request, ct);
        await _compensation.SaveChangesAsync(ct);

        return Result<Guid>.Success(request.Id);
    }

    public async Task<Result> StartReviewAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _compensation.GetRequestByIdAsync(requestId, ct);
        if (request is null)
        {
            return Result.Failure("Request not found.");
        }

        if (request.Status != CompensationRequestStatus.Submitted)
        {
            return Result.Failure($"Cannot start review from status {request.Status}.");
        }

        request.Status = CompensationRequestStatus.UnderReview;
        await _compensation.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ApproveAsync(Guid requestId, string? reviewerNote, CancellationToken ct = default)
    {
        var request = await _compensation.GetRequestByIdAsync(requestId, ct);
        if (request is null)
        {
            return Result.Failure("Request not found.");
        }

        if (request.Status != CompensationRequestStatus.UnderReview)
        {
            return Result.Failure($"Cannot approve from status {request.Status}.");
        }

        request.Status = CompensationRequestStatus.Approved;
        request.ReviewerNote = reviewerNote;
        request.ScheduledPayoutDateUtc = PayoutSchedule.NextPayoutDateUtc(DateTime.UtcNow);

        await _compensation.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RejectAsync(Guid requestId, string? reviewerNote, CancellationToken ct = default)
    {
        var request = await _compensation.GetRequestByIdAsync(requestId, ct);
        if (request is null)
        {
            return Result.Failure("Request not found.");
        }

        if (request.Status != CompensationRequestStatus.UnderReview)
        {
            return Result.Failure($"Cannot reject from status {request.Status}.");
        }

        request.Status = CompensationRequestStatus.Rejected;
        request.ReviewerNote = reviewerNote;

        await _compensation.SaveChangesAsync(ct);
        return Result.Success();
    }
}
