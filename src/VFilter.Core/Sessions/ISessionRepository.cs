namespace VFilter.Core.Sessions;

/// <summary>filter_sessions writes. Times are set with the database clock.</summary>
public interface ISessionRepository
{
    Task ClearAsync(CancellationToken cancellationToken);

    Task<long> InsertAsync(LiveSession session, CancellationToken cancellationToken);

    Task UpdateIdentityAsync(LiveSession session, CancellationToken cancellationToken);

    Task TouchAsync(long databaseId, CancellationToken cancellationToken);

    Task DeleteAsync(long databaseId, CancellationToken cancellationToken);
}
