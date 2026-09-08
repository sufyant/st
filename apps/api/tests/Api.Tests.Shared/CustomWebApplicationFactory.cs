using System.Security.Cryptography;
using Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Core;

namespace Api.Tests.Shared;

/// <param name="testLogSink">
/// Optional Serilog sink for tests that need to assert on log output (e.g. enrichment
/// properties). When supplied, it is appended to the app's own <c>UseSerilog</c>
/// configuration for this factory instance only, via a second <c>UseSerilog</c> call in
/// <see cref="ConfigureWebHost"/> — Serilog.AspNetCore's bootstrap-logger mechanism means the
/// last <c>UseSerilog</c> call to run wins, so this reconfigures the same reloadable logger
/// Program.cs already created rather than replacing it.
/// </param>
public sealed class CustomWebApplicationFactory(string connectionString, ILogEventSink? testLogSink = null)
    : WebApplicationFactory<Program>
{
    public static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048));

    public const string TestIssuer = "https://test-issuer.local";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.ConfigurationManager = null;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = SigningKey,
                    NameClaimType = "sub",
                };
            });

            services.RemoveAll<DbContextOptions<AdminDbContext>>();
            services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(connectionString));

            // Hosted services (e.g. OutboxProcessor) fire their first poll immediately
            // on host startup, which would drain/mark-process any pending outbox rows
            // in the shared test database. HTTP-pipeline tests using this factory
            // don't need the outbox to run, so remove all hosted services here.
            services.RemoveAll<IHostedService>();
        });
    }

    // ConfigureWebHost only exposes IWebHostBuilder, which Serilog.AspNetCore doesn't add a
    // UseSerilog overload for (only IHostBuilder gets one). CreateHost hands us the underlying
    // IHostBuilder before it's built, so a second UseSerilog call can be appended here.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        if (testLogSink is not null)
        {
            builder.UseSerilog((context, services, configuration) => configuration
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.Sink(testLogSink));
        }

        return base.CreateHost(builder);
    }
}
