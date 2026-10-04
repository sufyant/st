using ControlPlane.Application.Ports;
using ControlPlane.Domain.Roles;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ControlPlane.IntegrationTests;

// Cross-tenant reports read the catalog through the read-only reporting role (0018, 0031).
public sealed class TenantReportTests(Database database)
{
    [Fact]
    public async Task The_report_lists_tenants_with_their_member_counts()
    {
        var crowded = await Catalog.AddTenantAsync(database.Services, slug: $"0report-{Guid.NewGuid():N}"[..20]);
        var empty = await Catalog.AddTenantAsync(database.Services, slug: $"0report-{Guid.NewGuid():N}"[..20]);
        await Catalog.AddMemberAsync(database.Services, crowded, BuiltInRoles.Owner);
        await Catalog.AddMemberAsync(database.Services, crowded, BuiltInRoles.Member);

        var report = await ListTenantsAsync(database.Services, page: 1, pageSize: 100);

        report.Items.Where(tenant => tenant.Id == crowded.Id || tenant.Id == empty.Id)
            .Select(tenant => (tenant.Slug, tenant.Status, tenant.MemberCount))
            .ShouldBe([(crowded.Slug, "Active", 2), (empty.Slug, "Active", 0)], ignoreOrder: true);
    }

    [Fact]
    public async Task The_report_is_paginated()
    {
        await Catalog.AddTenantAsync(database.Services);
        await Catalog.AddTenantAsync(database.Services);

        var report = await ListTenantsAsync(database.Services, page: 2, pageSize: 1);

        report.Items.Count.ShouldBe(1);
        (report.Page, report.PageSize).ShouldBe((2, 1));
        report.TotalCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    // Pointed at a role without privileges on the catalog, the report fails: it does not fall back to the application's connection.
    [Fact]
    public async Task The_report_reads_through_the_reporting_connection()
    {
        await using var services = database.BuildServices(reportingConnectionString: await database.CreateLoginRoleAsync());

        var report = () => ListTenantsAsync(services, page: 1, pageSize: 1);

        (await report.ShouldThrowAsync<PostgresException>()).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static async Task<SharedKernel.PagedList<TenantSummary>> ListTenantsAsync(IServiceProvider services, int page, int pageSize)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ITenantReport>().ListTenantsAsync(page, pageSize, TestContext.Current.CancellationToken);
    }
}
