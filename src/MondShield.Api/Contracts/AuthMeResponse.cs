namespace MondShield.Api.Contracts;

/// <summary>The authenticated caller's identity context.</summary>
public sealed record AuthMeResponse(Guid? UserId, string? Email, string? FullName, bool IsAdmin);
