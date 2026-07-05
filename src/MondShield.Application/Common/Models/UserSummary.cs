namespace MondShield.Application.Common.Models;

/// <summary>
/// A non-sensitive projection of a login account (no password hash / refresh token), for
/// showing who the caller is or joining trader identity onto admin views.
/// </summary>
public sealed record UserSummary(Guid Id, string Email, string FullName, string Role);
