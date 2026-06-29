using Microsoft.EntityFrameworkCore;
using MondShield.Domain.Identity;

namespace MondShield.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the MondShield backend. Hosts the lightweight login table; domain
/// tables (ShieldAccount, LedgerEntry, CompensationRequest, audit logs, …) will be added
/// to this same context as those features are built.
/// </summary>
public class MondShieldDbContext : DbContext
{
    public MondShieldDbContext(DbContextOptions<MondShieldDbContext> options) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Table/column names are mapped to snake_case globally by UseSnakeCaseNamingConvention().
        builder.Entity<AppUser>(b =>
        {
            b.ToTable("users");
            b.HasKey(u => u.Id);

            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.HasIndex(u => u.Email).IsUnique();

            b.Property(u => u.FullName).HasMaxLength(256).IsRequired();
            b.Property(u => u.PasswordHash).IsRequired();
            b.Property(u => u.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
            b.Property(u => u.RefreshToken).HasMaxLength(256);
        });
    }
}
