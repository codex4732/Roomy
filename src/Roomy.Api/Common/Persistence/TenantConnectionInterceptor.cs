using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Roomy.Api.Common.Tenancy;

namespace Roomy.Api.Common.Tenancy;

/// <summary>
/// Tenant isolation layer 2 (technical design §4): sets the `app.tenant_id` GUC every time
/// a pooled connection is opened, so PostgreSQL row-level-security policies see the current
/// tenant. When no tenant is bound the GUC is set to the empty string, which makes
/// `current_setting('app.tenant_id')::uuid` fail — tenant tables fail closed.
/// </summary>
public sealed class TenantConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => SetTenantGuc(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = BuildCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void SetTenantGuc(DbConnection connection)
    {
        using var command = BuildCommand(connection);
        command.ExecuteNonQuery();
    }

    private DbCommand BuildCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', $1, false)";
        var parameter = command.CreateParameter();
        parameter.Value = tenantContext.HasTenant ? tenantContext.TenantId.ToString() : "";
        command.Parameters.Add(parameter);
        return command;
    }
}
