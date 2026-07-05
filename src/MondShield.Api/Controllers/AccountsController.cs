using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MondShield.Api.Contracts;
using MondShield.Application.Accounts;
using MondShield.Application.Common.Interfaces;
using MondShield.Application.Onboarding;
using MondShield.Domain.Authorization;

namespace MondShield.Api.Controllers;

[ApiController]
[Route("api/accounts")]
[Authorize(Policy = Policies.AuthenticatedUser)]
[Produces("application/json")]
public sealed class AccountsController : ControllerBase
{
    private readonly IShieldAccountRepository _accounts;
    private readonly IAccountActivityService _activity;
    private readonly ICurrentUser _currentUser;

    public AccountsController(
        IShieldAccountRepository accounts,
        IAccountActivityService activity,
        ICurrentUser currentUser)
    {
        _accounts = accounts;
        _activity = activity;
        _currentUser = currentUser;
    }

    /// <summary>The caller's own MondShield account: onboarding status, stage, and balance composition.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountResponse>> Me(CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        var account = await _accounts.GetByUserIdAsync(userId, ct);
        return account is null ? NotFound() : Ok(AccountResponse.From(account));
    }

    /// <summary>
    /// The caller's own unified activity feed — deposits, compensation, profit, commission,
    /// withdrawals, and stage changes, newest first.
    /// </summary>
    [HttpGet("me/activity")]
    [ProducesResponseType(typeof(IEnumerable<ActivityEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<ActivityEntryResponse>>> MyActivity(CancellationToken ct)
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

        var activity = await _activity.GetActivityAsync(account.Id, ct);
        return Ok(activity.Select(ActivityEntryResponse.From));
    }
}
