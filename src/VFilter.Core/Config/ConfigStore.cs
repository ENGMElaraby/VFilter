using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VFilter.Core.Modules;

namespace VFilter.Core.Config;

/// <summary>
/// In-memory configuration: one immutable snapshot per area, swapped whole on reload so readers
/// never see a half-loaded area and never lock (spec 001, FR-001..FR-004).
/// </summary>
public sealed partial class ConfigStore(IConfigRepository repository, ILogger<ConfigStore> logger) : IDisposable
{
    private static readonly FrozenDictionary<string, SettingType> SettingTypes = new Dictionary<string, SettingType>(StringComparer.Ordinal)
    {
        ["bool"] = SettingType.Flag,
        ["int"] = SettingType.Number,
        ["string"] = SettingType.Text,
        ["json"] = SettingType.Json,
        ["color"] = SettingType.Color,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, ModuleType> ModuleTypes = new Dictionary<string, ModuleType>(StringComparer.Ordinal)
    {
        ["gateway"] = ModuleType.Gateway,
        ["download"] = ModuleType.Download,
        ["agent"] = ModuleType.Agent,
    }.ToFrozenDictionary();

    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private SettingsSnapshot _settings = SettingsSnapshot.Empty;
    private ListsSnapshot _lists = ListsSnapshot.Empty;
    private AreaRulesSnapshot _areaRules = AreaRulesSnapshot.Empty;
    private CooldownsSnapshot _cooldowns = CooldownsSnapshot.Empty;
    private ModulesSnapshot _modules = ModulesSnapshot.Empty;
    private GaPermissionsSnapshot _gaPermissions = GaPermissionsSnapshot.Empty;
    private readonly Dictionary<ConfigArea, long> _loadedVersions = [];

    /// <summary>Raised after an area was swapped in, with the areas that changed.</summary>
    public event Action<IReadOnlyList<ConfigArea>>? Changed;

    public bool IsLoaded { get; private set; }

    public SettingsSnapshot Settings => Volatile.Read(ref _settings);

    public ListsSnapshot Lists => Volatile.Read(ref _lists);

    public AreaRulesSnapshot AreaRules => Volatile.Read(ref _areaRules);

    public CooldownsSnapshot Cooldowns => Volatile.Read(ref _cooldowns);

    public ModulesSnapshot Modules => Volatile.Read(ref _modules);

    public GaPermissionsSnapshot GaPermissions => Volatile.Read(ref _gaPermissions);

    /// <summary>
    /// Reloads areas whose counter moved (or all, when <paramref name="force"/>). Throws when the
    /// database cannot be read; the previous snapshots stay in place.
    /// </summary>
    public async Task<IReadOnlyList<ConfigArea>> RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        await _reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var versions = await repository.GetVersionsAsync(cancellationToken).ConfigureAwait(false);
            var changed = new List<ConfigArea>();

            foreach (var area in ConfigAreaNames.All)
            {
                var version = versions.GetValueOrDefault(area);

                if (!force && IsLoaded && _loadedVersions.TryGetValue(area, out var loaded) && loaded == version)
                {
                    continue;
                }

                await LoadAreaAsync(area, version, cancellationToken).ConfigureAwait(false);
                _loadedVersions[area] = version;
                changed.Add(area);
            }

            IsLoaded = true;

            if (changed.Count > 0)
            {
                Changed?.Invoke(changed);
            }

            return changed;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private async Task LoadAreaAsync(ConfigArea area, long version, CancellationToken ct)
    {
        switch (area)
        {
            case ConfigArea.Settings:
                Volatile.Write(ref _settings, BuildSettings(version, await repository.GetSettingsAsync(ct).ConfigureAwait(false)));
                break;
            case ConfigArea.Lists:
                Volatile.Write(ref _lists, BuildLists(version, await repository.GetListsAsync(ct).ConfigureAwait(false)));
                break;
            case ConfigArea.AreaRules:
                Volatile.Write(ref _areaRules, BuildAreaRules(version, await repository.GetAreaRulesAsync(ct).ConfigureAwait(false)));
                break;
            case ConfigArea.Cooldowns:
                var cooldowns = await repository.GetCooldownsAsync(ct).ConfigureAwait(false);
                Volatile.Write(ref _cooldowns, new CooldownsSnapshot(version, cooldowns.ToFrozenDictionary(c => c.Action, c => c.Seconds, StringComparer.Ordinal)));
                break;
            case ConfigArea.Modules:
                Volatile.Write(ref _modules, BuildModules(version, await repository.GetModulesAsync(ct).ConfigureAwait(false)));
                break;
            case ConfigArea.GaPermissions:
                var assistants = await repository.GetGaPermissionsAsync(ct).ConfigureAwait(false);
                Volatile.Write(ref _gaPermissions, new GaPermissionsSnapshot(version, assistants.ToFrozenDictionary(
                    a => a.Username,
                    a => a.AllowedCommands.ToFrozenSet(StringComparer.Ordinal),
                    StringComparer.OrdinalIgnoreCase)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(area));
        }

        LogAreaLoaded(area, version);
    }

    private SettingsSnapshot BuildSettings(long version, IReadOnlyList<SettingRow> rows)
    {
        var values = new Dictionary<string, SettingValue>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!SettingTypes.TryGetValue(row.ValueType, out var type) || (type == SettingType.Number && !int.TryParse(row.Value, out _)))
            {
                LogSkippedRow("settings", row.Key);

                continue;
            }

            values[row.Key] = new SettingValue(type, row.Value);
        }

        return new SettingsSnapshot(version, values.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private static ListsSnapshot BuildLists(long version, IReadOnlyList<ListRow> rows) =>
        new(version, rows
            .GroupBy(r => r.List, StringComparer.Ordinal)
            .ToFrozenDictionary(g => g.Key, g => g.Select(r => new ListEntry(r.Value, r.Comment)).ToImmutableArray(), StringComparer.Ordinal));

    private AreaRulesSnapshot BuildAreaRules(long version, IReadOnlyList<AreaRuleRow> rows)
    {
        var rules = ImmutableArray.CreateBuilder<AreaRule>(rows.Count);

        foreach (var row in rows)
        {
            JsonElement? parameters = null;

            if (row.Params is not null)
            {
                try
                {
                    using var document = JsonDocument.Parse(row.Params);
                    parameters = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    LogSkippedRule(row.Id);

                    continue;
                }
            }

            rules.Add(new AreaRule(row.Id, row.Rule, row.Area, row.Target, parameters, row.Enabled));
        }

        return new AreaRulesSnapshot(version, rules.ToImmutable());
    }

    private ModulesSnapshot BuildModules(long version, IReadOnlyList<ModuleRow> rows)
    {
        var modules = ImmutableArray.CreateBuilder<ModuleConfig>(rows.Count);

        foreach (var row in rows)
        {
            if (!ModuleTypes.TryGetValue(row.Type, out var type))
            {
                LogSkippedRow("modules", row.Name);

                continue;
            }

            modules.Add(new ModuleConfig(row.Id, type, row.Name, row.ModuleIp, row.ModulePort, row.GuardIp, row.GuardPort, row.Enabled));
        }

        return new ModulesSnapshot(version, [.. modules.OrderBy(m => m.StartOrder)]);
    }

    public void Dispose() => _reloadLock.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped area rule {Id}: params is not valid JSON")]
    private partial void LogSkippedRule(long id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded configuration area {Area} at version {Version}")]
    private partial void LogAreaLoaded(ConfigArea area, long version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped unreadable {Area} row {Row}")]
    private partial void LogSkippedRow(string area, string row);
}
