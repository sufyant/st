using System.Security.Claims;
using System.Text.Encodings.Web;
using Domain.Access;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class ControlPlaneFixture : IAsyncDisposable
{
    public const string TestUserId = "user_2abc123";

    private readonly PostgreSqlContainer postgres;
    private readonly WebApplicationFactory<Program> factory;

    private ControlPlaneFixture(PostgreSqlContainer postgres, WebApplicationFactory<Program> factory)
    {
        this.postgres = postgres;
        this.factory = factory;
        Client = factory.CreateClient();
    }

    public HttpClient Client { get; }

    public static async Task<ControlPlaneFixture> StartAsync(bool isPlatformAdmin)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE DATABASE control_plane");
        var controlPlane = WithDatabase(connectionString, "control_plane");
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(controlPlane, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;

        await using (var context = new ControlPlaneDbContext(options))
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            if (isPlatformAdmin)
            {
                context.PlatformAdmins.Add(PlatformAdmin.Create(
                    Guid.NewGuid(),
                    ExternalUserId.Create(TestUserId),
                    DateTimeOffset.UtcNow));
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:ControlPlane", controlPlane);
            builder.UseSetting("ConnectionStrings:ControlPlaneRead", controlPlane);
            builder.UseSetting("ConnectionStrings:TenantData", connectionString);
            builder.ConfigureTestServices(services =>
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { }));
        });

        return new ControlPlaneFixture(postgres, factory);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
        await postgres.DisposeAsync();
    }

    private static string WithDatabase(string connectionString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim("sub", TestUserId)], SchemeName);
            var principal = new ClaimsPrincipal(identity);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
