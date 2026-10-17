namespace VFilter.Core.Logging;

/// <summary>filter_logs levels and categories (shared contract).</summary>
public enum FilterLogLevel
{
    Debug,
    Info,
    Warn,
    Error,
    Security,
}

public sealed record FilterLogEvent(
    DateTime Ts,
    FilterLogLevel Level,
    string Module,
    string Category,
    string Message,
    string? Ip = null,
    string? Hwid = null,
    string? Username = null,
    string? Charname = null,
    string? Opcode = null,
    string? DataJson = null)
{
    public bool IsImportant => Level >= FilterLogLevel.Warn;

    public static string LevelKey(FilterLogLevel level) => level switch
    {
        FilterLogLevel.Debug => "debug",
        FilterLogLevel.Info => "info",
        FilterLogLevel.Warn => "warn",
        FilterLogLevel.Error => "error",
        FilterLogLevel.Security => "security",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };
}

public interface ILogRepository
{
    Task WriteAsync(IReadOnlyList<FilterLogEvent> events, CancellationToken cancellationToken);
}
