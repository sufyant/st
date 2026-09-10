using Api.Http;
using Api.Tenants;
using Application;
using System.Security.Claims;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Tenants;
using Api.Features.Invitations;
using Api.Features.Me;
using Api.Features.Members;
using Api.Features.Roles;
using ControlPlane;
using Infrastructure.Messaging;
using Infrastructure.Tenants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Application.Abstractions;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());
builder.Services.AddPostgresReadiness(builder.Configuration);
builder.Services.AddTenantPersistence(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddApplication();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<TenantResolver>();
builder.Services.AddScoped(provider =>
{
    var tenantContext = provider.GetRequiredService<TenantContext>();
    var factory = provider.GetRequiredService<TenantDbContextFactory>();

    return factory.Create(tenantContext.DatabaseName);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<OutboxProcessor>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Authority = builder.Configuration["Clerk:Issuer"];
        options.Audience = builder.Configuration["Clerk:Audience"];
    });
builder.Services.AddControlPlane();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseMiddleware<TenantAccessMiddleware>();
// Placed after tenant resolution so the enriched fields include the tenant the request reached.
app.UseMiddleware<RequestEnrichmentMiddleware>();
app.UseAuthorization();
app.MapOpenApi();
app.MapControlPlane();
app.MapTenantInvitations();
app.MapMe();
app.MapTenantMembers();
app.MapTenantRoles();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapGet("/{tenantAlias}/api/v1/whoami", (TenantContext tenantContext, ClaimsPrincipal user) => Results.Ok(new
{
    tenantId = tenantContext.TenantId,
    tenantAlias = tenantContext.Alias,
    userId = user.FindFirstValue("sub")
})).RequireAuthorization();

app.Run();

public partial class Program;
