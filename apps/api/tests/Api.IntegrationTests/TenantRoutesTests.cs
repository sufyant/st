using Api.Tenants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Api.IntegrationTests;

// T7, as an architecture test over the endpoints the application really maps; it needs the composed host, so it lives here.
public sealed class TenantRoutesTests(Database database)
{
    [Fact]
    public async Task CheckTenantRoutes_EveryEndpointOfTheApplication_IsBehindTheTenantDoorOrOutsideATenantPath()
    {
        await using var api = new ApiFactory(database.ApplicationConnectionString);

        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        endpoints.OfType<RouteEndpoint>().ShouldContain(endpoint => endpoint.RoutePattern.RawText!.StartsWith(TenantRoutesCheck.TenantPathPrefix, StringComparison.Ordinal));
        TenantRoutesCheck.Gaps(endpoints).ShouldBeEmpty();
    }

    [Fact]
    public async Task CheckTenantRoutes_EveryEndpointOfTheTestHost_IsBehindTheTenantDoorOrOutsideATenantPath()
    {
        await using var host = await PipelineHost.StartAsync();

        var gaps = TenantRoutesCheck.Gaps(host.Services.GetRequiredService<EndpointDataSource>().Endpoints);

        gaps.ShouldBeEmpty();
    }

    [Fact]
    public void CheckTenantRoutes_AnEndpointOnATenantPathOutsideTheGroup_IsAGap()
    {
        var app = WebApplication.CreateBuilder().Build();
        var v1 = app.MapV1();
        v1.MapTenant().MapGet("/ping", () => Results.Ok());
        v1.MapGet("/tenants/{tenantId:guid}/stray", () => Results.Ok());

        var gaps = TenantRoutesCheck.Gaps(EndpointsOf(app));

        gaps.ShouldBe(
        [
            "/v1/tenants/{tenantId:guid}/stray: on a tenant path, without the tenant metadata",
            "/v1/tenants/{tenantId:guid}/stray: on a tenant path, without the membership requirement",
        ]);
    }

    // Authorization skips an anonymous endpoint, and with it the membership requirement.
    [Fact]
    public void CheckTenantRoutes_AnAnonymousEndpointInTheGroup_IsAGap()
    {
        var app = WebApplication.CreateBuilder().Build();
        var v1 = app.MapV1();
        v1.MapTenant().MapGet("/open", () => Results.Ok()).AllowAnonymous();

        var gaps = TenantRoutesCheck.Gaps(EndpointsOf(app));

        gaps.ShouldBe(["/v1/tenants/{tenantId:guid}/open: on a tenant path, without the membership requirement"]);
    }

    private static IEnumerable<Endpoint> EndpointsOf(IEndpointRouteBuilder app) => app.DataSources.SelectMany(source => source.Endpoints);
}
