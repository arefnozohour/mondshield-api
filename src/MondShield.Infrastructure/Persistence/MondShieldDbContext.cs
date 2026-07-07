using Microsoft.EntityFrameworkCore;
using MondShield.Domain.Accounts;
using MondShield.Domain.Compensation;
using MondShield.Domain.Identity;
using MondShield.Domain.Ledger;
using MondShield.Domain.Stages;
using MondShield.Domain.Withdrawals;

namespace MondShield.Infrastructure.Persistence;

/// <summary>EF Core context for the MondShield backend.</summary>
public class MondShieldDbContext : DbContext
{
    public MondShieldDbContext(DbContextOptions<MondShieldDbContext> options) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ShieldAccount> ShieldAccounts => Set<ShieldAccount>();
    public DbSet<CompensationRequest> CompensationRequests => Set<CompensationRequest>();
    public DbSet<CompensationCapTracker> CompensationCapTrackers => Set<CompensationCapTracker>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<ProfitWithdrawal> ProfitWithdrawals => Set<ProfitWithdrawal>();
    public DbSet<StageTransitionRecord> StageTransitions => Set<StageTransitionRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Table/column names are mapped to snake_case globally by UseSnakeCaseNamingConvention().
        ConfigureUsers(builder);
        ConfigureShieldAccounts(builder);
        ConfigureCompensationRequests(builder);
        ConfigureCompensationCapTrackers(builder);
        ConfigureLedgerEntries(builder);
        ConfigureProfitWithdrawals(builder);
        ConfigureStageTransitions(builder);
    }

    private static void ConfigureUsers(ModelBuilder builder)
    {
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

    private static void ConfigureShieldAccounts(ModelBuilder builder)
    {
        builder.Entity<ShieldAccount>(b =>
        {
            b.ToTable("shield_accounts");
            b.HasKey(a => a.Id);

            // One ShieldAccount per AppUser.
            b.HasIndex(a => a.UserId).IsUnique();
            b.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);

            b.Property(a => a.Mt5Login).HasColumnName("mt5_login");
            b.HasIndex(a => a.Mt5Login);

            b.Property(a => a.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            b.Property(a => a.CurrentStage).HasConversion<string>().HasMaxLength(16);

            // The Domain's own verified BalanceComposition value object, mapped onto this
            // table's columns — reused as-is rather than re-declared as raw decimal fields.
            b.OwnsOne(a => a.Composition, comp =>
            {
                comp.Property(c => c.InsuredCapital).HasPrecision(18, 2).IsRequired();
                comp.Property(c => c.Compensation).HasPrecision(18, 2).IsRequired();
                comp.Property(c => c.Profit).HasPrecision(18, 2).IsRequired();
                comp.Property(c => c.Commission).HasPrecision(18, 2).IsRequired();
            });
            b.Navigation(a => a.Composition).IsRequired();
        });
    }

    private static void ConfigureCompensationRequests(ModelBuilder builder)
    {
        builder.Entity<CompensationRequest>(b =>
        {
            b.ToTable("compensation_requests");
            b.HasKey(r => r.Id);

            b.HasOne<ShieldAccount>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);

            // One request per stage per account.
            b.HasIndex(r => new { r.AccountId, r.StageAtRequest }).IsUnique();

            b.Property(r => r.StageAtRequest).HasConversion<string>().HasMaxLength(16).IsRequired();
            b.Property(r => r.LossAtRequest).HasPrecision(18, 2);
            b.Property(r => r.CommissionExcluded).HasPrecision(18, 2);
            b.Property(r => r.ComputedCoverage).HasPrecision(18, 2);
            b.Property(r => r.CappedAmount).HasPrecision(18, 2);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        });
    }

    private static void ConfigureCompensationCapTrackers(ModelBuilder builder)
    {
        builder.Entity<CompensationCapTracker>(b =>
        {
            b.ToTable("compensation_cap_trackers");
            b.HasKey(t => t.UserId);

            b.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);

            b.Property(t => t.LifetimeCompensationPaid).HasPrecision(18, 2);
        });
    }

    private static void ConfigureLedgerEntries(ModelBuilder builder)
    {
        builder.Entity<LedgerEntry>(b =>
        {
            b.ToTable("ledger_entries");
            b.HasKey(e => e.Id);

            b.HasOne<ShieldAccount>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => e.AccountId);

            b.Property(e => e.Bucket).HasConversion<string>().HasMaxLength(16).IsRequired();
            b.Property(e => e.Reason).HasConversion<string>().HasMaxLength(16).IsRequired();
            b.Property(e => e.Amount).HasPrecision(18, 2);

            // DB-level backstop against a double compensation credit: at most one Compensation
            // ledger line per source request. The payout claim/confirm flow already prevents this
            // in code; this filtered unique index makes it impossible even if that logic is bypassed
            // or a reconciliation is mis-run. Filtered to Compensation ONLY — a profit withdrawal
            // legitimately writes several ledger lines sharing one RelatedRequestId, so this must
            // not constrain other reasons.
            b.HasIndex(e => e.RelatedRequestId)
                .IsUnique()
                .HasFilter("reason = 'Compensation'");
        });
    }

    private static void ConfigureProfitWithdrawals(ModelBuilder builder)
    {
        builder.Entity<ProfitWithdrawal>(b =>
        {
            b.ToTable("profit_withdrawals");
            b.HasKey(w => w.Id);

            b.HasOne<ShieldAccount>().WithMany().HasForeignKey(w => w.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(w => w.AccountId);

            b.Property(w => w.RequestedAmount).HasPrecision(18, 2);
            b.Property(w => w.ProfitPortion).HasPrecision(18, 2);
            b.Property(w => w.NonProfitPortion).HasPrecision(18, 2);
            b.Property(w => w.BrokerShareAmount).HasPrecision(18, 2);
            b.Property(w => w.NetToTrader).HasPrecision(18, 2);
            b.Property(w => w.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        });
    }

    private static void ConfigureStageTransitions(ModelBuilder builder)
    {
        builder.Entity<StageTransitionRecord>(b =>
        {
            b.ToTable("stage_transitions");
            b.HasKey(t => t.Id);

            b.HasOne<ShieldAccount>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(t => t.AccountId);

            b.Property(t => t.From).HasConversion<string>().HasMaxLength(16).IsRequired();
            b.Property(t => t.To).HasConversion<string>().HasMaxLength(16);
            b.Property(t => t.Direction).HasConversion<string>().HasMaxLength(8).IsRequired();
            b.Property(t => t.Reason).IsRequired();
        });
    }
}
