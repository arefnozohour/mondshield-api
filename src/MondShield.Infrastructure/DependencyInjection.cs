using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MondShield.Application.Common.Interfaces;
using MondShield.Application.Compensation;
using MondShield.Application.Mt5;
using MondShield.Application.Onboarding;
using MondShield.Application.Withdrawals;
using MondShield.Domain.Authorization;
using MondShield.Domain.Identity;
using MondShield.Infrastructure.Identity;
using MondShield.Infrastructure.Mt5;
using MondShield.Infrastructure.Persistence;

namespace MondShield.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer: EF Core/PostgreSQL, JWT bearer
/// authentication, authorization policies, the lightweight identity service, the MT5 client,
/// and Hangfire (persisted jobs backing the monthly compensation payout).
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
        AddMt5Client(services, configuration);
        AddScheduler(services, configuration);

        services.AddScoped<IMt5AccountInfoService, Mt5AccountInfoService>();

        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IShieldAccountRepository, ShieldAccountRepository>();
        services.AddScoped<ICompensationRepository, CompensationRepository>();
        services.AddScoped<IProfitWithdrawalRepository, ProfitWithdrawalRepository>();

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

    private static void AddMt5Client(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<Mt5Settings>()
            .Bind(configuration.GetSection(Mt5Settings.SectionName))
            .ValidateOnStart();

        // Mt5:Mode selects the backing implementation. Default (Stub) is the in-memory fake — no
        // server needed. Set Mt5:Mode=Live (plus Mt5:Server / ManagerLogin / ManagerPassword via
        // user-secrets) to talk to the real MetaQuotes Manager API. Everything upstream depends
        // only on IMt5Client, so nothing else changes when you flip modes.
        var mt5Section = configuration.GetSection(Mt5Settings.SectionName);
        var mode = mt5Section.GetValue<Mt5Mode>("Mode");
        if (mode == Mt5Mode.Live)
        {
            // One shared connection for both request/response and the real-time pump — many servers
            // allow only one session per manager login, so the listener reuses this instance rather
            // than opening a second connection. Register the concrete type once and alias IMt5Client
            // to it so both resolve the same singleton.
            services.AddSingleton<Mt5ManagerClient>();
            services.AddSingleton<IMt5Client>(sp => sp.GetRequiredService<Mt5ManagerClient>());

            // Real-time tracking: a hosted listener holds the pumped connection open and reconciles
            // an account the moment a deal for it arrives. Opt-in via Mt5:Realtime:Enabled.
            if (mt5Section.GetSection("Realtime").GetValue<bool>("Enabled"))
            {
                services.AddHostedService<Mt5RealtimeListener>();
            }
        }
        else
        {
            services.AddSingleton<IMt5Client, Mt5StubClient>();
        }
    }

    private static void AddScheduler(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' was not found. Configure ConnectionStrings:Default.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(pg => pg.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer();
    }
}
