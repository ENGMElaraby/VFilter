using System.Data;
using Microsoft.Data.SqlClient;
using VFilter.Core.Logging;

namespace VFilter.Data;

/// <summary>Bulk-inserts log batches into filter_logs, cutting values to the contract's column sizes.</summary>
public sealed class LogRepository(SqlConnectionFactory db) : ILogRepository
{
    private static readonly (string Name, int Max)[] TextColumns =
    [
        ("level", 8), ("module", 32), ("category", 32), ("ip", 45), ("hwid", 64), ("username", 32),
        ("charname", 64), ("opcode", 8), ("message", 1024),
    ];

    public async Task WriteAsync(IReadOnlyList<FilterLogEvent> events, CancellationToken cancellationToken)
    {
        using var table = new DataTable();
        table.Locale = System.Globalization.CultureInfo.InvariantCulture;
        table.Columns.Add("ts", typeof(DateTime));

        foreach (var (name, _) in TextColumns)
        {
            table.Columns.Add(name, typeof(string));
        }

        table.Columns.Add("data", typeof(string));

        foreach (var e in events)
        {
            table.Rows.Add(
                e.Ts,
                Cut(FilterLogEvent.LevelKey(e.Level), 8),
                Cut(e.Module, 32),
                Cut(e.Category, 32),
                Cut(e.Ip, 45),
                Cut(e.Hwid, 64),
                Cut(e.Username, 32),
                Cut(e.Charname, 64),
                Cut(e.Opcode, 8),
                Cut(e.Message, 1024),
                (object?)e.DataJson ?? DBNull.Value);
        }

        await using var connection = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var bulk = new SqlBulkCopy(connection) { DestinationTableName = "filter_logs", BulkCopyTimeout = db.CommandTimeout };

        foreach (DataColumn column in table.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(table, cancellationToken).ConfigureAwait(false);
    }

    private static object Cut(string? value, int max) =>
        value is null ? DBNull.Value : value.Length <= max ? value : value[..max];
}
