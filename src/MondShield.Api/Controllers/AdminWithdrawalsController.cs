using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MondShield.Api.Contracts;
using MondShield.Application.Withdrawals;
using MondShield.Domain.Authorization;

namespace MondShield.Api.Controllers;

/// <summary>Admin actions for the manual profit-withdrawal worklist.</summary>
[ApiController]
[Route("api/admin/withdrawals")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminWithdrawalsController : ControllerBase
{
    private readonly IProfitWithdrawalService _withdrawals;
    private readonly IProfitWithdrawalRepository _withdrawalRepository;

    public AdminWithdrawalsController(IProfitWithdrawalService withdrawals, IProfitWithdrawalRepository withdrawalRepository)
    {
        _withdrawals = withdrawals;
        _withdrawalRepository = withdrawalRepository;
    }

    /// <summary>Look up any withdrawal request by id — including the broker-share split to execute in MT5.</summary>
    [HttpGet("{withdrawalId:guid}")]
    [ProducesResponseType(typeof(ProfitWithdrawalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfitWithdrawalResponse>> Get(Guid withdrawalId, CancellationToken ct)
    {
        var withdrawal = await _withdrawalRepository.GetByIdAsync(withdrawalId, ct);
        return withdrawal is null ? NotFound() : Ok(ProfitWithdrawalResponse.From(withdrawal));
    }

    /// <summary>Withdrawals awaiting manual execution in MT5 — the manual-withdrawal worklist.</summary>
    [HttpGet("queue/pending")]
    [ProducesResponseType(typeof(IEnumerable<ProfitWithdrawalResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProfitWithdrawalResponse>>> PendingQueue(CancellationToken ct)
    {
        var withdrawals = await _withdrawalRepository.GetPendingAsync(ct);
        return Ok(withdrawals.Select(ProfitWithdrawalResponse.From));
    }

    /// <summary>Mark a withdrawal completed after executing it manually in the MT5 Manager terminal.</summary>
    [HttpPost("{withdrawalId:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Complete(Guid withdrawalId, CancellationToken ct)
    {
        var result = await _withdrawals.CompleteAsync(withdrawalId, ct);
        return result.Succeeded ? NoContent() : BadRequest(new ErrorResponse(result.Errors));
    }
}
