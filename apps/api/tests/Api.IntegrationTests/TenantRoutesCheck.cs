using Api.Authorization;
using Api.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.IntegrationTests;

// T7: membership is checked in one place, the tenant group. TenantResolutionMiddleware reads the membership for an endpoint that
// carries the tenant metadata, and the membership requirement turns away a caller without one. So an endpoint on a tenant path
// must carry both, and must not let anonymous callers skip authorization; the tenant metadata on any other path is a mistake.
internal static class TenantRoutesCheck
{
    public const string TenantPathPrefix = "/v1/tenants/{tenantId";

    public static List<string> Gaps(IEnumerable<Endpoint> endpoints) =>
        [.. endpoints.OfType<RouteEndpoint>().SelectMany(Gaps)];

    private static IEnumerable<string> Gaps(RouteEndpoint endpoint)
    {
        var path = endpoint.RoutePattern.RawText ?? string.Empty;
        var onTenantPath = path.StartsWith(TenantPathPrefix, StringComparison.Ordinal);
        var tenantMetadata = endpoint.Metadata.GetMetadata<TenantScopedEndpoint>() is not null;
        var membershipRequired = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy == AccessPolicies.TenantMember)
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;

        if (onTenantPath && !tenantMetadata)
        {
            yield return $"{path}: on a tenant path, without the tenant metadata";
        }

        if (onTenantPath && !membershipRequired)
        {
            yield return $"{path}: on a tenant path, without the membership requirement";
        }

        if (tenantMetadata && !onTenantPath)
        {
            yield return $"{path}: carries the tenant metadata, outside a tenant path";
        }
    }
}
