using System.ComponentModel.DataAnnotations;

namespace MondShield.Application.Onboarding.Dtos;

/// <summary>
/// Admin-supplied name/email for the MT5 account being provisioned. Deliberately not read
/// from the trader's signup record — the admin confirms it during KYC, since the legal name
/// on a financial account should reflect verified identity, not unverified signup input.
/// </summary>
public sealed record ProvisionMt5Request
{
    [Required]
    public string FullName { get; init; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;
}

/// <summary>The confirmed activation deposit amount.</summary>
public sealed record ActivateAccountRequest
{
    [Range(0, double.MaxValue)]
    public decimal DepositAmount { get; init; }
}
