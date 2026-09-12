using Application.Abstractions;
using Application.Results;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Members;

public sealed record TenantMember(string ExternalUserId, string Email, string Status, string RoleCode);

[RequiresPermission(TenantPermissions.MembersRead)]
public sealed record ListMembersQuery : IQuery<IReadOnlyList<TenantMember>>;

public sealed class ListMembersHandler(TenantDbContext tenantDbContext)
    : IRequestHandler<ListMembersQuery, IReadOnlyList<TenantMember>>
{
    public async Task<Result<IReadOnlyList<TenantMember>>> HandleAsync(
        ListMembersQuery request,
        CancellationToken cancellationToken)
    {
        var users = await tenantDbContext.Users
            .Include(user => user.Role)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var members = users
            .Select(user => new TenantMember(
                user.ExternalUserId.Value,
                user.Email.Value,
                user.Status.ToString(),
                user.Role.Code))
            .OrderBy(member => member.ExternalUserId)
            .ToList();

        return members;
    }
}
