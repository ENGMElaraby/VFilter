using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VFilter.Core.Config;

namespace VFilter.Core.Modules;

/// <summary>
/// Starts and stops modules in order and tracks their state for the heartbeat (spec 001 US1/US2).
/// Start: Download, Agent1..N, Gateway last. Stop: the reverse.
/// </summary>
public sealed partial class ModuleController(IModuleRunner runner, ConfigStore config, TimeProvider time, ILogger<ModuleController> logger) : IDisposable
{
    public const string AllTarget = "all";

    private readonly ConcurrentDictionary<string, Tracked> _tracked = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed record Tracked(ModuleConfig Config, ModuleState State, long? StartedTimestamp, string? LastError);

    /// <summary>Status of every configured module (disabled ones report Stopped).</summary>
    public IReadOnlyList<ModuleStatus> Statuses()
    {
        return [.. config.Modules.Modules.Select(module =>
        {
            var tracked = _tracked.GetValueOrDefault(module.Name);
            var state = tracked?.State ?? ModuleState.Stopped;
            var counts = state == ModuleState.Running ? runner.GetCounts(module.Name) : default;
            var uptime = state == ModuleState.Running && tracked?.StartedTimestamp is long started ? time.GetElapsedTime(started) : (TimeSpan?)null;

            return new ModuleStatus(module.Name, module.Type, state, uptime, tracked?.LastError, counts.Connections, counts.OnlinePlayers);
        })];
    }

    /// <summary>Whether a running module was started with addresses that have since changed.</summary>
    public bool NeedsRestart(string name) =>
        _tracked.TryGetValue(name, out var tracked)
        && tracked.State == ModuleState.Running
        && config.Modules.Find(name.ToLowerInvariant()) is { } current
        && !current.SameAddresses(tracked.Config);

    public Task<string> StartAsync(string target, CancellationToken cancellationToken) =>
        RunAsync(target, ascending: true, StartOneAsync, cancellationToken);

    public Task<string> StopAsync(string target, CancellationToken cancellationToken) =>
        RunAsync(target, ascending: false, StopOneAsync, cancellationToken);

    public async Task<string> RestartAsync(string target, CancellationToken cancellationToken)
    {
        var stopped = await StopAsync(target, cancellationToken).ConfigureAwait(false);
        var started = await StartAsync(target, cancellationToken).ConfigureAwait(false);

        return $"stop: {stopped}; start: {started}";
    }

    /// <summary>Starts every enabled module at service start.</summary>
    public Task<string> StartAllAsync(CancellationToken cancellationToken) => StartAsync(AllTarget, cancellationToken);

    /// <summary>Stops every running module, including ones removed from the configuration.</summary>
    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var tracked in _tracked.Values.Where(t => t.State == ModuleState.Running).OrderByDescending(t => t.Config.StartOrder))
            {
                await StopOneAsync(tracked.Config, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> RunAsync(string target, bool ascending, Func<ModuleConfig, CancellationToken, Task<string>> action, CancellationToken ct)
    {
        var modules = Resolve(target);
        var ordered = ascending ? modules.OrderBy(m => m.StartOrder) : modules.OrderByDescending(m => m.StartOrder);
        var results = new List<string>();

        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            foreach (var module in ordered)
            {
                results.Add($"{module.Name} {await action(module, ct).ConfigureAwait(false)}");
            }
        }
        finally
        {
            _gate.Release();
        }

        return string.Join(", ", results);
    }

    private List<ModuleConfig> Resolve(string target)
    {
        if (string.Equals(target, AllTarget, StringComparison.OrdinalIgnoreCase))
        {
            var enabled = config.Modules.Modules.Where(m => m.Enabled).ToList();

            return enabled.Count > 0 ? enabled : throw new CommandFailedException("no enabled modules");
        }

        var module = config.Modules.Find(target) ?? throw new CommandFailedException($"unknown target {target}");

        return module.Enabled ? [module] : throw new CommandFailedException($"{module.Name} is disabled");
    }

    private async Task<string> StartOneAsync(ModuleConfig module, CancellationToken ct)
    {
        if (_tracked.TryGetValue(module.Name, out var current) && current.State == ModuleState.Running)
        {
            return "already running";
        }

        _tracked[module.Name] = new Tracked(module, ModuleState.Starting, null, null);

        try
        {
            await runner.StartAsync(module, ct).ConfigureAwait(false);
            _tracked[module.Name] = new Tracked(module, ModuleState.Running, time.GetTimestamp(), null);
            LogStarted(module.Name);

            return "started";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _tracked[module.Name] = new Tracked(module, ModuleState.Error, null, exception.Message);
            LogStartFailed(module.Name, exception);

            return $"failed to start: {exception.Message}";
        }
    }

    private async Task<string> StopOneAsync(ModuleConfig module, CancellationToken ct)
    {
        if (!_tracked.TryGetValue(module.Name, out var current) || current.State is ModuleState.Stopped)
        {
            return "already stopped";
        }

        try
        {
            await runner.StopAsync(current.Config, ct).ConfigureAwait(false);
            _tracked[module.Name] = current with { State = ModuleState.Stopped, StartedTimestamp = null, LastError = null };
            LogStopped(module.Name);

            return "stopped";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _tracked[module.Name] = current with { State = ModuleState.Error, LastError = exception.Message };

            return $"failed to stop: {exception.Message}";
        }
    }

    public void Dispose() => _gate.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Module {Module} started")]
    private partial void LogStarted(string module);

    [LoggerMessage(Level = LogLevel.Information, Message = "Module {Module} stopped")]
    private partial void LogStopped(string module);

    [LoggerMessage(Level = LogLevel.Error, Message = "Module {Module} failed to start")]
    private partial void LogStartFailed(string module, Exception exception);
}
