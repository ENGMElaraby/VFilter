using Dapper;
using VFilter.Core.Commands;

namespace VFilter.Data;

/// <summary>filter_commands: claim and finish with conditional updates (contract 0.3.0 claim/cancel rule).</summary>
public sealed class CommandRepository(SqlConnectionFactory db) : ICommandRepository
{
    public async Task<IReadOnlyList<long>> GetPendingIdsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        const string sql = "SELECT TOP (@limit) id FROM filter_commands WHERE status = 'pending' ORDER BY created_at, id";

        return [.. await connection.QueryAsync<long>(new CommandDefinition(sql, new { limit }, commandTimeout: db.CommandTimeout, cancellationToken: cancellationToken)).ConfigureAwait(false)];
    }

    public async Task<ClaimedCommand?> TryClaimAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        const string sql = """
            UPDATE filter_commands SET status = 'running'
            OUTPUT inserted.id AS Id, inserted.command AS Command, inserted.target AS Target, inserted.payload AS PayloadJson,
                   inserted.created_at AS CreatedAt, SYSUTCDATETIME() AS DatabaseNow
            WHERE id = @id AND status = 'pending'
            """;

        return await connection.QuerySingleOrDefaultAsync<ClaimedCommand>(
            new CommandDefinition(sql, new { id }, commandTimeout: db.CommandTimeout, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task FinishAsync(long id, CommandOutcome outcome, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        const string sql = """
            UPDATE filter_commands SET status = @status, result = @result, processed_at = SYSUTCDATETIME()
            WHERE id = @id AND status = 'running'
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, new { id, status = outcome.Status, result = outcome.Result }, commandTimeout: db.CommandTimeout, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
