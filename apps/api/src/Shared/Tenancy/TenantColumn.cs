namespace Tenancy;

// The tenant column every ITenantEntity gets, and the setting that row level security and the column default read.
internal static class TenantColumn
{
    public const string Property = "TenantId";

    public const string Name = "tenant_id";

    public const string Setting = "app.tenant_id";

    // Once a transaction has set the setting, the session keeps it as an empty string afterwards; NULLIF turns that back into
    // "no tenant" instead of a cast error.
    public const string CurrentTenantSql = $"NULLIF(current_setting('{Setting}', true), '')::uuid";
}
