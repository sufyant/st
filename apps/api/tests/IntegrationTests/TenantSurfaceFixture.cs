using System.Security.Claims;
using System.Text.Encodings.Web;
using Domain.Shared;
using Domain.Authorization;
using Domain.ControlPlane.Memberships;
using Domain.ControlPlane.Tenants;
using Infrastructure.Persistence;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
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

public sealed class TenantSurfaceFixture : IAsyncDisposable
{
    public const string Alias = "acme";
    public const string OwnerUserId = "user_owner";
    public const string OwnerEmail = "owner@example.com";

    private static readonly AuditInterceptor AuditInterceptor = new(TimeProvider.System);

    private readonly PostgreSqlContainer postgres;
    private readonly WebApplicationFactory<Program> factory;
    private readonly bool ownsContainer;

    private TenantSurfaceFixture(
        PostgreSqlContainer postgres,
        WebApplicationFactory<Program> factory,
        Tenant tenant,
        string controlPlaneConnectionString,
        bool ownsContainer)
    {
        this.postgres = postgres;
        this.factory = factory;
        this.ownsContainer = ownsContainer;
        Tenant = tenant;
        ControlPlaneConnectionString = controlPlaneConnectionString;
        Client = factory.CreateClient();
    }

    public HttpClient Client { get; }

    public Tenant Tenant { get; }

    public string ControlPlaneConnectionString { get; }

    public string ServerConnectionString => postgres.GetConnectionString();

    public static Task<TenantSurfaceFixture> StartAsync() =>
        StartAsync(OwnerUserId, OwnerEmail, "owner");

    public static async Task<TenantSurfaceFixture> StartAsync(
        string externalUserId,
        string? email,
        string? roleCode)
    {
        var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var connectionString = postgres.GetConnectionString();
        await ExecuteAsync(connectionString, "CREATE DATABASE control_plane");
        var controlPlane = WithDatabase(connectionString, "control_plane");
        var tenant = Tenant.Create(TenantAlias.Create(Alias));
        tenant.CompleteProvisioning();

        await using (var context = CreateControlPlaneDbContext(controlPlane))
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            context.Tenants.Add(tenant);
            context.Memberships.Add(Membership.Create(
                tenant.Id,
                ExternalUserId.Create(externalUserId)));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await ExecuteAsync(connectionString, $"CREATE DATABASE {tenant.DatabaseName.Value}");

        await using (var tenantDbContext =
                     new TenantDbContextFactory(connectionString, AuditInterceptor).Create(tenant.DatabaseName.Value))
        {
            await tenantDbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

            if (roleCode is not null)
            {
                var user = User.Create(
                    ExternalUserId.Create(externalUserId),
                    EmailAddress.Create(email!),
                    UserStatus.Active);
                tenantDbContext.Users.Add(user);
                var role = await tenantDbContext.Roles.SingleAsync(
                    candidate => candidate.Code == roleCode,
                    TestContext.Current.CancellationToken);
                user.AssignRoles([role]);
                await tenantDbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        return new TenantSurfaceFixture(
            postgres,
            BuildFactory(controlPlane, connectionString, externalUserId, email),
            tenant,
            controlPlane,
            ownsContainer: true);
    }

    public TenantSurfaceFixture WithPrincipal(string externalUserId, string? email) =>
        new(
            postgres,
            BuildFactory(ControlPlaneConnectionString, ServerConnectionString, externalUserId, email),
            Tenant,
            ControlPlaneConnectionString,
            ownsContainer: false);

    public Task<TenantSurfaceFixture> WithPrincipalAsync(string externalUserId, string? email) =>
        Task.FromResult(WithPrincipal(externalUserId, email));

    public IServiceScope CreateScope() => factory.Services.CreateScope();

    public ControlPlaneDbContext CreateControlPlane() =>
        CreateControlPlaneDbContext(ControlPlaneConnectionString);

    public TenantDbContext CreateTenantDbContext() =>
        new TenantDbContextFactory(ServerConnectionString, AuditInterceptor).Create(Tenant.DatabaseName.Value);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();

        if (ownsContainer)
        {
            await postgres.DisposeAsync();
        }
    }

    private static WebApplicationFactory<Program> BuildFactory(
        string controlPlane,
        string connectionString,
        string externalUserId,
        string? email) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:ControlPlane", controlPlane);
            builder.UseSetting("ConnectionStrings:ControlPlaneRead", controlPlane);
            builder.UseSetting("ConnectionStrings:TenantData", connectionString);
            builder.UseSetting("ConnectionStrings:Provisioner", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new TestPrincipal(externalUserId, email));
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        });

    private static ControlPlaneDbContext CreateControlPlaneDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "control"))
            .Options;

        return new ControlPlaneDbContext(options);
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

    private sealed record TestPrincipal(string ExternalUserId, string? Email);

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new("sub", principal.ExternalUserId) };

            if (principal.Email is not null)
            {
                claims.Add(new Claim("email", principal.Email));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
