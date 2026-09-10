namespace Api.Authorization;

public static class TenantClaims
{
    public const string TenantId = "tenant_id";

    public const string Permission = "permission";
}

public static class TenantPermissions
{
    public const string MembersRead = "members.read";

    public const string MembersManage = "members.manage";

    public const string RolesRead = "roles.read";

    public const string RolesManage = "roles.manage";

    public const string InvitationsManage = "invitations.manage";
}
