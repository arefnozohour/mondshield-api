using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MondShield.Api.Contracts;
using MondShield.Application.Common.Interfaces;
using MondShield.Application.Compensation;
using MondShield.Application.Compensation.Dtos;
using MondShield.Application.Onboarding;
using MondShield.Domain.Authorization;

namespace MondShield.Api.Controllers;

/// <summary>Trader-facing compensation ticket actions, scoped to the caller's own account.</summary>
[ApiController]
[Route("api/compensation-requests")]
[Authorize(Policy = Policies.AuthenticatedUser)]
[Produces("application/json")]
public sealed class CompensationController : ControllerBase
{
    private readonly ICompensationService _compensation;
    private readonly ICompensationRepository _compensationRepository;
    private readonly IShieldAccountRepository _accounts;
    private readonly ICurrentUser _currentUser;

    public CompensationController(
        ICompensationService compensation,
        ICompensationRepository compensationRepository,
        IShieldAccountRepository accounts,
        ICurrentUser currentUser)
    {
        _compensation = compensation;
        _compensationRepository = compensationRepository;
        _accounts = accounts;
        _currentUser = currentUser;
    }

    /// <summary>Submit a loss-compensation request for the caller's current stage.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CompensationRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CompensationRequestResponse>> Submit([FromBody] SubmitCompensationRequest request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        var account = await _accounts.GetByUserIdAsync(userId, ct);
        if (account is null)
        {
            return NotFound();
        }

        var result = await _compensation.SubmitAsync(account.Id, request.TotalTradingLoss, request.CommissionPaid, ct);
        if (!result.Succeeded)
        {
            return BadRequest(new ErrorResponse(result.Errors));
        }

        var created = await _compensationRepository.GetRequestByIdAsync(result.Value, ct);
        return Ok(CompensationRequestResponse.From(created!));
    }

    /// <summary>List all of the caller's own compensation requests, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CompensationRequestResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CompensationRequestResponse>>> ListMine(CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        var account = await _accounts.GetByUserIdAsync(userId, ct);
        if (account is null)
        {
            return Ok(Array.Empty<CompensationRequestResponse>());
        }

        var requests = await _compensationRepository.GetByAccountIdAsync(account.Id, ct);
        return Ok(requests.Select(CompensationRequestResponse.From));
    }

    /// <summary>View one of the caller's own compensation requests.</summary>
    [HttpGet("{requestId:guid}")]
    [ProducesResponseType(typeof(CompensationRequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompensationRequestResponse>> Get(Guid requestId, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        var account = await _accounts.GetByUserIdAsync(userId, ct);
        if (account is null)
        {
            return NotFound();
        }

        var request = await _compensationRepository.GetRequestByIdAsync(requestId, ct);
        if (request is null || request.AccountId != account.Id)
        {
            return NotFound();
        }

        return Ok(CompensationRequestResponse.From(request));
    }
}
