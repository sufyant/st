using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public sealed class LoggingEnrichmentTests : IDisposable
{
    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentBag<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private readonly PostgresContainerFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly CapturingSink _sink = new();

    public LoggingEnrichmentTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
        _factory = new CustomWebApplicationFactory(fixture.ConnectionString, _sink);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task RenameTenantCommand_EmitsLogsEnrichedWithTenantAndUserAndRequestId()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(_fixture.ConnectionString).Options;
        await using var dbContext = new AdminDbContext(options);
        var provisioningService = new TenantProvisioningService(dbContext);
        var tenant = await provisioningService.ProvisionAsync(
            TenantSlug.Create($"log-{Guid.NewGuid():N}"[..15]), "Original Name");

        var clerkUserId = $"clerk_log_{Guid.NewGuid():N}";
        var user = User.Create(clerkUserId, Email.Create($"{Guid.NewGuid():N}@example.com"));
        dbContext.Users.Add(user);
        dbContext.Memberships.Add(Membership.Create(user.Id, tenant.Id, "owner"));
        await dbContext.SaveChangesAsync();

        // Other test classes sharing this Postgres container may have already seeded this
        // exact role/permission pair, so check first rather than blindly inserting (matches
        // RenameTenantEndpointTests.EnsureRolePermissionAsync's pattern).
        var hasPermission = await dbContext.RolePermissions
            .AnyAsync(rp => rp.Role == "owner" && rp.Permission == "tenant.rename");
        if (!hasPermission)
        {
            dbContext.RolePermissions.Add(RolePermission.Create("owner", "tenant.rename"));
            await dbContext.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var token = TestJwtTokenFactory.CreateToken(clerkUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        await client.PutAsJsonAsync($"/{tenant.Slug.Value}/api/v1/tenant", new { NewName = "Renamed" });

        // Assert
        // Snapshot into an array before asserting: ConcurrentBag<T>'s enumerator is safe
        // against concurrent Emit() calls, but taking an explicit snapshot avoids enumerating
        // a live collection while ASP.NET Core's own request-pipeline logging may still be
        // writing to it.
        var events = _sink.Events.ToArray();
        Assert.Contains(events, e =>
            e.Properties.TryGetValue("tenant_id", out var tenantIdProp)
            && tenantIdProp.ToString().Contains(tenant.Id.ToString())
            && e.Properties.ContainsKey("request_id")
            && e.Properties.TryGetValue("user_id", out var userIdProp)
            && userIdProp.ToString().Contains(clerkUserId));
    }
}
