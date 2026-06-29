using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MondShield.Application;
using MondShield.Application.Common.Interfaces;
using MondShield.Api.Security;
using MondShield.Infrastructure;
using MondShield.Infrastructure.Identity;
using MondShield.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(ConfigureSwagger);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Early-dev schema management: no migrations. EnsureCreated() builds the schema directly
    // from the current model. Set Database:RecreateOnStartup=true to drop & rebuild on every
    // run so entity changes are picked up automatically (EnsureCreated alone does NOT alter an
    // existing schema). Switch to migrations (MigrateAsync) before going to production.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<MondShieldDbContext>();
        if (builder.Configuration.GetValue<bool>("Database:RecreateOnStartup"))
        {
            await db.Database.EnsureDeletedAsync();
        }
        await db.Database.EnsureCreatedAsync();
    }

    // Provision the bootstrap admin in development.
    await IdentitySeeder.SeedAsync(app.Services);
}

app.UseHttpsRedirection();

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
