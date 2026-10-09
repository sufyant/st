using System.Diagnostics;
using System.Reflection;
using Api.Authentication;
using Api.Authorization;
using Api.ErrorHandling;
using Api.Messaging;
using Api.Networking;
using Api.Observability;
using Api.Persistence;
using Api.RateLimiting;
using Api.Tenants;
using JasperFx.CodeGeneration.Model;
using Npgsql;
using Scalar.AspNetCore;
using Serilog;
using Wolverine;
using Wolverine.FluentValidation;

namespace Api;

// The shared pipeline every request and command passes through. Program composes it with the modules;
// the integration tests compose it with test endpoints.
internal static class ApiPipeline
{
    private const string HealthPath = "/health";

    // FluentValidation validators are found only in the handler assemblies known when validation is switched on, so they are
    // passed here rather than added to Wolverine's discovery afterwards.
    public static WebApplicationBuilder AddApiPipeline(this WebApplicationBuilder builder, params Assembly[] handlerAssemblies)
    {
        builder.Services.AddSingleton(TimeProvider.System);

        builder.AddObservability();
        builder.AddPersistence();
        builder.AddClerkAuthentication();
        builder.Services.AddAccessAuthorization();

        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        builder.Services.AddExceptionHandler<ValidationExceptionHandler>();

        builder.Services.AddOptions<PipelineOptions>()
            .BindConfiguration(PipelineOptions.Section)
            .Validate(options => options.SlowCommandThreshold > TimeSpan.Zero, "Pipeline:SlowCommandThreshold must be positive.")
            .ValidateOnStart();
        builder.Services.AddWolverine(options =>
        {
            foreach (var assembly in handlerAssemblies)
            {
                options.Discovery.IncludeAssembly(assembly);
            }

            // Module DbContexts are built by factories on the scope's tenant connection, which Wolverine can only resolve
            // from the message's scope.
            options.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;

            // Wolverine takes its data source while it is configured. The build writes the OpenAPI document, and the migration step
            // runs, from a host without the setting; a host that runs without it keeps no messages and is never ready
            // (DatabaseHealthCheck).
            if (builder.Configuration.GetConnectionString(PersistenceExtensions.DirectConnection) is { Length: > 0 } direct)
            {
                MessageStorage.Configure(options, NpgsqlDataSource.Create(direct));
            }

            // A message that fails for good goes to the dead letter queue, and its fault is published for a flow that has to react,
            // such as a saga's compensation. Faults are stored messages, so they carry only the exception's type.
            options.PublishFaultEvents(includeExceptionMessage: false, includeStackTrace: false);

            options.UseFluentValidation();
            options.Policies.AddMiddleware(typeof(CommandDurationMiddleware));
            options.Policies.Add<TenantTransactionPolicy>();
        });

        builder.Services.AddTenantRateLimiting();
        builder.Services.AddHealthChecks();
        builder.Services.AddOpenApi();

        return builder;
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

        // Routing runs first so tenant resolution sees the tenant id, and the rate limiter the resolved tenant. Authorization comes
        // last, so callers it turns away have been rate limited too.
        app.UseRouting();
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapHealthChecks($"{HealthPath}/live", new() { Predicate = _ => false });
        app.MapHealthChecks($"{HealthPath}/ready", new() { Predicate = check => check.Tags.Contains("ready") });

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        return app;
    }

    // Every API route lives under a version segment; expected failures returned as results become Problem Details here.
    // Every endpoint in it needs a signed-in user unless it says otherwise.
    public static RouteGroupBuilder MapV1(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/v1")
            .AddEndpointFilter<ResultEndpointFilter>()
            .DescribeResults()
            .RequireRateLimiting(TenantRateLimiting.Policy)
            .RequireAuthorization();
}
