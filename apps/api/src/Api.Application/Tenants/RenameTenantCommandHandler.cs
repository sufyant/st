namespace Api.Application.Tenants;

public sealed class RenameTenantCommandHandler(ITenantRepository tenantRepository)
    : IRequestHandler<RenameTenantCommand, Result>
{
    public async Task<Result> Handle(RenameTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await tenantRepository.FindByIdAsync(request.TenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure(new Error("Tenant.NotFound", $"Tenant '{request.TenantId}' was not found."));
        }

        tenant.Rename(request.NewName);
        return Result.Success();
    }
}
