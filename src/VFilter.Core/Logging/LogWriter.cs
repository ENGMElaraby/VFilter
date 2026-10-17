using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VFilter.Core.Logging;

/// <summary>Flushes the log buffer to filter_logs every second or every 500 events (spec 001, FR-014).</summary>
public sealed partial class LogWriter(FilterLog log, ILogRepository repository, ILogger<LogWriter> logger) : BackgroundService
{
    public const int BatchSize = 500;

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _signal = new(0, 1);

    public override void Dispose()
    {
        _signal.Dispose();
        base.Dispose();
    }
    private bool _failing;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.BatchReady += Signal;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var delay = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                delay.CancelAfter(Interval);

                try
                {
                    await _signal.WaitAsync(delay.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Interval elapsed.
                }

                await FlushAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            log.BatchReady -= Signal;
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Writes everything waiting, in batches. Never throws; on failure the batch is kept.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        while (log.Count > 0)
        {
            var batch = log.Take(BatchSize);

            try
            {
                await repository.WriteAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                log.Return(batch);

                if (!_failing)
                {
                    _failing = true;
                    LogFailed(exception);
                }

                return;
            }

            if (_failing)
            {
                _failing = false;
            }

            var dropped = log.TakeDroppedCount();

            if (dropped > 0)
            {
                log.Write(FilterLogLevel.Warn, "connection", $"{dropped} log events were dropped while the database was unavailable");
            }
        }
    }

    private void Signal()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Already signalled.
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Log events could not be written; keeping them in memory")]
    private partial void LogFailed(Exception exception);
}
