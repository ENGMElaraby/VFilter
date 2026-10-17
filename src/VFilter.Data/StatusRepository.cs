using Dapper;
using VFilter.Core.Status;

namespace VFilter.Data;

/// <summary>
/// filter_status upserts. heartbeat_at and started_at come from the database clock: started_at is
/// "database now minus the module's uptime", so the website's uptime is right even with clock skew.
/// </summary>
public sealed class StatusRepository(SqlConnectionFactory db) : IStatusRepository
{
    private const string MergeSql = """
        MERGE filter_status WITH (HOLDLOCK) AS t
        USING (SELECT @Module AS module) AS s ON t.module = s.module
        WHEN MATCHED THEN UPDATE SET
            state = @State, online_count = @OnlineCount, connections = @Connections, gateway_mode = @GatewayMode,
            version = @Version, last_error = @LastError, heartbeat_at = SYSUTCDATETIME(),
            started_at = CASE WHEN @UptimeSeconds IS NULL THEN NULL ELSE DATEADD(second, -@UptimeSeconds, SYSUTCDATETIME()) END
        WHEN NOT MATCHED THEN INSERT (module, state, online_count, connections, gateway_mode, version, started_at, heartbeat_at, last_error)
            VALUES (@Module, @State, @OnlineCount, @Connections, @GatewayMode, @Version,
                    CASE WHEN @UptimeSeconds IS NULL THEN NULL ELSE DATEADD(second, -@UptimeSeconds, SYSUTCDATETIME()) END,
                    SYSUTCDATETIME(), @LastError);
        """;

    public async Task<DateTime> WriteAsync(IReadOnlyList<StatusRow> rows, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            await connection.ExecuteAsync(new CommandDefinition(MergeSql, Parameters(row), transaction, db.CommandTimeout, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        var now = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT SYSUTCDATETIME()", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return now;
    }

    private static object Parameters(StatusRow row) => new
    {
        row.Module,
        row.State,
        row.OnlineCount,
        row.Connections,
        row.GatewayMode,
        row.Version,
        row.LastError,
        // DATEADD takes an int: whole seconds cover decades of uptime.
        UptimeSeconds = row.UptimeMilliseconds is long ms ? (int?)(ms / 1000) : null,
    };
}
