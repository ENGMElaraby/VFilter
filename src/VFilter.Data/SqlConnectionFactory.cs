using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace VFilter.Data;

/// <summary>Database settings (section "VFilter"). The connection string is never logged.</summary>
public sealed class DatabaseOptions
{
    public const string Section = "VFilter";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Seconds before a single statement is abandoned.</summary>
    public int CommandTimeoutSeconds { get; set; } = 15;
}

public sealed class SqlConnectionFactory(IOptions<DatabaseOptions> options)
{
    public int CommandTimeout => options.Value.CommandTimeoutSeconds;

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString))
        {
            throw new InvalidOperationException("VFilter:ConnectionString is not configured.");
        }

        var connection = new SqlConnection(options.Value.ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }
}
