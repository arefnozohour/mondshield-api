using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MondShield.Application.Common.Interfaces;
using MondShield.Domain.Authorization;
using MondShield.Domain.Identity;
using MondShield.Infrastructure.Identity;
using MondShield.Infrastructure.Persistence;

namespace MondShield.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer: EF Core/PostgreSQL, JWT bearer
/// authentication, authorization policies, and the lightweight identity service.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        AddPersistence(services, configuration);
        AddJwtAuthentication(services, configuration);
        AddAuthorizationPolicies(services);

        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' was not found. Configure ConnectionStrings:Default.");

        services.AddDbContext<MondShieldDbContext>(options =>
            options
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());
    }

    private static void AddJwtAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey));

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });
    }

    private static void AddAuthorizationPolicies(IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AdminOnly, policy => policy.RequireRole(Roles.Admin))
            .AddPolicy(Policies.AuthenticatedUser, policy => policy.RequireAuthenticatedUser());
    }
}
