using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Domain.Access;
using Domain.Tenants;
using Infrastructure.Persistence.Admin;
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
    [Fact]
    public async Task GetWhoAmI_WithAnInvalidTenantAlias_ReturnsNotFound()
    {
        // Arrange
        await using var factory = CreateFactory("Host=localhost;Database=st");
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
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var tenant = Tenant.Create(Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9"), TenantAlias.Create("acme"));
        await SeedTenantAccessAsync(postgres.GetConnectionString(), tenant, "user_2abc123", "Active", hasMembership: false);
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
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var tenant = Tenant.Create(Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9"), TenantAlias.Create("acme"));
        await SeedTenantAccessAsync(postgres.GetConnectionString(), tenant, "user_2abc123", "Disabled");
        await using var factory = CreateFactory(postgres.GetConnectionString());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/acme/api/v1/whoami", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWhoAmI_ForAnActiveTenantMember_ReturnsTenantContext()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var tenant = Tenant.Create(Guid.Parse("018f4e3b-7c9d-4a1b-a2c3-d4e5f6a7b8c9"), TenantAlias.Create("acme"));
        const string externalUserId = "user_2abc123";
        await SeedTenantAccessAsync(postgres.GetConnectionString(), tenant, externalUserId, "Active");
        await using var factory = CreateFactory(postgres.GetConnectionString());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/acme/api/v1/whoami", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task SeedTenantAccessAsync(
        string connectionString,
        Tenant tenant,
        string externalUserId,
        string status,
        bool hasMembership = true)
    {
        await CreateSystemDatabaseAsync(connectionString);
        var systemDatabaseConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "systemdb"
        }.ConnectionString;
        var adminOptions = new DbContextOptionsBuilder<AdminDbContext>()
            .UseNpgsql(systemDatabaseConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "admin"))
            .Options;
        await using var adminContext = new AdminDbContext(adminOptions);
        await adminContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        adminContext.Tenants.Add(tenant);

        if (hasMembership)
        {
            adminContext.Memberships.Add(Membership.Create(Guid.NewGuid(), tenant.Id, ExternalUserId.Create(externalUserId)));
        }

        await adminContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var provisioner = new PostgresTenantDatabaseProvisioner(dataSource);
        await provisioner.CreateAsync(tenant, TestContext.Current.CancellationToken);
        await using var tenantContext = new TenantDbContextFactory(connectionString).Create(tenant);
        await tenantContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await tenantContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO users (id, external_user_id, status) VALUES ({Guid.NewGuid()}, {externalUserId}, {status})",
            TestContext.Current.CancellationToken);
    }

    private static async Task CreateSystemDatabaseAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("CREATE DATABASE systemdb", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
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
            var identity = new ClaimsIdentity([new Claim("sub", "user_2abc123")], SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
