using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using MondShield.Api.Contracts;
using MondShield.Application.Mt5;
using MondShield.Domain.Authorization;
using MondShield.Domain.Ledger;

namespace MondShield.Api.Controllers;

/// <summary>Admin diagnostics and balance-operation review for the MT5 integration.</summary>
[ApiController]
[Route("api/admin/mt5")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminMt5Controller : ControllerBase
{
    private readonly IMt5Client _mt5;
    private readonly IMt5BalanceOperationService _balanceOps;
    private readonly IWebHostEnvironment _env;

    public AdminMt5Controller(IMt5Client mt5, IMt5BalanceOperationService balanceOps, IWebHostEnvironment env)
    {
        _mt5 = mt5;
        _balanceOps = balanceOps;
        _env = env;
    }

    /// <summary>
    /// Connection health check. In Live mode this forces a real Manager API connect and reports
    /// success or the exact failure reason; in Stub mode it always reports healthy.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(Mt5ConnectionStatus), StatusCodes.Status200OK)]
    public async Task<ActionResult<Mt5ConnectionStatus>> Status(CancellationToken ct)
    {
        var status = await _mt5.CheckConnectionAsync(ct);
        return Ok(status);
    }

    /// <summary>
    /// The classification worklist: external MT5 balance operations (trader top-ups, manual dealer
    /// ops) reconciliation captured but that we could not auto-attribute. Each needs an admin to
    /// classify it into a bucket or ignore it — until then it is money our ledger has seen but not
    /// booked, showing up as reconciliation drift.
    /// </summary>
    [HttpGet("balance-operations/pending")]
    [ProducesResponseType(typeof(IEnumerable<Mt5BalanceOperationResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<Mt5BalanceOperationResponse>>> PendingBalanceOperations(CancellationToken ct)
    {
        var pending = await _balanceOps.GetPendingAsync(ct);
        return Ok(pending.Select(Mt5BalanceOperationResponse.From));
    }

    /// <summary>
    /// Classify a pending external deposit into a composition bucket (InsuredCapital, Compensation,
    /// or Profit): books the ledger entry, credits the bucket, and closes the drift it caused.
    /// </summary>
    [HttpPost("balance-operations/{operationId:guid}/classify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClassifyBalanceOperation(Guid operationId, [FromBody] ClassifyBalanceOperationRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<BalanceBucket>(request.Bucket, ignoreCase: true, out var bucket))
        {
            return BadRequest(new ErrorResponse([$"'{request.Bucket}' is not a valid bucket. Use InsuredCapital, Compensation, or Profit."]));
        }

        var result = await _balanceOps.ClassifyAsync(operationId, bucket, request.Note, ct);
        return result.Succeeded ? NoContent() : BadRequest(new ErrorResponse(result.Errors));
    }

    /// <summary>
    /// Acknowledge a pending external balance operation without booking it (e.g. a manual withdrawal
    /// that belongs to the profit-withdrawal flow). Marks it Ignored; its drift stays visible.
    /// </summary>
    [HttpPost("balance-operations/{operationId:guid}/ignore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IgnoreBalanceOperation(Guid operationId, [FromBody] IgnoreBalanceOperationRequest request, CancellationToken ct)
    {
        var result = await _balanceOps.IgnoreAsync(operationId, request.Note, ct);
        return result.Succeeded ? NoContent() : BadRequest(new ErrorResponse(result.Errors));
    }

    /// <summary>
    /// TEST SEAM (Development + Stub only; 404 otherwise): simulate money moving on an MT5 login
    /// outside our flows, so the next reconciliation captures it as a PendingReview balance op. Lets
    /// the reconcile → capture → classify pipeline be exercised without a live server.
    /// </summary>
    [HttpPost("stub/simulate-external-balance-op")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult SimulateExternalBalanceOp([FromBody] SimulateExternalBalanceOpRequest request)
    {
        if (!_env.IsDevelopment() || _mt5 is not IMt5TestHarness harness)
        {
            return NotFound();
        }

        harness.SimulateExternalBalanceOperation(request.Login, request.Amount, request.Comment ?? "external test deposit");
        return NoContent();
    }
}
