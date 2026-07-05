using MondShield.Application.Onboarding;

namespace MondShield.Api.Contracts;

/// <summary>The new MT5 login and one-time credentials from provisioning.</summary>
public sealed record Mt5ProvisioningResponse(long Mt5Login, string MainPassword, string InvestorPassword)
{
    public static Mt5ProvisioningResponse From(Mt5ProvisioningResult result) => new(
        result.Mt5Login,
        result.MainPassword,
        result.InvestorPassword);
}
