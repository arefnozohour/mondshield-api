using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MondShield.Api.Contracts;
using MondShield.Application.Accounts;
using MondShield.Application.Onboarding;
using MondShield.Application.Onboarding.Dtos;
using MondShield.Domain.Authorization;

namespace MondShield.Api.Controllers;

/// <summary>
/// Admin actions for the onboarding pipeline: KYC approval, MT5 provisioning, activation
/// deposit confirmation. Each step is a deliberate admin action rather than automatic (see
/// CLAUDE.md's open question on KYC → provisioning) — a human checkpoint before a real
/// trading account exists.
/// </summary>
[ApiController]
[Route("api/admin/accounts")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminAccountsController : ControllerBase
{
    private readonly IOnboardingService _onboarding;
    private readonly IShieldAccountRepository _accounts;
    private readonly IAccountActivityService _activity;

    public AdminAccountsController(
        IOnboardingService onboarding,
        IShieldAccountRepository accounts,
        IAccountActivityService activity)
    {
        _onboarding = onboarding;
        _accounts = accounts;
        _activity = activity;
    }

    /// <summary>Look up any trader's account by id, with the owner's identity joined on.</summary>
    [HttpGet("{accountId:guid}")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountResponse>> Get(Guid accountId, CancellationToken ct)
    {
        var joined = await _accounts.GetByIdWithUserAsync(accountId, ct);
        return joined is null ? NotFound() : Ok(AccountResponse.From(joined));
    }

    /// <summary>Any trader's unified activity feed (ledger + stage transitions), newest first.</summary>
    [HttpGet("{accountId:guid}/activity")]
    [ProducesResponseType(typeof(IEnumerable<ActivityEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<ActivityEntryResponse>>> Activity(Guid accountId, CancellationToken ct)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            return NotFound();
        }

        var activity = await _activity.GetActivityAsync(accountId, ct);
        return Ok(activity.Select(ActivityEntryResponse.From));
    }

    /// <summary>Accounts still in the onboarding pipeline (PendingKyc/KycApproved/Provisioned) — the KYC/provisioning/activation queue.</summary>
    [HttpGet("queue/onboarding")]
    [ProducesResponseType(typeof(IEnumerable<AccountResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AccountResponse>>> OnboardingQueue(CancellationToken ct)
    {
        var accounts = await _accounts.GetOnboardingQueueWithUserAsync(ct);
        return Ok(accounts.Select(AccountResponse.From));
    }

    /// <summary>Approve KYC: PendingKyc → KycApproved.</summary>
    [HttpPost("{accountId:guid}/approve-kyc")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ApproveKyc(Guid accountId, CancellationToken ct)
    {
        var result = await _onboarding.ApproveKycAsync(accountId, ct);
        return result.Succeeded ? NoContent() : BadRequest(new ErrorResponse(result.Errors));
    }

    /// <summary>Provision the MT5 account: KycApproved → Provisioned. Returns one-time MT5 credentials.</summary>
    [HttpPost("{accountId:guid}/provision-mt5")]
    [ProducesResponseType(typeof(Mt5ProvisioningResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Mt5ProvisioningResponse>> ProvisionMt5(Guid accountId, [FromBody] ProvisionMt5Request request, CancellationToken ct)
    {
        var result = await _onboarding.ProvisionMt5Async(accountId, request.FullName, request.Email, ct);
        return result.Succeeded
            ? Ok(Mt5ProvisioningResponse.From(result.Value!))
            : BadRequest(new ErrorResponse(result.Errors));
    }

    /// <summary>Confirm the activation deposit: Provisioned → Active at Stage 1.</summary>
    [HttpPost("{accountId:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Activate(Guid accountId, [FromBody] ActivateAccountRequest request, CancellationToken ct)
    {
        var result = await _onboarding.ActivateAsync(accountId, request.DepositAmount, ct);
        return result.Succeeded ? NoContent() : BadRequest(new ErrorResponse(result.Errors));
    }

    /// <summary>
    /// Confirm the trader met their level-up profit target and advance them one stage up the
    /// ladder (Revival → Rebuild → Stage1 → Stage2 → Stage3 → VIP). Returns the new stage.
    /// </summary>
    [HttpPost("{accountId:guid}/level-up")]
    [ProducesResponseType(typeof(LevelUpResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LevelUpResponse>> LevelUp(Guid accountId, CancellationToken ct)
    {
        var result = await _onboarding.LevelUpAsync(accountId, ct);
        return result.Succeeded
            ? Ok(new LevelUpResponse(result.Value!))
            : BadRequest(new ErrorResponse(result.Errors));
    }
}

/// <summary>The stage an account moved to after a level-up.</summary>
public sealed record LevelUpResponse(string CurrentStage);
