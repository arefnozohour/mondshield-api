using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MondShield.Api.Contracts;
using MondShield.Application.Compensation;
using MondShield.Application.Compensation.Dtos;
using MondShield.Domain.Authorization;

namespace MondShield.Api.Controllers;

/// <summary>Admin review actions for the compensation ticket pipeline.</summary>
[ApiController]
[Route("api/admin/compensation-requests")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminCompensationController : ControllerBase
{
    private readonly ICompensationService _compensation;
    private readonly ICompensationRepository _compensationRepository;
    private readonly IPayoutService _payout;

    public AdminCompensationController(
        ICompensationService compensation, ICompensationRepository compensationRepository, IPayoutService payout)
    {
        _compensation = compensation;
        _compensationRepository = compensationRepository;
        _payout = payout;
    }

    /// <summary>Look up any compensation request by id.</summary>
    [HttpGet("{requestId:guid}")]
    [ProducesResponseType(typeof(CompensationRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompensationRequestResponse>> Get(Guid requestId, CancellationToken ct)
    {
        var request = await _compensationRepository.GetRequestByIdAsync(requestId, ct);
        return request is null ? NotFound() : Ok(CompensationRequestResponse.From(request));
    }

    /// <summary>Requests awaiting a decision (Submitted or UnderReview) — the review queue.</summary>
    [HttpGet("queue/review")]
    [ProducesResponseType(typeof(IEnumerable<CompensationRequestResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CompensationRequestResponse>>> ReviewQueue(CancellationToken ct)
    {
        var requests = await _compensationRepository.GetReviewQueueAsync(ct);
        return Ok(requests.Select(CompensationRequestResponse.From));
    }

    /// <summary>Approved requests whose scheduled payout date has arrived — preview of what the next job run will pay.</summary>
    [HttpGet("queue/due-for-payout")]
    [ProducesResponseType(typeof(IEnumerable<CompensationRequestResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CompensationRequestResponse>>> DueForPayout(CancellationToken ct)
    {
        var requests = await _compensationRepository.GetDueForPayoutAsync(DateTime.UtcNow, ct);
        return Ok(requests.Select(CompensationRequestResponse.From));
    }

    /// <summary>Every person's lifetime compensation total against the $5,000 cap — cap monitoring.</summary>
    [HttpGet("cap-trackers")]
    [ProducesResponseType(typeof(IEnumerable<CapTrackerResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CapTrackerResponse>>> CapTrackers(CancellationToken ct)
    {
        var trackers = await _compensationRepository.GetAllCapTrackersAsync(ct);
        return Ok(trackers.Select(CapTrackerResponse.From));
    }

    /// <summary>Start reviewing: Submitted → UnderReview.</summary>
    [HttpPost("{requestId:guid}/start-review")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartReview(Guid requestId, CancellationToken ct)
    {
        var result = await _compensation.StartReviewAsync(requestId, ct);
        return result.Succeeded ? NoContent() : BadRequest(new ErrorResponse(result.Errors));
    }

    /// <summary>Approve: UnderReview → Approved. Schedules the next 27th/28th payout date.</summary>
    [HttpPost("{requestId:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Approve(Guid requestId, [FromBody] ReviewDecisionRequest request, CancellationToken ct)
    {
        var result = await _compensation.ApproveAsync(requestId, request.ReviewerNote, ct);
        return result.Succeeded ? NoContent() : BadRequest(new ErrorResponse(result.Errors));
    }

    /// <summary>Reject: UnderReview → Rejected.</summary>
    [HttpPost("{requestId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reject(Guid requestId, [FromBody] ReviewDecisionRequest request, CancellationToken ct)
    {
        var result = await _compensation.RejectAsync(requestId, request.ReviewerNote, ct);
        return result.Succeeded ? NoContent() : BadRequest(new ErrorResponse(result.Errors));
    }

    /// <summary>
    /// Manually run the payout job now, instead of waiting for the scheduled 27th. For ops
    /// override use — the same job Hangfire runs automatically every month.
    /// </summary>
    [HttpPost("run-payout-job")]
    [ProducesResponseType(typeof(RunPayoutJobResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<RunPayoutJobResponse>> RunPayoutJob(CancellationToken ct)
    {
        var paidCount = await _payout.ProcessDuePayoutsAsync(ct);
        return Ok(new RunPayoutJobResponse(paidCount));
    }
}
