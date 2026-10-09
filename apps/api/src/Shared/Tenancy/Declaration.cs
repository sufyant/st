using System.Data.Common;

namespace Tenancy;

// Declares the tenant or the user for the rest of a transaction (R4, R11). A transaction declares one of them, never both: the
// tenant_isolation and own_memberships policies combine with OR, so a transaction with both would read the user's memberships in
// other tenants too. The database checks it in the statement that declares, which sets nothing and returns no row when the other
// one is set.
internal static class Declaration
{
    private const string UserSetting = "app.user_id";

    private const string Tenant =
        $"SELECT set_config('{TenantColumn.Setting}', @value, true) WHERE NULLIF(current_setting('{UserSetting}', true), '') IS NULL";

    private const string User =
        $"SELECT set_config('{UserSetting}', @value, true) WHERE NULLIF(current_setting('{TenantColumn.Setting}', true), '') IS NULL";

    public static Task DeclareTenantAsync(DbConnection connection, DbTransaction transaction, Guid tenantId, CancellationToken cancellationToken) =>
        DeclareAsync(connection, transaction, Tenant, tenantId, cancellationToken);

    public static Task DeclareUserAsync(DbConnection connection, DbTransaction transaction, Guid userId, CancellationToken cancellationToken) =>
        DeclareAsync(connection, transaction, User, userId, cancellationToken);

    private static async Task DeclareAsync(
        DbConnection connection,
        DbTransaction transaction,
        string declaration,
        Guid value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = declaration;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "value";
        parameter.Value = value.ToString();
        command.Parameters.Add(parameter);

        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new InvalidOperationException("A transaction declares a tenant or a user, never both (R4).");
        }
    }
}
