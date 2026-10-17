using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VFilter.Core.Modules;

namespace VFilter.Core.Status;

/// <summary>Writes one filter_status row per configured module every 5 s (spec 001, FR-010/FR-012).</summary>
public sealed partial class HeartbeatService(
    ModuleController modules,
    GatewayModeState gatewayMode,
    IStatusRepository repository,
    DatabaseClockOffset clock,
    TimeProvider time,
    ILogger<HeartbeatService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    public static string FilterVersion { get; } =
        typeof(HeartbeatService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private bool _failing;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);

        do
        {
            await BeatAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>One heartbeat. Never throws: a failed write is retried on the next tick.</summary>
    public async Task BeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            var databaseNow = await repository.WriteAsync(Rows(), cancellationToken).ConfigureAwait(false);
            clock.Observe(databaseNow);
            _failing = false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!_failing)
            {
                _failing = true;
                LogFailed(exception);
            }
        }
    }

    public IReadOnlyList<StatusRow> Rows() =>
        [.. modules.Statuses().Select(status => new StatusRow(
            status.Name,
            status.State.ToString().ToLowerInvariant(),
            status.OnlinePlayers,
            status.Connections,
            status.Type == ModuleType.Gateway ? GatewayModeState.Key(gatewayMode.Mode) : null,
            FilterVersion,
            status.Uptime is { } uptime ? (long)uptime.TotalMilliseconds : null,
            status.LastError))];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Heartbeat could not be written")]
    private partial void LogFailed(Exception exception);
}
