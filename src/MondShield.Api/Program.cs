using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MondShield.Application;
using MondShield.Application.Common.Interfaces;
using MondShield.Application.Compensation;
using MondShield.Api.Security;
using MondShield.Domain.Compensation;
using MondShield.Infrastructure;
using MondShield.Infrastructure.Identity;
using MondShield.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Optional, git-ignored local overrides (see .gitignore). A convenient place to drop
// machine-specific secrets like the MT5 connection without committing them. Added after the
// default sources, so it takes precedence over appsettings.json / appsettings.{Environment}.json,
// user-secrets, and environment variables during local dev. It's never deployed (git-ignored),
// so in production env vars / real secret stores apply as usual. Absent by default — nothing to
// do if you use user-secrets or env vars instead.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

const string WebDevCorsPolicy = "WebDev";

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(ConfigureSwagger);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// mondshield-web (Next.js) runs on a separate origin in dev. Configurable via
// Cors:AllowedOrigins so a deployed frontend origin can be added later without a code change.
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:3000"];

    options.AddPolicy(WebDevCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Dashboard is dev-only for now — production would need an IDashboardAuthorizationFilter.
    app.UseHangfireDashboard();

    // Early-dev schema management: no migrations. Set Database:RecreateOnStartup=true to drop
    // & rebuild on every run so entity changes are picked up automatically. Switch to
    // migrations (MigrateAsync) before going to production.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<MondShieldDbContext>();
        if (builder.Configuration.GetValue<bool>("Database:RecreateOnStartup"))
        {
            // Drop only the "public" schema (where our EF tables live), not the whole
            // database — Hangfire keeps its own "hangfire" schema in the same database and
            // must survive this reset.
            await db.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;");
        }

        // Not EnsureCreatedAsync(): its "does this database already have tables" check isn't
        // schema-scoped, so with Hangfire's tables living in the same database (its own
        // "hangfire" schema) it wrongly concludes our schema already exists and skips creation.
        // Check "public" specifically instead.
        var publicTableCount = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .SingleAsync();

        if (publicTableCount == 0)
        {
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
        }
    }

    // Provision the bootstrap admin in development.
    await IdentitySeeder.SeedAsync(app.Services);
}

// Registered in every environment — the payout job must run in production too, not just dev.
RecurringJob.AddOrUpdate<IPayoutService>(
    "compensation-payout",
    job => job.ProcessDuePayoutsAsync(CancellationToken.None),
    Cron.Monthly(PayoutSchedule.PayoutDayOfMonth, 0, 0));

// Skipped in Development: the frontend calls http://localhost:5259 directly (see
// mondshield-web/.env.local), and if the API happens to be running under a profile with an
// https binding (e.g. the "https" launchSettings profile), this would 307-redirect the CORS
// preflight OPTIONS request to https before UseCors() gets a chance to answer it — the browser
// then reports that as a CORS error rather than a redirect.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors(WebDevCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static void ConfigureSwagger(Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options)
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MondShield API",
        Version = "v1",
        Description = "Account, stage, and compensation management for the MondShield insurance-style forex account.",
    });

    var jwtScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT access token (without the 'Bearer ' prefix).",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer",
        },
    };

    options.AddSecurityDefinition("Bearer", jwtScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [jwtScheme] = Array.Empty<string>(),
    });
}

// Exposed for integration testing (WebApplicationFactory).
public partial class Program;
