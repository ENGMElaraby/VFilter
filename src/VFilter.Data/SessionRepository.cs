using Dapper;
using VFilter.Core.Sessions;

namespace VFilter.Data;

/// <summary>filter_sessions. The filter is the only writer; times use the database clock.</summary>
public sealed class SessionRepository(SqlConnectionFactory db) : ISessionRepository
{
    public Task ClearAsync(CancellationToken cancellationToken) => ExecuteAsync("DELETE FROM filter_sessions", null, cancellationToken);

    public async Task<long> InsertAsync(LiveSession session, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        const string sql = """
            INSERT INTO filter_sessions (module, ip, hwid, username, charname, connected_at, last_packet_at)
            OUTPUT inserted.id
            VALUES (@Module, @Ip, @Hwid, @Username, @Charname, SYSUTCDATETIME(), SYSUTCDATETIME())
            """;

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { session.Module, session.Ip, session.Hwid, session.Username, session.Charname }, commandTimeout: db.CommandTimeout, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public Task UpdateIdentityAsync(LiveSession session, CancellationToken cancellationToken) =>
        ExecuteAsync("UPDATE filter_sessions SET hwid = @Hwid, username = @Username, charname = @Charname WHERE id = @DatabaseId", new { session.Hwid, session.Username, session.Charname, session.DatabaseId }, cancellationToken);

    public Task TouchAsync(long databaseId, CancellationToken cancellationToken) =>
        ExecuteAsync("UPDATE filter_sessions SET last_packet_at = SYSUTCDATETIME() WHERE id = @databaseId", new { databaseId }, cancellationToken);

    public Task DeleteAsync(long databaseId, CancellationToken cancellationToken) =>
        ExecuteAsync("DELETE FROM filter_sessions WHERE id = @databaseId", new { databaseId }, cancellationToken);

    private async Task ExecuteAsync(string sql, object? parameters, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, commandTimeout: db.CommandTimeout, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
