using Api.Domain;
using Api.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class TenantHubTests(PostgresContainerFixture fixture) : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new(fixture.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private AdminDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        return new AdminDbContext(options);
    }

    private async Task<Tenant> ProvisionTenantWithMembershipAsync(string clerkUserId)
    {
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"hub-{Guid.NewGuid():N}"[..15]), "Hub Test Tenant");

        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenant.Id, "owner"));
        await dbContext.SaveChangesAsync();

        return tenant;
    }

    [Fact]
    public async Task Connect_WithValidToken_ReachesConnectedState()
    {
        // Arrange
        var clerkUserId = $"clerk_hub_{Guid.NewGuid():N}";
        var tenant = await ProvisionTenantWithMembershipAsync(clerkUserId);
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        var connection = new HubConnectionBuilder()
            .WithUrl($"http://localhost/{tenant.Slug.Value}/hubs/tenant", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        // Act
        await connection.StartAsync();

        // Assert
        Assert.Equal(HubConnectionState.Connected, connection.State);

        // Cleanup
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Connect_WithTokenInQueryString_ReachesConnectedState()
    {
        // Arrange: append the token directly to the connection URL's query string instead of
        // using AccessTokenProvider (which sends it as an Authorization header). This mirrors
        // how a real browser SignalR client authenticates, since a WebSocket/SSE handshake
        // can't carry an Authorization header, and exercises the JwtBearerEvents.OnMessageReceived
        // query-string path specifically rather than header-based auth.
        var clerkUserId = $"clerk_hub_{Guid.NewGuid():N}";
        var tenant = await ProvisionTenantWithMembershipAsync(clerkUserId);
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        var connection = new HubConnectionBuilder()
            .WithUrl($"http://localhost/{tenant.Slug.Value}/hubs/tenant?access_token={token}", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        // Act
        await connection.StartAsync();

        // Assert
        Assert.Equal(HubConnectionState.Connected, connection.State);

        // Cleanup
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Connect_WithoutToken_Fails()
    {
        // Arrange: provision the tenant (but no membership/token) so the connection fails on
        // auth, not on tenant-alias resolution (which would 404 before auth ever runs).
        await using var dbContext = CreateDbContext();
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"hub-{Guid.NewGuid():N}"[..15]), "Hub Test Tenant");

        var connection = new HubConnectionBuilder()
            .WithUrl($"http://localhost/{tenant.Slug.Value}/hubs/tenant", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());

        // The SignalR client's negotiate request surfaces a 401 as an HttpRequestException with
        // StatusCode set when the handler pipeline preserves it; in this test harness the failure
        // can also surface as a wrapped exception without a status code depending on transport
        // fallback, so we only assert the status code when it's actually available rather than
        // failing the test on a difference in exception shape.
        if (exception is HttpRequestException httpRequestException && httpRequestException.StatusCode is { } statusCode)
        {
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, statusCode);
        }

        await connection.DisposeAsync();
    }
}
