using System.ComponentModel.DataAnnotations;

namespace MondShield.Application.Withdrawals.Dtos;

/// <summary>The total amount the trader wants to withdraw.</summary>
public sealed record RequestWithdrawalRequest
{
    [Range(0.01, double.MaxValue)]
    public decimal RequestedAmount { get; init; }
}
