namespace MondShield.Api.Contracts;

/// <summary>The shape returned for every validation/business-rule failure (400 Bad Request).</summary>
public sealed record ErrorResponse(IReadOnlyList<string> Errors);
