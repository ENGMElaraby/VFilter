using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VFilter.Core.Config;
using VFilter.Core.Logging;
using VFilter.Core.Modules;
using VFilter.Core.Status;

namespace VFilter.Core;

/// <summary>
/// Start: load configuration (retrying with backoff, never starting without it), set the gateway mode,
/// start enabled modules. Stop: stop modules in reverse order and write a final heartbeat (spec 001 US1).
/// </summary>
public sealed partial class FilterLifecycle(
    ConfigStore config,
    ModuleController modules,
    GatewayModeState gatewayMode,
    HeartbeatService heartbeat,
    FilterLog filterLog,
    TimeProvider time,
    ILogger<FilterLifecycle> logger) : IHostedLifecycleService, IDisposable
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _startup = new();
    private Task _startTask = Task.CompletedTask;

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Starts in the background so the service registers with Windows even while the DB is down.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _startTask = Task.Run(() => StartFilterAsync(_startup.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        await _startup.CancelAsync().ConfigureAwait(false);

        try
        {
            await _startTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped before configuration loaded.
        }

        await modules.StopAllAsync(cancellationToken).ConfigureAwait(false);
        await heartbeat.BeatAsync(cancellationToken).ConfigureAwait(false);
        filterLog.Write(FilterLogLevel.Info, "connection", $"VFilter {HeartbeatService.FilterVersion} stopped");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _startup.Dispose();

    /// <summary>Loads configuration, then starts modules. Exposed for tests.</summary>
    public async Task StartFilterAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);

        while (!config.IsLoaded)
        {
            try
            {
                await config.RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogWaitingForDatabase(delay, exception);
                await Task.Delay(delay, time, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks));
            }
        }

        gatewayMode.Initialize(config.Settings);
        var result = await modules.StartAllAsync(cancellationToken).ConfigureAwait(false);
        await heartbeat.BeatAsync(cancellationToken).ConfigureAwait(false);

        LogStarted(HeartbeatService.FilterVersion, gatewayMode.Mode, result);
        filterLog.Write(FilterLogLevel.Info, "connection", $"VFilter {HeartbeatService.FilterVersion} started: {result}");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "VFILTER database not reachable; retrying in {Delay}")]
    private partial void LogWaitingForDatabase(TimeSpan delay, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "VFilter {Version} started in {Mode} mode: {Result}")]
    private partial void LogStarted(string version, GatewayMode mode, string result);
}
