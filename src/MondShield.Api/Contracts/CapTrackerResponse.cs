using MondShield.Domain.Compensation;
using MondShield.Domain.Money;

namespace MondShield.Api.Contracts;

public sealed record CapTrackerResponse(
    Guid UserId,
    decimal LifetimeCompensationPaid,
    decimal RemainingCap,
    DateTime UpdatedAtUtc)
{
    public static CapTrackerResponse From(CompensationCapTracker t) => new(
        t.UserId,
        t.LifetimeCompensationPaid,
        Math.Max(0m, MoneyConstants.LifetimeCompensationCap - t.LifetimeCompensationPaid),
        t.UpdatedAtUtc);
}
