namespace VFilter.Core.Modules;

/// <summary>
/// Opens and closes one module's traffic path. The proxy (next feature) implements this; until then
/// <see cref="StandbyModuleRunner"/> reports state only.
/// </summary>
public interface IModuleRunner
{
    Task StartAsync(ModuleConfig config, CancellationToken cancellationToken);

    Task StopAsync(ModuleConfig config, CancellationToken cancellationToken);

    ModuleCounts GetCounts(string moduleName);
}

public readonly record struct ModuleCounts(int Connections, int OnlinePlayers);

/// <summary>No listener yet: starting a module only marks it ready (spec 001, Assumptions).</summary>
public sealed class StandbyModuleRunner : IModuleRunner
{
    public Task StartAsync(ModuleConfig config, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(ModuleConfig config, CancellationToken cancellationToken) => Task.CompletedTask;

    public ModuleCounts GetCounts(string moduleName) => default;
}
