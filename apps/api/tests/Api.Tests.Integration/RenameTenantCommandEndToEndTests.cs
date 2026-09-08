using System.Security.Claims;
using Api.Application;
using Api.Application.Tenants;
using Api.Domain;
using Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class RenameTenantCommandEndToEndTests(PostgresContainerFixture fixture)
{
    private (IMediator Mediator, AdminDbContext DbContext) BuildMediator(ClaimsPrincipal? user)
    {
        var services = new ServiceCollection();

        services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = user is null ? null : new DefaultHttpContext { User = user },
        };
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);

        services.AddScoped<IMediator, Mediator>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(PermissionBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(SaveChangesUnitOfWorkBehavior<,>));

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IRequestHandler<RenameTenantCommand, Result>, RenameTenantCommandHandler>();
        services.AddScoped<FluentValidation.IValidator<RenameTenantCommand>, RenameTenantCommandValidator>();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMediator>(), scope.ServiceProvider.GetRequiredService<AdminDbContext>());
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

    [Fact]
    public async Task Send_ValidCommandWithPermission_RenamesTenantInDatabase()
    {
        // Arrange
        var tenant = Tenant.Create(TenantSlug.Create($"e2e-{Guid.NewGuid():N}"[..15]), "Original Name");
        var (mediator, setupContext) = BuildMediator(AuthenticatedUserWithPermission("tenant.rename", tenant.Id));
        setupContext.Tenants.Add(tenant);
        await setupContext.SaveChangesAsync();

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
        var (mediator, setupContext) = BuildMediator(AuthenticatedUserWithPermission("tenant.rename", tenant.Id));
        setupContext.Tenants.Add(tenant);
        await setupContext.SaveChangesAsync();

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
        var (mediator, setupContext) = BuildMediator(AuthenticatedUserWithPermission("some.other.permission", tenant.Id));
        setupContext.Tenants.Add(tenant);
        await setupContext.SaveChangesAsync();

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
        var (mediator, setupContext) = BuildMediator(AuthenticatedUserWithPermission("tenant.rename", tenantA.Id));
        setupContext.Tenants.AddRange(tenantA, tenantB);
        await setupContext.SaveChangesAsync();

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
