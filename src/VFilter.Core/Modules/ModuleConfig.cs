namespace VFilter.Core.Modules;

public enum ModuleType
{
    Gateway,
    Download,
    Agent,
}

public enum ModuleState
{
    Stopped,
    Starting,
    Running,
    Error,
}

/// <summary>One proxied server module from filter_modules.</summary>
public sealed record ModuleConfig(int Id, ModuleType Type, string Name, string ModuleIp, int ModulePort, string GuardIp, int GuardPort, bool Enabled)
{
    /// <summary>Command target: the lower-case name (contract 0.3.1).</summary>
    public string Target => Name.ToLowerInvariant();

    public bool SameAddresses(ModuleConfig other) =>
        ModuleIp == other.ModuleIp && ModulePort == other.ModulePort && GuardIp == other.GuardIp && GuardPort == other.GuardPort;

    /// <summary>Start order: Download, then agents by number, Gateway last (players must not reach a gateway before agents are up).</summary>
    public int StartOrder => Type switch
    {
        ModuleType.Download => 0,
        ModuleType.Agent => 1 + AgentNumber,
        _ => 100,
    };

    private int AgentNumber => int.TryParse(Name.AsSpan(5), out var n) ? n : 0;
}
