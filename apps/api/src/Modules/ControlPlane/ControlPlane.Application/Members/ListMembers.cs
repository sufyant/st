using ControlPlane.Application.Ports;
using SharedKernel;

namespace ControlPlane.Application.Members;

/// <summary>The members of the active tenant, a page at a time: by role (Owner, Admin, Member), then by user id.</summary>
public sealed record ListMembers(PageRequest Paging);

/// <summary>A member: the catalog user and the built-in role they hold in the tenant.</summary>
public sealed record MemberSummary(Guid UserId, string Role);

public static class ListMembersHandler
{
    public static async Task<Result<ListPage<MemberSummary>>> HandleAsync(
        ListMembers query,
        ITenantCatalog catalog,
        CancellationToken cancellationToken) =>
        (await catalog.ListMembersAsync(query.Paging, cancellationToken))
            .Map(member => new MemberSummary(member.UserId, member.Role.ToString()));
}
