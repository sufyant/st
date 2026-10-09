using System.Reflection;
using Api.Authorization;
using ControlPlane.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Architecture.Tests;

// A5: every endpoint of the composed application carries exactly one access state, public, signed-in only or a permission, and
// every endpoint on the tenant path requires a permission.
public sealed class EndpointAccessTests : IAsyncLifetime
{
    private const string Public = "public";
    private const string SignedIn = "signed-in";
    private const string Permission = "permission";

    private const string TenantPath = "/v1/tenants/";

    // Every endpoint anyone may call without signing in. A new one is added here on purpose.
    private static readonly string[] PublicEndpoints =
    [
        "* /health/live",
        "* /health/ready",
        "GET /openapi/{documentName}.json",
        "GET /scalar/{documentName?}",
        "GET /scalar/scalar.js",
        "GET /scalar/scalar.aspnetcore.js",
        "GET /scalar/favicon.svg",
    ];

    // The permission catalogue: an endpoint asks for one of these by name.
    private static readonly string[] PermissionCatalogue =
    [
        .. typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!),
    ];

    private readonly ComposedApplication _application = new();
    private IReadOnlyList<RouteEndpoint> _endpoints = [];

    public ValueTask InitializeAsync()
    {
        _endpoints = [.. _application.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _application.DisposeAsync();

    [Fact]
    public void StateAccess_EveryEndpoint_HasExactlyOneState()
    {
        var withoutOneState = _endpoints
            .Where(endpoint => StatesOf(endpoint).Length != 1)
            .Select(endpoint => $"{Describe(endpoint)}: [{string.Join(", ", StatesOf(endpoint))}]");

        withoutOneState.ShouldBeEmpty();
    }

    [Fact]
    public void StateAccess_EndpointOnTheTenantPath_RequiresAPermission()
    {
        var tenantEndpoints = _endpoints.Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith(TenantPath, StringComparison.Ordinal));

        var withoutPermission = tenantEndpoints
            .Where(endpoint => !StatesOf(endpoint).SequenceEqual([Permission]))
            .Select(Describe);

        tenantEndpoints.ShouldNotBeEmpty();
        withoutPermission.ShouldBeEmpty();
    }

    [Fact]
    public void StateAccess_PublicEndpoint_IsOnTheList()
    {
        var publicEndpoints = _endpoints.Where(endpoint => StatesOf(endpoint).Contains(Public)).Select(Describe);

        publicEndpoints.ShouldBe(PublicEndpoints, ignoreOrder: true);
    }

    private static string[] StatesOf(RouteEndpoint endpoint) =>
    [
        .. endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null ? Array.Empty<string>() : [Public],
        .. endpoint.Metadata.GetMetadata<SignedInEndpoint>() is null ? Array.Empty<string>() : [SignedIn],
        .. endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => PermissionCatalogue.Contains(data.Policy))
            ? [Permission]
            : Array.Empty<string>(),
    ];

    private static string Describe(RouteEndpoint endpoint) =>
        $"{string.Join(",", endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])} {endpoint.RoutePattern.RawText}";

    // The application as Program composes it, in development, where the API documents are mapped too, in the role that serves the
    // whole API. The host's own start-up checks need a database and its settings; the endpoints do not.
    private sealed class ComposedApplication : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("Host:Role", "all");
            builder.ConfigureTestServices(services =>
            {
                services
                    .Where(service => service.ServiceType == typeof(IHostedService) && service.ImplementationType?.Assembly == typeof(Program).Assembly)
                    .ToList()
                    .ForEach(check => services.Remove(check));
                services.RemoveAll<IStartupValidator>();
            });
        }
    }
}
