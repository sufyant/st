using System.Security.Claims;
using System.Text.Json;
using Domain.Tenants;
using Infrastructure.Messaging;
using Infrastructure.Persistence.ControlPlane;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane;

public sealed record CreateTenantRequest(string Alias);

public sealed record TenantDetail(
    Guid Id,
    string Alias,
    string Status,
    string? ProvisioningStep,
    string? ProvisioningError,
    DateTimeOffset CreatedAt);

public static class TenantEndpoints
{
    private const string GetTenantRouteName = "GetTenant";

    public static void MapTenants(this RouteGroupBuilder group)
    {
        group.MapPost("/tenants", CreateAsync);
        group.MapGet("/tenants/{id:guid}", GetAsync).WithName(GetTenantRouteName);
        group.MapPost("/tenants/{id:guid}/retry-provisioning", RetryAsync);
    }

    private static async Task<IResult> CreateAsync(
        CreateTenantRequest request,
        ControlPlaneDbContext dbContext,
        ClaimsPrincipal user,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        TenantAlias alias;

        try
        {
            alias = TenantAlias.Create(request.Alias);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        if (await dbContext.Tenants.AnyAsync(tenant => tenant.Alias == alias, cancellationToken))
        {
            return Results.Conflict(new { error = $"Alias '{request.Alias}' is already taken." });
        }

        var now = timeProvider.GetUtcNow();
        var tenant = Tenant.Create(Guid.CreateVersion7(), alias, now);
        dbContext.Tenants.Add(tenant);
        Enqueue(dbContext, tenant.Id, user, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.AcceptedAtRoute(GetTenantRouteName, new { id = tenant.Id }, ToDetail(tenant));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ControlPlaneDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return tenant is null ? Results.NotFound() : Results.Ok(ToDetail(tenant));
    }

    private static async Task<IResult> RetryAsync(
        Guid id,
        ControlPlaneDbContext dbContext,
        ClaimsPrincipal user,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Tenants
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (tenant is null)
        {
            return Results.NotFound();
        }

        if (tenant.Status is not TenantStatus.Provisioning)
        {
            return Results.Conflict(new { error = $"Tenant is {tenant.Status}, not Provisioning." });
        }

        Enqueue(dbContext, tenant.Id, user, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.AcceptedAtRoute(GetTenantRouteName, new { id = tenant.Id }, ToDetail(tenant));
    }

    private static void Enqueue(
        ControlPlaneDbContext dbContext,
        Guid tenantId,
        ClaimsPrincipal user,
        DateTimeOffset now) =>
        dbContext.OutboxMessages.Add(OutboxMessage.Create(
            Guid.CreateVersion7(),
            TenantProvisioningRequested.MessageType,
            JsonSerializer.Serialize(new TenantProvisioningRequested(
                tenantId,
                user.FindFirstValue("sub")!)),
            now));

    private static TenantDetail ToDetail(Tenant tenant) => new(
        tenant.Id,
        tenant.Alias.Value,
        tenant.Status.ToString(),
        tenant.ProvisioningStep?.ToString(),
        tenant.ProvisioningError,
        tenant.CreatedAt);
}
