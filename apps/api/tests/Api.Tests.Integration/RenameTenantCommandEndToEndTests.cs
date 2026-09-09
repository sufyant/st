using System.Security.Claims;
using Api.Application;
using Api.Application.Tenants;
using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests.Integration;

// IMediator is resolved from the real host (CustomWebApplicationFactory, the same composition
// root Program.cs builds) rather than a hand-built ServiceCollection, so these tests exercise the
// actual registered pipeline behavior order (Logging -> Permission -> Validation -> SaveChanges),
// not a test-local reimplementation that could silently drift from production.
[Collection(nameof(PostgresCollection))]
public sealed class RenameTenantCommandEndToEndTests(PostgresContainerFixture fixture) : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new(fixture.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private AdminDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        return new AdminDbContext(options);
    }

    private static ClaimsPrincipal AuthenticatedUserWithPermission(string permission, Guid tenantId)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", $"clerk_{Guid.NewGuid():N}"),
                new Claim("permission", permission),
                new Claim("tenant_id", tenantId.ToString()),
            ],
            authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    private IMediator ResolveMediatorAs(ClaimsPrincipal user, out IServiceScope scope)
    {
        var httpContextAccessor = _factory.Services.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = new DefaultHttpContext { User = user };

        scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Send_ValidCommandWithPermission_RenamesTenantInDatabase()
    {
        // Arrange
        var tenant = Tenant.Create(TenantSlug.Create($"e2e-{Guid.NewGuid():N}"[..15]), "Original Name");
        await using var setupContext = CreateDbContext();
        setupContext.Tenants.Add(tenant);
        await setupContext.SaveChangesAsync();

        var mediator = ResolveMediatorAs(
            AuthenticatedUserWithPermission("tenant.rename", tenant.Id), out var scope);
        using var _ = scope;

        // Act
        var result = await mediator.Send(new RenameTenantCommand(tenant.Id, "Renamed"));

        // Assert
        Assert.True(result.IsSuccess);
        var verifyOptions = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var verifyContext = new AdminDbContext(verifyOptions);
        var reloaded = await verifyContext.Tenants.SingleAsync(t => t.Id == tenant.Id);
        Assert.Equal("Renamed", reloaded.Name);

        // The real SaveChangesUnitOfWorkBehavior (not a test-local copy) must have
        // drained the TenantRenamedDomainEvent into the outbox in the same save,
        // scoped to this test's own tenant to avoid Finding 2's unscoped-query hazard.
        var outboxMessage = await verifyContext.OutboxMessages.SingleAsync(m =>
            m.Type == typeof(TenantRenamedDomainEvent).FullName && m.Content.Contains(tenant.Id.ToString()));
        Assert.Null(outboxMessage.ProcessedAtUtc);
    }

    [Fact]
    public async Task Send_InvalidCommand_ReturnsFailureAndDoesNotPersist()
    {
        // Arrange
        var tenant = Tenant.Create(TenantSlug.Create($"e2e-{Guid.NewGuid():N}"[..15]), "Original Name");
        await using var setupContext = CreateDbContext();
        setupContext.Tenants.Add(tenant);
        await setupContext.SaveChangesAsync();

        var mediator = ResolveMediatorAs(
            AuthenticatedUserWithPermission("tenant.rename", tenant.Id), out var scope);
        using var _ = scope;

        // Act
        var result = await mediator.Send(new RenameTenantCommand(tenant.Id, ""));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Validation.Failed", result.Error.Code);

        var verifyOptions = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var verifyContext = new AdminDbContext(verifyOptions);
        var reloaded = await verifyContext.Tenants.SingleAsync(t => t.Id == tenant.Id);
        Assert.Equal("Original Name", reloaded.Name);
    }

    [Fact]
    public async Task Send_NoPermission_ReturnsFailureAndDoesNotPersist()
    {
        // Arrange
        var tenant = Tenant.Create(TenantSlug.Create($"e2e-{Guid.NewGuid():N}"[..15]), "Original Name");
        await using var setupContext = CreateDbContext();
        setupContext.Tenants.Add(tenant);
        await setupContext.SaveChangesAsync();

        var mediator = ResolveMediatorAs(
            AuthenticatedUserWithPermission("some.other.permission", tenant.Id), out var scope);
        using var _ = scope;

        // Act
        var result = await mediator.Send(new RenameTenantCommand(tenant.Id, "Should Not Apply"));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Permission.Denied", result.Error.Code);

        var verifyOptions = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var verifyContext = new AdminDbContext(verifyOptions);
        var reloaded = await verifyContext.Tenants.SingleAsync(t => t.Id == tenant.Id);
        Assert.Equal("Original Name", reloaded.Name);
    }

    [Fact]
    public async Task Send_MismatchedTenantId_ReturnsTenantMismatchAndDoesNotPersist()
    {
        // Arrange — caller is authenticated for Tenant A but targets Tenant B.
        var tenantA = Tenant.Create(TenantSlug.Create($"e2e-{Guid.NewGuid():N}"[..15]), "Tenant A");
        var tenantB = Tenant.Create(TenantSlug.Create($"e2e-{Guid.NewGuid():N}"[..15]), "Tenant B Original");
        await using var setupContext = CreateDbContext();
        setupContext.Tenants.AddRange(tenantA, tenantB);
        await setupContext.SaveChangesAsync();

        var mediator = ResolveMediatorAs(
            AuthenticatedUserWithPermission("tenant.rename", tenantA.Id), out var scope);
        using var _ = scope;

        // Act
        var result = await mediator.Send(new RenameTenantCommand(tenantB.Id, "Hijacked Name"));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Mismatch", result.Error.Code);

        var verifyOptions = new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var verifyContext = new AdminDbContext(verifyOptions);
        var reloaded = await verifyContext.Tenants.SingleAsync(t => t.Id == tenantB.Id);
        Assert.Equal("Tenant B Original", reloaded.Name);
    }
}
