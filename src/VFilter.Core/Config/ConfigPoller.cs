using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VFilter.Core.Config;

/// <summary>Checks filter_config_version every 2 s and reloads changed areas (spec 001, FR-002/FR-003).</summary>
public sealed partial class ConfigPoller(ConfigStore store, TimeProvider time, ILogger<ConfigPoller> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private bool _failing;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await PollOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>One poll. Failures are logged once per outage and never thrown.</summary>
    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.RefreshAsync(force: false, cancellationToken).ConfigureAwait(false);

            if (_failing)
            {
                _failing = false;
                LogRecovered();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!_failing)
            {
                _failing = true;
                LogUnavailable(exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Configuration database unavailable; keeping the last good configuration")]
    private partial void LogUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Configuration database reachable again")]
    private partial void LogRecovered();
}
