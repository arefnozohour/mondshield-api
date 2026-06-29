using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MondShield.Domain.Identity;
using MondShield.Infrastructure.Persistence;

namespace MondShield.Infrastructure.Identity;

/// <summary>
/// Ensures a bootstrap admin account exists. Credentials come from the "Seed:Admin" config
/// section so they are never hardcoded; if no password is configured the admin is skipped.
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
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        var adminEmail = config["Seed:Admin:Email"]?.Trim().ToLowerInvariant();
        var adminPassword = config["Seed:Admin:Password"];

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning(
                "Seed:Admin:Email/Password not configured — skipping admin seeding. " +
                "Set them via user-secrets or environment to provision the bootstrap admin.");
            return;
        }

        if (await db.Users.AnyAsync(u => u.Email == adminEmail, ct))
        {
            return;
        }

        var admin = new AppUser
        {
            Email = adminEmail,
            FullName = "MondShield Administrator",
            Role = UserRole.Admin,
        };
        admin.PasswordHash = hasher.HashPassword(admin, adminPassword);

        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded bootstrap admin {Email}", adminEmail);
    }
}
