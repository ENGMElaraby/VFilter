namespace VFilter.Core.Config;

// Raw rows as stored in VFILTER (shared contract 0.3.2). Parsed into snapshots by ConfigStore.

public sealed record SettingRow(string Key, string Value, string ValueType);

public sealed record ListRow(string List, string Value, string? Comment);

public sealed record AreaRuleRow(long Id, string Rule, string Area, string? Target, string? Params, bool Enabled);

public sealed record CooldownRow(string Action, int Seconds);

public sealed record ModuleRow(int Id, string Type, string Name, string ModuleIp, int ModulePort, string GuardIp, int GuardPort, bool Enabled);

public sealed record GaPermissionRow(string Username, IReadOnlyCollection<string> AllowedCommands);
