using System.Security.Claims;
using Api.Application;
using Api.Application.Tenants;
using Api.Domain;
using Api.Infrastructure;
using Api.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Api.Tests.TenantIsolation;

// This test deliberately duplicates the assertion already made by
// Api.Tests.Integration/RenameTenantCommandEndToEndTests.Send_MismatchedTenantId_...
// (see ADR 0025): that overlap is intentional so this dedicated tenant-isolation project
// independently proves the guarantee holds, rather than only relying on it being covered
// incidentally by a feature-oriented test elsewhere.
//
// An HTTP-level "cross-tenant write" test cannot actually express an attack here: the rename
// endpoint reads its target tenant id from the authenticated tenant_id claim/URL alias, not
// from the request body, so there is no request shape through which a caller authenticated for
// tenant A could ask to rename tenant B over HTTP. The real defense this system relies on is
// PermissionBehavior's ITenantScopedRequest check in the MediatR pipeline (the "Tenant.Mismatch"
// rejection), so this test dispatches RenameTenantCommand directly via IMediator with a
// mismatched TenantId to exercise that check the way RenameTenantCommandEndToEndTests does.
[Collection(nameof(PostgresCollection))]
public sealed class CrossTenantWriteTests(PostgresContainerFixture fixture)
{
    private (IMediator Mediator, AdminDbContext DbContext) BuildMediator(ClaimsPrincipal user)
    {
        var services = new ServiceCollection();

        services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = user },
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
    public async Task AuthenticatedForTenantA_CannotRenameTenantB_TenantMismatchRejectedAndTenantBUntouched()
    {
        // Arrange — caller is authenticated for Tenant A (has tenant.rename there) but the
        // command targets Tenant B, a tenant they hold no membership in whatsoever.
        var tenantA = Tenant.Create(TenantSlug.Create($"iso-wr-a-{Guid.NewGuid():N}"[..15]), "Tenant A");
        var tenantB = Tenant.Create(TenantSlug.Create($"iso-wr-b-{Guid.NewGuid():N}"[..15]), "Tenant B Original");
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
        var reloadedB = await verifyContext.Tenants.SingleAsync(t => t.Id == tenantB.Id);
        Assert.Equal("Tenant B Original", reloadedB.Name);
    }
}
