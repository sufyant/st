using Application.Abstractions;
using Domain.ControlPlane.Tenants;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Tenants;
using Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public sealed class TenantDbContextScopeTests
{
    [Fact]
    public void ResolvingTenantDbContext_BeforeTheTenantIsResolved_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<TenantContext>();
        services.AddScoped(_ => new TenantDbContextFactory(
            "Host=localhost;Username=st_tenant",
            new AuditInterceptor(TimeProvider.System)));
        services.AddScoped(provider =>
        {
            var tenantContext = provider.GetRequiredService<TenantContext>();

            return provider.GetRequiredService<TenantDbContextFactory>()
                .Create(tenantContext.DatabaseName);
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var act = () => scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        // Assert
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Create_WithCredentials_ConnectsAsTheGivenRole()
    {
        // Arrange
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var provisioner = new TenantProvisioner(postgres.GetConnectionString(), new AuditInterceptor(TimeProvider.System));
        var databaseName = TenantDatabaseName.Create("tenant_acme");
        var roleName = TenantRoleName.Create("access_acme");
        await provisioner.CreateDatabaseAsync(databaseName, TestContext.Current.CancellationToken);
        await provisioner.MigrateSchemaAsync(databaseName, TestContext.Current.CancellationToken);
        var password = await provisioner.GrantTenantAccessAsync(
            databaseName, roleName, TestContext.Current.CancellationToken);
        var factory = new TenantDbContextFactory(postgres.GetConnectionString(), new AuditInterceptor(TimeProvider.System));

        // Act
        await using var context = factory.Create(databaseName.Value, roleName.Value, password);
        var count = await context.Users.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, count);
    }
}
