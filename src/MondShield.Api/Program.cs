using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MondShield.Application;
using MondShield.Application.Common.Interfaces;
using MondShield.Application.Compensation;
using MondShield.Application.Mt5;
using MondShield.Api.Security;
using MondShield.Domain.Compensation;
using MondShield.Infrastructure;
using MondShield.Infrastructure.Identity;
using MondShield.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

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
// Cors:AllowAnyOrigin=true opens the API to ANY origin (safe here only because auth is a JWT
// Bearer token in the Authorization header, not cookies — AllowAnyOrigin can't be combined with
// credentials). Otherwise, Cors:AllowNgrok additionally permits any *.ngrok-free.app /
// *.ngrok.app / *.ngrok.io origin, so a machine exposed through an ngrok tunnel works without
// hardcoding the rotating free-tier URL (which changes on every ngrok restart).
builder.Services.AddCors(options =>
{
    var allowAnyOrigin = builder.Configuration.GetValue<bool>("Cors:AllowAnyOrigin");
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:3000"];
    var allowNgrok = builder.Configuration.GetValue<bool>("Cors:AllowNgrok");

    options.AddPolicy(WebDevCorsPolicy, policy =>
    {
        if (allowAnyOrigin)
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
            return;
        }

        policy.SetIsOriginAllowed(origin =>
              {
                  if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                  {
                      return true;
                  }

                  if (!allowNgrok || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                  {
                      return false;
                  }

                  return uri.Host.EndsWith(".ngrok-free.app", StringComparison.OrdinalIgnoreCase)
                      || uri.Host.EndsWith(".ngrok.app", StringComparison.OrdinalIgnoreCase)
                      || uri.Host.EndsWith(".ngrok.io", StringComparison.OrdinalIgnoreCase);
              })
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
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

// Pulls each active account's realized trading profit and commission from MT5 into the local
// ledger, sets FirstTradeAtUtc, and reconciles the MT5 balance. Hourly is a gentle default for the
// (single-locked, low-volume) Manager API; admins can also trigger a per-account sync on demand.
RecurringJob.AddOrUpdate<IMt5ReconciliationService>(
    "mt5-reconciliation",
    job => job.ReconcileAllActiveAsync(CancellationToken.None),
    Cron.Hourly());

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
