namespace Tenancy;

/// <summary>The roles the bootstrap script creates; migrations grant privileges to them by name.</summary>
public static class DatabaseRoles
{
    public const string Owner = "api_owner";

    public const string Application = "api_application";
}
