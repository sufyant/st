using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Domain.ControlPlane.Memberships;
using Domain.Shared;
using Domain.ControlPlane.Tenants;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantAccessEndpointTests
{
    private const string ExternalUser = "user_2abc123";
    private const string ExternalUserEmail = "user@example.com";
    private const string ActiveUser = "Active";
    private const string DisabledUser = "Disabled";

    [Fact]
    public async Task GetWhoAmI_WithAnInvalidTenantAlias_ReturnsNotFound()
    {
        // Arrange
        await using var factory = CreateFactory("Host=localhost;Database=control_plane");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/ACME/api/v1/whoami", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_WithoutAMembership_ReturnsForbidden()
    {
        // Arrange
        await using var postgres = await StartPostgresAsync();
        await SeedAsync(postgres, TenantStatus.Active, ActiveUser, hasMembership: false);
        await using var factory = CreateFactory(postgres.GetConnectionString());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/acme/api/v1/whoami", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_ForADisabledTenantUser_ReturnsForbidden()
    {
        // Arrange
        await using var postgres = await StartPostgresAsync();
        await SeedAsync(postgres, TenantStatus.Active, DisabledUser);
        await using var factory = CreateFactory(postgres.GetConnectionString());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/acme/api/v1/whoami", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_WhileTheTenantIsProvisioning_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var postgres = await StartPostgresAsync();
        await SeedAsync(postgres, TenantStatus.Provisioning, ActiveUser);
        await using var factory = CreateFactory(postgres.GetConnectionString());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/acme/api/v1/whoami", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_ForASuspendedTenant_ReturnsForbidden()
    {
        // Arrange
        await using var postgres = await StartPostgresAsync();
        await SeedAsync(postgres, TenantStatus.Suspended, ActiveUser);
        await using var factory = CreateFactory(postgres.GetConnectionString());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/acme/api/v1/whoami", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_ForADeletedTenant_ReturnsNotFound()
    {
        // Arrange
        await using var postgres = await StartPostgresAsync();
        await SeedAsync(postgres, TenantStatus.Deleted, ActiveUser);
        await using var factory = CreateFactory(postgres.GetConnectionString());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/acme/api/v1/whoami", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_ForAnActiveTenantMember_ReturnsTenantContext()
    {
        // Arrange
        await using var postgres = await StartPostgresAsync();
        await SeedAsync(postgres, TenantStatus.Active, ActiveUser);
        await using var factory = CreateFactory(postgres.GetConnectionString());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/acme/api/v1/whoami", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);

        return postgres;
    }

    private static async Task SeedAsync(
        PostgreSqlContainer postgres,
        TenantStatus tenantStatus,
        string tenantUserStatus,
        bool hasMembership = true)
    {
        var connectionString = postgres.GetConnectionString();
        var tenant = Tenant.Create(TenantAlias.Create("acme"), DateTimeOffset.UtcNow);
        MoveToStatus(tenant, tenantStatus, DateTimeOffset.UtcNow);

        await ExecuteAsync(connectionString, "CREATE DATABASE control_plane");
        var controlPlaneOptions = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(
                WithDatabase(connectionString, "control_plane"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;
        await using var controlPlaneContext = new ControlPlaneDbContext(controlPlaneOptions);
        await controlPlaneContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        controlPlaneContext.Tenants.Add(tenant);

        if (hasMembership)
        {
            controlPlaneContext.Memberships.Add(
                Membership.Create(tenant.Id, ExternalUserId.Create(ExternalUser)));
        }

        await controlPlaneContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await ExecuteAsync(connectionString, $"CREATE DATABASE {tenant.DatabaseName.Value}");
        await using var tenantContext = new TenantDbContextFactory(connectionString).Create(tenant.DatabaseName.Value);
        await tenantContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await tenantContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO users (id, external_user_id, email, status) VALUES ({Guid.NewGuid()}, {ExternalUser}, {ExternalUserEmail}, {tenantUserStatus})",
            TestContext.Current.CancellationToken);
    }

    private static void MoveToStatus(Tenant tenant, TenantStatus status, DateTimeOffset updatedAt)
    {
        switch (status)
        {
            case TenantStatus.Provisioning:
                break;
            case TenantStatus.Active:
                tenant.CompleteProvisioning(updatedAt);
                break;
            case TenantStatus.Suspended:
                tenant.CompleteProvisioning(updatedAt);
                tenant.Suspend(updatedAt);
                break;
            case TenantStatus.Deprovisioning:
                tenant.CompleteProvisioning(updatedAt);
                tenant.BeginDeprovisioning(updatedAt);
                break;
            case TenantStatus.Deleted:
                tenant.CompleteProvisioning(updatedAt);
                tenant.BeginDeprovisioning(updatedAt);
                tenant.MarkDeleted(updatedAt);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static string WithDatabase(string connectionString, string databaseName) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            var controlPlane = WithDatabase(connectionString, "control_plane");
            builder.UseSetting("ConnectionStrings:ControlPlane", controlPlane);
            builder.UseSetting("ConnectionStrings:ControlPlaneRead", controlPlane);
            builder.UseSetting("ConnectionStrings:TenantData", connectionString);
            builder.UseSetting("ConnectionStrings:Provisioner", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        });

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim("sub", ExternalUser)], SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
