using Application.Abstractions;
using Application.Results;
using Infrastructure.Persistence.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Members;

public sealed record TenantMember(string ExternalUserId, string Status, string[] RoleCodes);

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
            .Include(user => user.Roles)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var members = users
            .Select(user => new TenantMember(
                user.ExternalUserId.Value,
                user.Status.ToString(),
                user.Roles
                    .Select(role => role.Code)
                    .OrderBy(code => code)
                    .ToArray()))
            .OrderBy(member => member.ExternalUserId)
            .ToList();

        return Result<IReadOnlyList<TenantMember>>.Success(members);
    }
}
