namespace VFilter.Core.Modules;

/// <summary>What the heartbeat reports for one module.</summary>
public sealed record ModuleStatus(
    string Name,
    ModuleType Type,
    ModuleState State,
    TimeSpan? Uptime,
    string? LastError,
    int Connections,
    int OnlinePlayers);
