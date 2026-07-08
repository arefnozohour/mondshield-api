using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MondShield.Api.Contracts;
using MondShield.Application.Accounts;
using MondShield.Application.Common.Interfaces;
using MondShield.Application.Mt5;
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
    private readonly IMt5AccountInfoService _mt5Info;
    private readonly ICurrentUser _currentUser;

    public AccountsController(
        IShieldAccountRepository accounts,
        IAccountActivityService activity,
        IMt5AccountInfoService mt5Info,
        ICurrentUser currentUser)
    {
        _accounts = accounts;
        _activity = activity;
        _mt5Info = mt5Info;
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
    /// The caller's own MT5 trading-account credentials: login, server, and the generated main +
    /// investor passwords, so the trader can log into the MT5 terminal. Sensitive — scoped to the
    /// caller's own account only.
    /// </summary>
    [HttpGet("me/mt5")]
    [ProducesResponseType(typeof(Mt5AccountInfoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Mt5AccountInfoResponse>> MyMt5(CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        var info = await _mt5Info.GetForUserAsync(userId, ct);
        return info is null ? NotFound() : Ok(Mt5AccountInfoResponse.From(info));
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
