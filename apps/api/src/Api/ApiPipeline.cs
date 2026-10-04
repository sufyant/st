using System.Diagnostics;
using System.Reflection;
using Api.ErrorHandling;
using Api.Messaging;
using Api.Networking;
using Api.Observability;
using Api.Persistence;
using Api.RateLimiting;
using Api.Tenants;
using JasperFx.CodeGeneration.Model;
using Scalar.AspNetCore;
using Serilog;
using Wolverine;
using Wolverine.FluentValidation;

namespace Api;

// The shared pipeline every request and command passes through (0022). Program composes it with the modules;
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

            // Module DbContexts are built by factories on the scope's tenant connection (0016), which Wolverine can only resolve
            // from the message's scope.
            options.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;

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

        // Routing runs first so tenant resolution sees the slug, and the rate limiter the resolved tenant.
        app.UseRouting();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseRateLimiter();

        app.MapHealthChecks($"{HealthPath}/live", new() { Predicate = _ => false });
        app.MapHealthChecks($"{HealthPath}/ready", new() { Predicate = check => check.Tags.Contains("ready") });

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        return app;
    }

    // Every API route lives under a version segment (0033); expected failures returned as results become Problem Details here.
    public static RouteGroupBuilder MapV1(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("/v1")
            .AddEndpointFilter<ResultEndpointFilter>()
            .RequireRateLimiting(TenantRateLimiting.Policy);
}
