using System.ComponentModel.DataAnnotations;

namespace MondShield.Application.Compensation.Dtos;

/// <summary>Self-reported trading loss and commission for a new compensation request.</summary>
public sealed record SubmitCompensationRequest
{
    [Range(0, double.MaxValue)]
    public decimal TotalTradingLoss { get; init; }

    [Range(0, double.MaxValue)]
    public decimal CommissionPaid { get; init; }
}

/// <summary>Optional admin note recorded alongside an approve/reject decision.</summary>
public sealed record ReviewDecisionRequest
{
    public string? ReviewerNote { get; init; }
}
