namespace Api.Application.Tenants;

[RequiresPermission("tenant.rename")]
public sealed record RenameTenantCommand(Guid TenantId, string NewName) : IRequest<Result>, ITenantScopedRequest;
