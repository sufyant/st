using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services.AddDbContext<Api.Infrastructure.AdminDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("AdminDb"),
        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Api.Infrastructure.AdminDbContext.SchemaName)));

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<Api.Application.IMediator, Api.Application.Mediator>();

// Registration order = execution order Logging -> Permission -> Validation -> Handler
// -> SaveChanges, matching ADR 0006 (first-registered runs outermost, per
// Mediator.Send's .Reverse() composition).
builder.Services.AddScoped(
    typeof(Api.Application.IPipelineBehavior<,>), typeof(Api.Application.LoggingBehavior<,>));
builder.Services.AddScoped(
    typeof(Api.Application.IPipelineBehavior<,>), typeof(Api.Infrastructure.PermissionBehavior<,>));
builder.Services.AddScoped(
    typeof(Api.Application.IPipelineBehavior<,>), typeof(Api.Application.ValidationBehavior<,>));
builder.Services.AddScoped(
    typeof(Api.Application.IPipelineBehavior<,>), typeof(Api.Infrastructure.SaveChangesUnitOfWorkBehavior<,>));

builder.Services.AddHostedService<Api.Infrastructure.OutboxProcessor>();

builder.Services.AddScoped<Api.Application.Tenants.ITenantRepository, Api.Infrastructure.TenantRepository>();
builder.Services.AddScoped<
    Api.Application.IRequestHandler<Api.Application.Tenants.RenameTenantCommand, Api.Application.Result>,
    Api.Application.Tenants.RenameTenantCommandHandler>();
builder.Services.AddScoped<
    FluentValidation.IValidator<Api.Application.Tenants.RenameTenantCommand>,
    Api.Application.Tenants.RenameTenantCommandValidator>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Authority = builder.Configuration["Clerk:Authority"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Clerk:Authority"],
            ValidateAudience = false,
            ValidateLifetime = true,
            NameClaimType = "sub",
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("tenant.whoami", policy =>
        policy.Requirements.Add(new Api.Infrastructure.PermissionRequirement("tenant.whoami")));
});
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Api.Infrastructure.PermissionAuthorizationHandler>();

var app = builder.Build();

app.UseRouting();
app.MapOpenApi();
app.UseAuthentication();
app.UseMiddleware<Api.Infrastructure.TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapHub<Api.Host.Hubs.TenantHub>("/hubs/tenant");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/{tenant-alias}/api/v1/whoami", (HttpContext context) =>
{
    var userId = context.User.FindFirstValue("sub");
    var tenantId = context.User.FindFirstValue("tenant_id");
    var role = context.User.FindFirstValue("membership_role");
    return Results.Ok(new { userId, tenantId, role });
}).RequireAuthorization("tenant.whoami");

app.MapPut("/{tenant-alias}/api/v1/tenant", async (
    HttpContext context,
    Api.Host.RenameTenantRequestBody body,
    Api.Application.IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var tenantIdClaim = context.User.FindFirstValue("tenant_id");

    if (tenantIdClaim is null || !Guid.TryParse(tenantIdClaim, out var tenantId))
    {
        return Results.Forbid();
    }

    var result = await mediator.Send(
        new Api.Application.Tenants.RenameTenantCommand(tenantId, body.NewName), cancellationToken);
    return Api.Host.ResultHttpMapper.ToHttpResult(result);
})
.RequireAuthorization();

app.Run();

public partial class Program;
