using Microsoft.Extensions.DependencyInjection;
using MondShield.Application.Accounts;
using MondShield.Application.Compensation;
using MondShield.Application.Mt5;
using MondShield.Application.Onboarding;
using MondShield.Application.Withdrawals;

namespace MondShield.Application;

/// <summary>
/// Composition root for the Application layer. Use cases (ticket flow, payout
/// orchestration, etc.) get registered here as they are built out.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IOnboardingService, OnboardingService>();
        services.AddScoped<ICompensationService, CompensationService>();
        services.AddScoped<IPayoutService, PayoutService>();
        services.AddScoped<IProfitWithdrawalService, ProfitWithdrawalService>();
        services.AddScoped<IAccountActivityService, AccountActivityService>();
        services.AddScoped<IMt5ReconciliationService, Mt5ReconciliationService>();
        return services;
    }
}
