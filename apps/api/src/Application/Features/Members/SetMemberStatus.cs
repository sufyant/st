using Application.Abstractions;
using Application.Results;
using Infrastructure.Persistence.Tenants;

namespace Application.Features.Members;

[RequiresPermission(TenantPermissions.MembersManage)]
public sealed record DisableMemberCommand(string ExternalUserId) : ICommand<Unit>;

[RequiresPermission(TenantPermissions.MembersManage)]
public sealed record EnableMemberCommand(string ExternalUserId) : ICommand<Unit>;

public sealed class DisableMemberHandler(TenantDbContext tenantDbContext)
    : IRequestHandler<DisableMemberCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        DisableMemberCommand request,
        CancellationToken cancellationToken)
    {
        var user = await TenantUsers.FindAsync(tenantDbContext, request.ExternalUserId, cancellationToken);

        if (user is null)
        {
            return MemberErrors.Missing;
        }

        user.Disable();

        return Result.Success();
    }
}

public sealed class EnableMemberHandler(TenantDbContext tenantDbContext)
    : IRequestHandler<EnableMemberCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        EnableMemberCommand request,
        CancellationToken cancellationToken)
    {
        var user = await TenantUsers.FindAsync(tenantDbContext, request.ExternalUserId, cancellationToken);

        if (user is null)
        {
            return MemberErrors.Missing;
        }

        user.Enable();

        return Result.Success();
    }
}
