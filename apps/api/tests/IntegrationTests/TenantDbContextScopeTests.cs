using Application.Abstractions;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Tenants;
using Microsoft.Extensions.DependencyInjection;
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
}
