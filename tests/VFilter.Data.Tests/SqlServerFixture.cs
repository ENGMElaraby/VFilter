using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using VFilter.Data;

namespace VFilter.Data.Tests;

/// <summary>
/// A fresh database per test class on the SQL Server in VFILTER_TEST_CONNECTION (server-level, no Database=),
/// created from contract-schema.sql and dropped afterwards. Tests skip when the variable is not set.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "VFILTER_TEST_CONNECTION";

    private readonly string? _server = Environment.GetEnvironmentVariable(EnvironmentVariable);
    private readonly string _database = "vfilter_test_" + Guid.NewGuid().ToString("N")[..12];

    public bool Available => !string.IsNullOrWhiteSpace(_server);

    public SqlConnectionFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (!Available)
        {
            return;
        }

        await using (var master = new SqlConnection(_server))
        {
            await master.OpenAsync();
            await master.ExecuteAsync($"CREATE DATABASE [{_database}]");
        }

        var builder = new SqlConnectionStringBuilder(_server) { InitialCatalog = _database };
        Factory = new SqlConnectionFactory(Options.Create(new DatabaseOptions { ConnectionString = builder.ConnectionString }));

        await using var connection = await Factory.OpenAsync(CancellationToken.None);
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "contract-schema.sql")));
    }

    public async Task DisposeAsync()
    {
        if (!Available)
        {
            return;
        }

        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(_server);
        await master.OpenAsync();
        await master.ExecuteAsync($"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]");
    }

    public async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = await Factory.OpenAsync(CancellationToken.None);
        await connection.ExecuteAsync(sql, parameters);
    }

    public async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await Factory.OpenAsync(CancellationToken.None);

        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await Factory.OpenAsync(CancellationToken.None);

        return [.. await connection.QueryAsync<T>(sql, parameters)];
    }
}

/// <summary>Skips the test when no SQL Server is configured.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerFixture.EnvironmentVariable)))
        {
            Skip = $"Set {SqlServerFixture.EnvironmentVariable} to run SQL Server integration tests.";
        }
    }
}
