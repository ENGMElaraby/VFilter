using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using VFilter.Core.Modules;

namespace VFilter.Core.Config;

public enum SettingType
{
    Flag,
    Number,
    Text,
    Json,
    Color,
}

public sealed record SettingValue(SettingType Type, string Raw)
{
    public bool AsBool() => Raw is "1" or "true";

    public int AsInt() => int.Parse(Raw, CultureInfo.InvariantCulture);
}

public sealed record SettingsSnapshot(long Version, FrozenDictionary<string, SettingValue> Values)
{
    public static SettingsSnapshot Empty { get; } = new(0, FrozenDictionary<string, SettingValue>.Empty);

    public bool GetBool(string key, bool fallback) =>
        Values.TryGetValue(key, out var value) && value.Type == SettingType.Flag ? value.AsBool() : fallback;

    public int GetInt(string key, int fallback) =>
        Values.TryGetValue(key, out var value) && value.Type == SettingType.Number ? value.AsInt() : fallback;
}

public sealed record ListEntry(string Value, string? Comment);

public sealed record ListsSnapshot(long Version, FrozenDictionary<string, ImmutableArray<ListEntry>> Lists)
{
    public static ListsSnapshot Empty { get; } = new(0, FrozenDictionary<string, ImmutableArray<ListEntry>>.Empty);

    public ImmutableArray<ListEntry> Get(string list) => Lists.TryGetValue(list, out var entries) ? entries : [];
}

public sealed record AreaRule(long Id, string Rule, string Area, string? Target, JsonElement? Params, bool Enabled);

public sealed record AreaRulesSnapshot(long Version, ImmutableArray<AreaRule> Rules)
{
    public static AreaRulesSnapshot Empty { get; } = new(0, []);
}

public sealed record CooldownsSnapshot(long Version, FrozenDictionary<string, int> Seconds)
{
    public static CooldownsSnapshot Empty { get; } = new(0, FrozenDictionary<string, int>.Empty);
}

public sealed record ModulesSnapshot(long Version, ImmutableArray<ModuleConfig> Modules)
{
    public static ModulesSnapshot Empty { get; } = new(0, []);

    public ModuleConfig? Find(string target) =>
        Modules.FirstOrDefault(m => string.Equals(m.Target, target, StringComparison.OrdinalIgnoreCase));
}

public sealed record GaPermissionsSnapshot(long Version, FrozenDictionary<string, FrozenSet<string>> Assistants)
{
    public static GaPermissionsSnapshot Empty { get; } = new(0, FrozenDictionary<string, FrozenSet<string>>.Empty);

    public bool Allows(string username, string command) =>
        Assistants.TryGetValue(username, out var commands) && commands.Contains(command);
}
