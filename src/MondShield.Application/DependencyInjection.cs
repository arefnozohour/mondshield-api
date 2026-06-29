using Microsoft.Extensions.DependencyInjection;

namespace MondShield.Application;

/// <summary>
/// Composition root for the Application layer. Use cases (ticket flow, payout
/// orchestration, etc.) get registered here as they are built out.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Reserved for use-case handlers / validators as the application layer grows.
        return services;
    }
}
