using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VFilter.Core.Sessions;

/// <summary>Applies session changes to filter_sessions in the background (spec 001, FR-013).</summary>
public sealed partial class SessionMirror(SessionRegistry registry, ISessionRepository repository, ILogger<SessionMirror> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ClearLeftoversAsync(stoppingToken).ConfigureAwait(false);

        await foreach (var change in registry.Changes.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await ApplyAsync(change, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Rows from a previous run belong to connections that no longer exist.</summary>
    public async Task ClearLeftoversAsync(CancellationToken cancellationToken)
    {
        try
        {
            await repository.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed("clear", exception);
        }
    }

    public async Task ApplyAsync(SessionChange change, CancellationToken cancellationToken)
    {
        var session = change.Session;

        try
        {
            switch (change.Kind)
            {
                case SessionChangeKind.Opened:
                    session.DatabaseId = await repository.InsertAsync(session, cancellationToken).ConfigureAwait(false);
                    break;
                case SessionChangeKind.Identified when session.DatabaseId is long id:
                    await repository.UpdateIdentityAsync(session, cancellationToken).ConfigureAwait(false);
                    break;
                case SessionChangeKind.Touched when session.DatabaseId is long id:
                    await repository.TouchAsync(id, cancellationToken).ConfigureAwait(false);
                    break;
                case SessionChangeKind.Closed when session.DatabaseId is long id:
                    await repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(change.Kind.ToString(), exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Session mirror {Operation} failed")]
    private partial void LogFailed(string operation, Exception exception);
}
