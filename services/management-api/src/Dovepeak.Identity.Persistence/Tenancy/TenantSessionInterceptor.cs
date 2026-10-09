using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Dovepeak.Identity.Persistence.Tenancy;

/// <summary>
/// Copies the current <see cref="TenantScope"/> into PostgreSQL session settings each time a connection opens,
/// so row-level security policies enforce the same boundary as the EF Core query filters.
/// </summary>
/// <remarks>
/// Npgsql resets session state (<c>DISCARD ALL</c>) when a connection returns to the pool, so settings never leak
/// between units of work. EF Core opens a connection per command outside transactions, so a scope change
/// within a request is picked up by the next command.
/// </remarks>
public sealed class TenantSessionInterceptor(TenantScope scope) : DbConnectionInterceptor
{
    private const string Sql = "SELECT set_config('dovepeak.org_id', @org_id, false), set_config('dovepeak.system', @system, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = Sql;

        var organization = command.CreateParameter();
        organization.ParameterName = "org_id";
        organization.Value = scope.OrganizationId?.ToString() ?? string.Empty;
        command.Parameters.Add(organization);

        var system = command.CreateParameter();
        system.ParameterName = "system";
        system.Value = scope.IsSystem ? "on" : "off";
        command.Parameters.Add(system);

        return command;
    }
}
