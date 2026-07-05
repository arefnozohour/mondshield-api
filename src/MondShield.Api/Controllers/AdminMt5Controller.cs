using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MondShield.Application.Mt5;
using MondShield.Domain.Authorization;

namespace MondShield.Api.Controllers;

/// <summary>Admin diagnostics for the MT5 integration.</summary>
[ApiController]
[Route("api/admin/mt5")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminMt5Controller : ControllerBase
{
    private readonly IMt5Client _mt5;

    public AdminMt5Controller(IMt5Client mt5)
    {
        _mt5 = mt5;
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
}
