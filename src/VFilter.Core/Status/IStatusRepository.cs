namespace VFilter.Core.Status;

/// <summary>One filter_status row as the filter reports it. Times are set with the database clock.</summary>
public sealed record StatusRow(
    string Module,
    string State,
    int OnlineCount,
    int Connections,
    string? GatewayMode,
    string Version,
    long? UptimeMilliseconds,
    string? LastError);

public interface IStatusRepository
{
    /// <summary>Upserts every row with heartbeat_at = database now; returns the database UTC now.</summary>
    Task<DateTime> WriteAsync(IReadOnlyList<StatusRow> rows, CancellationToken cancellationToken);
}
