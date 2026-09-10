using Application.Abstractions;
using Application.Behaviors;
using Application.Results;
using Domain.Access;
using Domain.Access.Users;
using Infrastructure.Persistence.ControlPlane;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests;

public sealed record SaveBothCommand : ICommand<Unit>;

public sealed record ReadBothQuery : IQuery<Unit>;

public sealed class UnitOfWorkBehaviorTests
{
    [Fact]
    public async Task HandleAsync_ForASuccessfulCommand_SavesBothContexts()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();
        await using var controlPlane = fixture.CreateControlPlane();
        await using var tenant = fixture.CreateTenantDbContext();
        var behavior = NewBehavior<SaveBothCommand>(fixture, controlPlane, tenant);
        var invitee = ExternalUserId.Create("user_invitee");

        // Act
        await behavior.HandleAsync(
            new SaveBothCommand(),
            () =>
            {
                controlPlane.Memberships.Add(Membership.Create(
                    Guid.CreateVersion7(),
                    fixture.Tenant.Id,
                    invitee));
                tenant.Users.Add(TenantUser.Create(Guid.CreateVersion7(), invitee, TenantUserStatus.Active));

                return Task.FromResult(Result.Success());
            },
            TestContext.Current.CancellationToken);

        // Assert
        await using var freshControlPlane = fixture.CreateControlPlane();
        await using var freshTenant = fixture.CreateTenantDbContext();
        Assert.True(await freshControlPlane.Memberships.AnyAsync(
            membership => membership.ExternalUserId == invitee,
            TestContext.Current.CancellationToken));
        Assert.True(await freshTenant.Users.AnyAsync(
            user => user.ExternalUserId == invitee,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HandleAsync_ForAFailedCommand_SavesNothing()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();
        await using var controlPlane = fixture.CreateControlPlane();
        await using var tenant = fixture.CreateTenantDbContext();
        var behavior = NewBehavior<SaveBothCommand>(fixture, controlPlane, tenant);
        var invitee = ExternalUserId.Create("user_rejected");

        // Act
        await behavior.HandleAsync(
            new SaveBothCommand(),
            () =>
            {
                controlPlane.Memberships.Add(Membership.Create(
                    Guid.CreateVersion7(),
                    fixture.Tenant.Id,
                    invitee));

                return Task.FromResult(Result<Unit>.Failure(
                    Error.Conflict("nope", "The command refused.")));
            },
            TestContext.Current.CancellationToken);

        // Assert
        await using var fresh = fixture.CreateControlPlane();
        Assert.False(await fresh.Memberships.AnyAsync(
            membership => membership.ExternalUserId == invitee,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HandleAsync_ForAQuery_SavesNothing()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();
        await using var controlPlane = fixture.CreateControlPlane();
        await using var tenant = fixture.CreateTenantDbContext();
        var behavior = NewBehavior<ReadBothQuery>(fixture, controlPlane, tenant);
        var invitee = ExternalUserId.Create("user_query");

        // Act
        await behavior.HandleAsync(
            new ReadBothQuery(),
            () =>
            {
                controlPlane.Memberships.Add(Membership.Create(
                    Guid.CreateVersion7(),
                    fixture.Tenant.Id,
                    invitee));

                return Task.FromResult(Result.Success());
            },
            TestContext.Current.CancellationToken);

        // Assert
        await using var fresh = fixture.CreateControlPlane();
        Assert.False(await fresh.Memberships.AnyAsync(
            membership => membership.ExternalUserId == invitee,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HandleAsync_WhenTheTenantSaveFails_KeepsTheControlPlaneWrite()
    {
        // Arrange
        await using var fixture = await TenantSurfaceFixture.StartAsync();
        await using var controlPlane = fixture.CreateControlPlane();
        await using var tenant = fixture.CreateTenantDbContext();
        var behavior = NewBehavior<SaveBothCommand>(fixture, controlPlane, tenant);
        var invitee = ExternalUserId.Create("user_partial");

        // Act
        var act = async () => await behavior.HandleAsync(
            new SaveBothCommand(),
            () =>
            {
                controlPlane.Memberships.Add(Membership.Create(
                    Guid.CreateVersion7(),
                    fixture.Tenant.Id,
                    invitee));
                var user = TenantUser.Create(Guid.CreateVersion7(), invitee, TenantUserStatus.Active);
                tenant.Users.Add(user);
                tenant.UserRoles.Add(TenantUserRole.Create(user.Id, Guid.CreateVersion7()));

                return Task.FromResult(Result.Success());
            },
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAnyAsync<Exception>(act);
        await using var freshControlPlane = fixture.CreateControlPlane();
        await using var freshTenant = fixture.CreateTenantDbContext();
        Assert.True(await freshControlPlane.Memberships.AnyAsync(
            membership => membership.ExternalUserId == invitee,
            TestContext.Current.CancellationToken));
        Assert.False(await freshTenant.Users.AnyAsync(
            user => user.ExternalUserId == invitee,
            TestContext.Current.CancellationToken));
    }

    private static UnitOfWorkBehavior<TRequest, Unit> NewBehavior<TRequest>(
        TenantSurfaceFixture fixture,
        ControlPlaneDbContext controlPlane,
        TenantDbContext tenant)
        where TRequest : IRequest<Unit>
    {
        var tenantContext = new TenantContext();
        tenantContext.Set(
            fixture.Tenant.Id,
            fixture.Tenant.Alias.Value,
            fixture.Tenant.DatabaseName.Value);
        var provider = new ServiceCollection()
            .AddSingleton(controlPlane)
            .AddSingleton(tenant)
            .BuildServiceProvider();

        return new UnitOfWorkBehavior<TRequest, Unit>(provider, tenantContext);
    }
}
