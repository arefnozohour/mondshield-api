using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MondShield.Api.Contracts;
using MondShield.Application.Authentication.Dtos;
using MondShield.Application.Common.Interfaces;
using MondShield.Application.Onboarding;
using MondShield.Domain.Authorization;

namespace MondShield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IIdentityService _identityService;
    private readonly IOnboardingService _onboardingService;
    private readonly ICurrentUser _currentUser;

    public AuthController(IIdentityService identityService, IOnboardingService onboardingService, ICurrentUser currentUser)
    {
        _identityService = identityService;
        _onboardingService = onboardingService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Register a new trader account (assigned the User role), create and provision their
    /// MondShield + MT5 account, activate it at Stage 1 with the standard $2,000 insured capital,
    /// and return a token pair.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResult>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _identityService.RegisterAsync(request, ct);
        if (!result.Succeeded)
        {
            return BadRequest(new ErrorResponse(result.Errors));
        }

        // Provision the MT5 account and activate at Stage 1 with $2,000 right at sign-up.
        var onboarding = await _onboardingService.RegisterTraderAsync(
            result.Value!.UserId, request.FullName, request.Email, ct);
        if (!onboarding.Succeeded)
        {
            return BadRequest(new ErrorResponse(onboarding.Errors));
        }

        return Ok(result.Value);
    }

    /// <summary>Email + password login.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResult>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _identityService.LoginAsync(request, ct);
        return result.Succeeded
            ? Ok(result.Value)
            : Unauthorized(new ErrorResponse(result.Errors));
    }

    /// <summary>Exchange a valid refresh token for a fresh access/refresh token pair.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResult>> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await _identityService.RefreshAsync(request, ct);
        return result.Succeeded
            ? Ok(result.Value)
            : Unauthorized(new ErrorResponse(result.Errors));
    }

    /// <summary>Revoke the caller's active refresh token (logout).</summary>
    [HttpPost("logout")]
    [Authorize(Policy = Policies.AuthenticatedUser)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        await _identityService.RevokeRefreshTokenAsync(userId, ct);
        return NoContent();
    }

    /// <summary>Return the authenticated caller's identity context.</summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.AuthenticatedUser)]
    [ProducesResponseType(typeof(AuthMeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthMeResponse>> Me(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        // FullName isn't a JWT claim (the token stays minimal), so look it up.
        var summary = await _identityService.GetUserSummaryAsync(userId, ct);
        return Ok(new AuthMeResponse(
            userId,
            _currentUser.Email ?? summary?.Email,
            summary?.FullName,
            _currentUser.IsInRole(Roles.Admin)));
    }
}
