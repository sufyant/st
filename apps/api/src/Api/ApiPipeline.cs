using System.Diagnostics;
using System.Reflection;
using Api.Authentication;
using Api.Authorization;
using Api.ErrorHandling;
using Api.Hosting;
using Api.Messaging;
using Api.Networking;
using Api.Observability;
using Api.Persistence;
using Api.RateLimiting;
using Api.Tenants;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Npgsql;
using Scalar.AspNetCore;
using Serilog;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;

namespace Api;

// The shared pipeline every request and command passes through. Program composes it with the modules;
// the integration tests compose it with test endpoints.
internal static class ApiPipeline
{
    private const string HealthPath = "/health";

    private const string IdempotencyKeyHeader = "Idempotency-Key";

    // FluentValidation validators are found only in the handler assemblies known when validation is switched on, so they are
    // passed here rather than added to Wolverine's discovery afterwards. Returns the role the host runs as, or null when the setting
    // is wrong and the host will not start.
    public static HostRole? AddApiPipeline(this WebApplicationBuilder builder, params Assembly[] handlerAssemblies)
    {
        // The OpenAPI document build only describes the API: it has no configuration and no database, so it registers no database
        // and checks nothing while it starts. Every other start checks every setting first (section 7).
        var openApiBuild = OpenApiDocumentBuild.IsRunning;
        if (openApiBuild)
        {
            builder.Services.AddSingleton<IStartupValidator, OpenApiDocumentBuild.NothingToCheck>();
        }
        else
        {
            builder.AddPersistence();
        }

        var host = builder.AddHostSettings();
        var role = openApiBuild ? HostRole.All : host.RoleOrNull;
        builder.Services.AddSingleton(TimeProvider.System);

        builder.AddObservability();
        builder.AddClerkAuthentication();
        builder.Services.AddTrustedProxies();
        builder.Services.AddAccessAuthorization();

        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        builder.Services.AddExceptionHandler<ValidationExceptionHandler>();

        // A request an endpoint cannot read answers 400 in every environment. Development would otherwise throw, and the exception
        // handler would answer 500 (OWASP API8).
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

        builder.Services.AddWolverine(options =>
        {
            foreach (var assembly in handlerAssemblies)
            {
                options.Discovery.IncludeAssembly(assembly);
            }

            // Wolverine takes its data source while it is configured. The migration step composes the host without the setting and
            // never starts it; any other host without it does not start (PersistenceExtensions).
            if (!openApiBuild && PersistenceExtensions.MessagingConnectionOf(builder.Configuration) is { ConnectionString: var messaging })
            {
                MessageStorage.Configure(options, NpgsqlDataSource.Create(messaging), role);
            }

            // A stopping host stops taking messages and finishes the ones it has, within the host's shutdown time.
            options.Durability.DrainTimeout = host.ShutdownTimeout;

            // A message that fails for good goes to the dead letter queue, and its fault is published for a flow that has to react,
            // such as a saga's compensation. Faults are stored messages, so they carry only the exception's type.
            options.PublishFaultEvents(includeExceptionMessage: false, includeStackTrace: false);

            // Of two messages a saga handles at once, the second fails on the saga's version and is tried again on the first one's
            // result (W6, S8). Wolverine does not retry this by itself.
            options.OnException<SagaConcurrencyException>().RetryTimes(3);

            // Wolverine's EF Core middleware begins, saves and commits the transaction of every handler that uses a module DbContext,
            // with the messages the handler sends in it (W1). Each handler of a message runs in its own transaction (W4).
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AutoApplyTransactions();
            options.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;

            options.UseFluentValidation();
        });

        // API8: only the configured browser origins may call, with the methods and headers the API uses, and without credentials
        // mode: the API reads its caller from the Authorization header, never from a cookie.
        builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
            .WithOrigins(host.Cors.AllowedOrigins)
            .WithMethods(HttpMethods.Get, HttpMethods.Post)
            .WithHeaders(HeaderNames.Authorization, HeaderNames.ContentType, IdempotencyKeyHeader)));

        builder.Services.AddUserRateLimiting();
        builder.Services.AddHealthChecks();
        builder.Services.AddOpenApi();

        return role;
    }

    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        // The client address is settled first, so logging and rate limiting see the client rather than the proxy.
        app.UseTrustedProxies();

        // Request logging sits outside the exception handler so it logs the 500 response, not the exception a second time.
        // Health checks are polled every few seconds and would drown the log.
        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments(HealthPath),
            requests => requests.UseSerilogRequestLogging(options => options.Logger = app.Services.GetRequiredService<Serilog.ILogger>()));
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        // Routing runs first so tenant resolution sees the tenant id. A browser's preflight is answered before anything asks who the
        // caller is. The rate limiter needs only the user, so a caller over their limit costs no membership lookup. Authorization comes
        // last, so callers it turns away have been rate limited too.
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();

        app.MapHealthChecks($"{HealthPath}/live", new() { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks($"{HealthPath}/ready", new() { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

        return app;
    }

    // The API's documents, in Development only, on a host that serves the API.
    public static WebApplication MapApiDocuments(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference().AllowAnonymous();
        }

        return app;
    }

    // Every API route lives under a version segment; expected failures returned as results become Problem Details here.
    // The group grants no access: each endpoint states its own (A5).
    public static RouteGroupBuilder MapV1(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/v1")
            .AddEndpointFilter<ResultEndpointFilter>()
            .DescribeResults()
            .RequireUserRateLimit();

    // The endpoints of the version group that any signed-in user may call, without a permission (A5).
    public static RouteGroupBuilder MapSignedIn(this RouteGroupBuilder v1) => v1.MapGroup("").RequireSignedIn();
}
