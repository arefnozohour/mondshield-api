using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MondShield.Application.Onboarding;
using MondShield.Domain.Identity;
using MondShield.Infrastructure.Persistence;

namespace MondShield.Infrastructure.Identity;

/// <summary>
/// Seeds bootstrap accounts from config so nothing is hardcoded: one admin (<c>Seed:Admin</c>)
/// and any number of trader users (<c>Seed:Users</c>). Each trader also gets a MondShield account
/// at <c>PendingKyc</c>, exactly as registration would.
///
/// Idempotent and safe to run on every startup: existing users/accounts are not recreated. MT5
/// provisioning is intentionally left to a deliberate post-startup admin action (it's unreliable
/// to call the native Manager API synchronously during startup) — and with
/// <c>Database:RecreateOnStartup=false</c> a provisioned account then survives restarts.
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<MondShieldDbContext>();
        var hasher = sp.GetRequiredService<IPasswordHasher<AppUser>>();
        var config = sp.GetRequiredService<IConfiguration>();
        var onboarding = sp.GetRequiredService<IOnboardingService>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        // --- bootstrap admin (no trader account — admins aren't traders) ---
        var adminEmail = config["Seed:Admin:Email"]?.Trim().ToLowerInvariant();
        var adminPassword = config["Seed:Admin:Password"];

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning(
                "Seed:Admin:Email/Password not configured — skipping admin seeding. " +
                "Set them via user-secrets or environment to provision the bootstrap admin.");
        }
        else
        {
            await EnsureUserAsync(db, hasher, logger, adminEmail, adminPassword,
                "MondShield Administrator", UserRole.Admin, ct);
        }

        // --- trader users ---
        foreach (var child in config.GetSection("Seed:Users").GetChildren())
        {
            var email = child["Email"]?.Trim().ToLowerInvariant();
            var password = child["Password"];
            var fullName = child["FullName"]?.Trim();

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                continue;
            }

            // AutoProvision is intentionally NOT acted on during startup seeding: calling into the
            // MT5 Manager API here (synchronously, before the web host is running) is unreliable.
            // Seed the trader at PendingKyc only; provision MT5 as a deliberate post-startup admin
            // action (admin UI / POST provision-mt5). Because Database:RecreateOnStartup=false, the
            // provisioned state then survives restarts, so it only needs doing once.
            _ = child["AutoProvision"];

            var name = string.IsNullOrWhiteSpace(fullName) ? email : fullName;
            var user = await EnsureUserAsync(db, hasher, logger, email, password, name, UserRole.User, ct);

            // Ensure the trader has a MondShield account (fast, local — no MT5).
            var account = await db.ShieldAccounts.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
            if (account is null)
            {
                await onboarding.CreateAccountForNewUserAsync(user.Id, ct);
            }
        }
    }

    /// <summary>Creates the login account if it doesn't already exist. Returns the (existing or new) user.</summary>
    private static async Task<AppUser> EnsureUserAsync(
        MondShieldDbContext db, IPasswordHasher<AppUser> hasher, ILogger logger,
        string email, string password, string fullName, UserRole role, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is not null)
        {
            return user;
        }

        user = new AppUser { Email = email, FullName = fullName, Role = role };
        user.PasswordHash = hasher.HashPassword(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Role} {Email}", role, email);
        return user;
    }
}
