using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MondShield.Api.Contracts;
using MondShield.Application.Common.Interfaces;
using MondShield.Application.Onboarding;
using MondShield.Application.Withdrawals;
using MondShield.Application.Withdrawals.Dtos;
using MondShield.Domain.Authorization;

namespace MondShield.Api.Controllers;

/// <summary>Trader-facing profit-withdrawal actions, scoped to the caller's own account.</summary>
[ApiController]
[Route("api/withdrawals")]
[Authorize(Policy = Policies.AuthenticatedUser)]
[Produces("application/json")]
public sealed class WithdrawalsController : ControllerBase
{
    private readonly IProfitWithdrawalService _withdrawals;
    private readonly IProfitWithdrawalRepository _withdrawalRepository;
    private readonly IShieldAccountRepository _accounts;
    private readonly ICurrentUser _currentUser;

    public WithdrawalsController(
        IProfitWithdrawalService withdrawals,
        IProfitWithdrawalRepository withdrawalRepository,
        IShieldAccountRepository accounts,
        ICurrentUser currentUser)
    {
        _withdrawals = withdrawals;
        _withdrawalRepository = withdrawalRepository;
        _accounts = accounts;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Request a profit withdrawal from the caller's account. Computes and shows the
    /// broker-share split; no money moves until an admin marks it completed.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProfitWithdrawalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfitWithdrawalResponse>> RequestWithdrawal([FromBody] RequestWithdrawalRequest request, CancellationToken ct)
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

        var result = await _withdrawals.RequestAsync(account.Id, request.RequestedAmount, ct);
        if (!result.Succeeded)
        {
            return BadRequest(new ErrorResponse(result.Errors));
        }

        var created = await _withdrawalRepository.GetByIdAsync(result.Value, ct);
        return Ok(ProfitWithdrawalResponse.From(created!));
    }

    /// <summary>List all of the caller's own withdrawal requests, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ProfitWithdrawalResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProfitWithdrawalResponse>>> ListMine(CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        var account = await _accounts.GetByUserIdAsync(userId, ct);
        if (account is null)
        {
            return Ok(Array.Empty<ProfitWithdrawalResponse>());
        }

        var withdrawals = await _withdrawalRepository.GetByAccountIdAsync(account.Id, ct);
        return Ok(withdrawals.Select(ProfitWithdrawalResponse.From));
    }

    /// <summary>View one of the caller's own withdrawal requests.</summary>
    [HttpGet("{withdrawalId:guid}")]
    [ProducesResponseType(typeof(ProfitWithdrawalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfitWithdrawalResponse>> Get(Guid withdrawalId, CancellationToken ct)
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

        var withdrawal = await _withdrawalRepository.GetByIdAsync(withdrawalId, ct);
        if (withdrawal is null || withdrawal.AccountId != account.Id)
        {
            return NotFound();
        }

        return Ok(ProfitWithdrawalResponse.From(withdrawal));
    }
}
