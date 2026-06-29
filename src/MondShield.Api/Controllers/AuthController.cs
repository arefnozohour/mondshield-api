using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MondShield.Application.Authentication.Dtos;
using MondShield.Application.Common.Interfaces;
using MondShield.Domain.Authorization;

namespace MondShield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IIdentityService _identityService;
    private readonly ICurrentUser _currentUser;

    public AuthController(IIdentityService identityService, ICurrentUser currentUser)
    {
        _identityService = identityService;
        _currentUser = currentUser;
    }

    /// <summary>Register a new trader account (assigned the User role) and return a token pair.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _identityService.RegisterAsync(request, ct);
        return result.Succeeded
            ? Ok(result.Value)
            : BadRequest(new { errors = result.Errors });
    }

    /// <summary>Email + password login.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _identityService.LoginAsync(request, ct);
        return result.Succeeded
            ? Ok(result.Value)
            : Unauthorized(new { errors = result.Errors });
    }

    /// <summary>Exchange a valid refresh token for a fresh access/refresh token pair.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await _identityService.RefreshAsync(request, ct);
        return result.Succeeded
            ? Ok(result.Value)
            : Unauthorized(new { errors = result.Errors });
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized();
        }

        return Ok(new
        {
            userId = _currentUser.UserId,
            email = _currentUser.Email,
            isAdmin = _currentUser.IsInRole(Roles.Admin),
        });
    }
}
