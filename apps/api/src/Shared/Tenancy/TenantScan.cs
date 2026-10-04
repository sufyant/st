namespace Tenancy;

/// <summary>
/// SQL for a narrow SECURITY DEFINER lookup that lets background work find due items across tenants without a role that
/// bypasses row level security: it returns only <c>(tenant_id, id)</c>, and each item is then processed under its own tenant
/// (0017). Use it from a migration.
/// </summary>
public static class TenantScan
{
    /// <param name="schema">The schema of the table and the function.</param>
    /// <param name="function">The function's name.</param>
    /// <param name="parameters">The function's parameter list, for example <c>due_before timestamptz</c>.</param>
    /// <param name="table">The tenant table to scan; it needs an <c>id</c> column.</param>
    /// <param name="condition">The condition a row must meet, in terms of the table's columns and the parameters.</param>
    public static string CreateFunctionSql(string schema, string function, string parameters, string table, string condition) =>
        $"""
        CREATE FUNCTION "{schema}"."{function}"({parameters})
        RETURNS TABLE (tenant_id uuid, id uuid)
        LANGUAGE sql STABLE SECURITY DEFINER
        SET search_path = pg_catalog, pg_temp
        AS $scan$
            SELECT scanned.{TenantColumn.Name}, scanned.id FROM "{schema}"."{table}" AS scanned WHERE {condition}
        $scan$;
        REVOKE ALL ON FUNCTION "{schema}"."{function}"({parameters}) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION "{schema}"."{function}"({parameters}) TO {DatabaseRoles.Application};
        """;
}
