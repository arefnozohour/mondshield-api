namespace MondShield.Api.Contracts;

/// <summary>How many due compensation requests the payout job just processed.</summary>
public sealed record RunPayoutJobResponse(int PaidCount);
