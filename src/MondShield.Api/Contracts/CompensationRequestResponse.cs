using MondShield.Domain.Compensation;

namespace MondShield.Api.Contracts;

public sealed record CompensationRequestResponse(
    Guid Id,
    Guid AccountId,
    string StageAtRequest,
    decimal LossAtRequest,
    decimal CommissionExcluded,
    decimal ComputedCoverage,
    decimal CappedAmount,
    bool CapReached,
    string Status,
    DateTime SubmittedAtUtc,
    DateTime? ScheduledPayoutDateUtc,
    DateTime? PaidAtUtc,
    string? ReviewerNote)
{
    public static CompensationRequestResponse From(CompensationRequest r) => new(
        r.Id,
        r.AccountId,
        r.StageAtRequest.ToString(),
        r.LossAtRequest,
        r.CommissionExcluded,
        r.ComputedCoverage,
        r.CappedAmount,
        r.CapReached,
        r.Status.ToString(),
        r.SubmittedAtUtc,
        r.ScheduledPayoutDateUtc,
        r.PaidAtUtc,
        r.ReviewerNote);
}
