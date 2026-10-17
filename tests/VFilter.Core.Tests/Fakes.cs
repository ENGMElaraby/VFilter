using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using VFilter.Core.Commands;
using VFilter.Core.Config;
using VFilter.Core.Logging;
using VFilter.Core.Modules;
using VFilter.Core.Status;

namespace VFilter.Core.Tests;

internal sealed class FakeConfigRepository : IConfigRepository
{
    public Dictionary<ConfigArea, long> Versions { get; } = [];

    public List<SettingRow> Settings { get; } = [];

    public List<ListRow> Lists { get; } = [];

    public List<AreaRuleRow> AreaRules { get; } = [];

    public List<CooldownRow> Cooldowns { get; } = [];

    public List<ModuleRow> Modules { get; } =
    [
        new(1, "gateway", "Gateway", "10.0.0.1", 15779, "203.0.113.1", 15779, true),
        new(2, "download", "Download", "10.0.0.1", 15881, "203.0.113.1", 15881, true),
        new(3, "agent", "Agent1", "10.0.0.1", 15884, "203.0.113.1", 15884, true),
    ];

    public List<GaPermissionRow> GaPermissions { get; } = [];

    public bool Fail { get; set; }

    public List<ConfigArea> Loaded { get; } = [];

    public Task<IReadOnlyDictionary<ConfigArea, long>> GetVersionsAsync(CancellationToken cancellationToken) =>
        Fail ? throw new InvalidOperationException("db down") : Task.FromResult<IReadOnlyDictionary<ConfigArea, long>>(new Dictionary<ConfigArea, long>(Versions));

    public Task<IReadOnlyList<SettingRow>> GetSettingsAsync(CancellationToken cancellationToken) => Load(ConfigArea.Settings, Settings);

    public Task<IReadOnlyList<ListRow>> GetListsAsync(CancellationToken cancellationToken) => Load(ConfigArea.Lists, Lists);

    public Task<IReadOnlyList<AreaRuleRow>> GetAreaRulesAsync(CancellationToken cancellationToken) => Load(ConfigArea.AreaRules, AreaRules);

    public Task<IReadOnlyList<CooldownRow>> GetCooldownsAsync(CancellationToken cancellationToken) => Load(ConfigArea.Cooldowns, Cooldowns);

    public Task<IReadOnlyList<ModuleRow>> GetModulesAsync(CancellationToken cancellationToken) => Load(ConfigArea.Modules, Modules);

    public Task<IReadOnlyList<GaPermissionRow>> GetGaPermissionsAsync(CancellationToken cancellationToken) => Load(ConfigArea.GaPermissions, GaPermissions);

    private Task<IReadOnlyList<T>> Load<T>(ConfigArea area, List<T> rows)
    {
        Loaded.Add(area);

        return Task.FromResult<IReadOnlyList<T>>([.. rows]);
    }
}

internal sealed class RecordingRunner : IModuleRunner
{
    public List<string> Calls { get; } = [];

    public HashSet<string> FailOnStart { get; } = new(StringComparer.Ordinal);

    public Task StartAsync(ModuleConfig config, CancellationToken cancellationToken)
    {
        Calls.Add("start " + config.Name);

        return FailOnStart.Contains(config.Name) ? throw new InvalidOperationException("port in use") : Task.CompletedTask;
    }

    public Task StopAsync(ModuleConfig config, CancellationToken cancellationToken)
    {
        Calls.Add("stop " + config.Name);

        return Task.CompletedTask;
    }

    public ModuleCounts GetCounts(string moduleName) => moduleName == "Agent1" ? new ModuleCounts(12, 10) : default;
}

internal sealed class FakeCommandRepository : ICommandRepository
{
    public DateTime Now { get; set; } = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    public List<Row> Rows { get; } = [];

    public sealed class Row
    {
        public required long Id { get; init; }

        public required string Command { get; init; }

        public required string Target { get; init; }

        public string? Payload { get; init; }

        public string Status { get; set; } = "pending";

        public string? Result { get; set; }

        public DateTime CreatedAt { get; init; }
    }

    public Row Add(string command, string target, string? payload = null, TimeSpan? age = null)
    {
        var row = new Row { Id = Rows.Count + 1, Command = command, Target = target, Payload = payload, CreatedAt = Now - (age ?? TimeSpan.FromSeconds(1)) };
        Rows.Add(row);

        return row;
    }

    public Task<IReadOnlyList<long>> GetPendingIdsAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<long>>([.. Rows.Where(r => r.Status == "pending").OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Take(limit).Select(r => r.Id)]);

    public Task<ClaimedCommand?> TryClaimAsync(long id, CancellationToken cancellationToken)
    {
        var row = Rows.Single(r => r.Id == id);

        if (row.Status != "pending")
        {
            return Task.FromResult<ClaimedCommand?>(null);
        }

        row.Status = "running";

        return Task.FromResult<ClaimedCommand?>(new ClaimedCommand(row.Id, row.Command, row.Target, row.Payload, row.CreatedAt, Now));
    }

    public Task FinishAsync(long id, CommandOutcome outcome, CancellationToken cancellationToken)
    {
        var row = Rows.Single(r => r.Id == id && r.Status == "running");
        row.Status = outcome.Status;
        row.Result = outcome.Result;

        return Task.CompletedTask;
    }
}

internal sealed class Harness
{
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));

    public FakeConfigRepository ConfigRepository { get; } = new();

    public RecordingRunner Runner { get; } = new();

    public FakeCommandRepository Commands { get; } = new();

    public ConfigStore Config { get; }

    public ModuleController Modules { get; }

    public GatewayModeState GatewayMode { get; } = new();

    public Sessions.SessionRegistry Sessions { get; }

    public FilterLog Log { get; }

    public CommandProcessor Processor { get; }

    public Harness()
    {
        Config = new ConfigStore(ConfigRepository, NullLogger<ConfigStore>.Instance);
        Modules = new ModuleController(Runner, Config, Time, NullLogger<ModuleController>.Instance);
        Sessions = new Sessions.SessionRegistry(Time);
        Log = new FilterLog(new DatabaseClockOffset(Time));
        Processor = new CommandProcessor(Commands, Modules, GatewayMode, Config, Sessions, Log, Time, NullLogger<CommandProcessor>.Instance);
    }

    public async Task<Harness> LoadedAsync()
    {
        await Config.RefreshAsync(force: false, CancellationToken.None);

        return this;
    }
}
