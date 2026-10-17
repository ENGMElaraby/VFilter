using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VFilter.Core.Config;
using VFilter.Core.Logging;
using VFilter.Core.Modules;
using VFilter.Core.Sessions;

namespace VFilter.Core.Commands;

/// <summary>
/// Carries out website commands from filter_commands, one at a time in queue order (spec 001 US2).
/// A command runs only after this instance's claim succeeded, so a cancel and a claim never both win.
/// </summary>
public sealed partial class CommandProcessor(
    ICommandRepository repository,
    ModuleController modules,
    GatewayModeState gatewayMode,
    ConfigStore config,
    SessionRegistry sessions,
    FilterLog filterLog,
    TimeProvider time,
    ILogger<CommandProcessor> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);

    private const int BatchLimit = 20;

    private const int FinishAttempts = 3;

    private bool _failing;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>One poll: claims and runs every pending command. Never throws.</summary>
    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<long> ids;

        try
        {
            ids = await repository.GetPendingIdsAsync(BatchLimit, cancellationToken).ConfigureAwait(false);
            _failing = false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!_failing)
            {
                _failing = true;
                LogQueueUnavailable(exception);
            }

            return;
        }

        foreach (var id in ids)
        {
            await ProcessOneAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessOneAsync(long id, CancellationToken ct)
    {
        ClaimedCommand? command;

        try
        {
            command = await repository.TryClaimAsync(id, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogClaimFailed(id, exception);

            return;
        }

        if (command is null)
        {
            return;
        }

        var outcome = command.DatabaseNow - command.CreatedAt > Expiry
            ? CommandOutcome.Failed("expired")
            : await ExecuteAsync(command, ct).ConfigureAwait(false);

        await FinishAsync(command, outcome, ct).ConfigureAwait(false);

        filterLog.Write(
            outcome.Succeeded ? FilterLogLevel.Info : FilterLogLevel.Warn,
            "command",
            $"{command.Command} {command.Target}: {outcome.Status} ({outcome.Result})");
    }

    /// <summary>Runs one claimed command. Exceptions become a failed outcome; the processor keeps going.</summary>
    public async Task<CommandOutcome> ExecuteAsync(ClaimedCommand command, CancellationToken ct)
    {
        try
        {
            return command.Command switch
            {
                "start" => CommandOutcome.Done(await modules.StartAsync(command.Target, ct).ConfigureAwait(false)),
                "stop" => CommandOutcome.Done(await modules.StopAsync(command.Target, ct).ConfigureAwait(false)),
                "restart" => CommandOutcome.Done(await modules.RestartAsync(command.Target, ct).ConfigureAwait(false)),
                "set_gateway_mode" => SetGatewayMode(command),
                "reload" => await ReloadAsync(ct).ConfigureAwait(false),
                "kick" => Kick(command),
                "ban" => Ban(command),
                "notice" => CommandOutcome.Failed("not supported yet"),
                _ => CommandOutcome.Failed($"unknown command {command.Command}"),
            };
        }
        catch (CommandFailedException exception)
        {
            return CommandOutcome.Failed(exception.Message);
        }
        catch (JsonException)
        {
            return CommandOutcome.Failed("bad payload");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCommandError(command.Id, exception);

            return CommandOutcome.Failed($"error: {exception.Message}");
        }
    }

    private CommandOutcome SetGatewayMode(ClaimedCommand command)
    {
        if (!string.Equals(command.Target, "gateway", StringComparison.OrdinalIgnoreCase))
        {
            return CommandOutcome.Failed("gateway mode applies to the gateway only");
        }

        if (!GatewayModeState.TryParse(PayloadString(command, "mode"), out var mode))
        {
            return CommandOutcome.Failed("bad payload: mode must be gm, inspection or public");
        }

        var previous = gatewayMode.Mode;
        gatewayMode.Set(mode);

        return CommandOutcome.Done($"gateway mode {GatewayModeState.Key(previous)} → {GatewayModeState.Key(mode)}");
    }

    private async Task<CommandOutcome> ReloadAsync(CancellationToken ct)
    {
        var areas = await config.RefreshAsync(force: true, ct).ConfigureAwait(false);

        return CommandOutcome.Done("reloaded: " + string.Join(", ", areas.Select(a => a.Key())));
    }

    private CommandOutcome Kick(ClaimedCommand command)
    {
        var reason = PayloadString(command, "reason") ?? "kicked by an admin";
        var count = sessions.Kick(command.Target, reason);

        return count > 0 ? CommandOutcome.Done($"disconnected {count} session(s)") : CommandOutcome.Failed("player not online");
    }

    /// <summary>
    /// Disconnects the player now. The game-account ban itself needs the proxy/GameServer feature, so the
    /// command reports failed until then: the website must not show a ban as done when it is not.
    /// </summary>
    private CommandOutcome Ban(ClaimedCommand command)
    {
        var reason = PayloadString(command, "reason") ?? "banned";
        var count = sessions.Kick(command.Target, reason);

        return CommandOutcome.Failed($"account ban not supported yet; disconnected {count} session(s)");
    }

    private static string? PayloadString(ClaimedCommand command, string property)
    {
        if (string.IsNullOrWhiteSpace(command.PayloadJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(command.PayloadJson);

        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private async Task FinishAsync(ClaimedCommand command, CommandOutcome outcome, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= FinishAttempts; attempt++)
        {
            try
            {
                await repository.FinishAsync(command.Id, outcome, ct).ConfigureAwait(false);

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException && attempt < FinishAttempts)
            {
                LogFinishRetry(command.Id, attempt, exception);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), time, ct).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Command queue unavailable")]
    private partial void LogQueueUnavailable(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not claim command {Id}")]
    private partial void LogClaimFailed(long id, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Command {Id} failed unexpectedly")]
    private partial void LogCommandError(long id, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record the outcome of command {Id} (attempt {Attempt})")]
    private partial void LogFinishRetry(long id, int attempt, Exception exception);
}
