namespace VFilter.Core.Config;

/// <summary>Configuration areas, one change counter each in filter_config_version (shared contract).</summary>
public enum ConfigArea
{
    Settings,
    Lists,
    AreaRules,
    Cooldowns,
    Modules,
    GaPermissions,
}

public static class ConfigAreaNames
{
    private static readonly Dictionary<string, ConfigArea> ByKey = new(StringComparer.Ordinal)
    {
        ["settings"] = ConfigArea.Settings,
        ["lists"] = ConfigArea.Lists,
        ["area_rules"] = ConfigArea.AreaRules,
        ["cooldowns"] = ConfigArea.Cooldowns,
        ["modules"] = ConfigArea.Modules,
        ["ga_permissions"] = ConfigArea.GaPermissions,
    };

    public static IReadOnlyList<ConfigArea> All { get; } = Enum.GetValues<ConfigArea>();

    public static string Key(this ConfigArea area) => area switch
    {
        ConfigArea.Settings => "settings",
        ConfigArea.Lists => "lists",
        ConfigArea.AreaRules => "area_rules",
        ConfigArea.Cooldowns => "cooldowns",
        ConfigArea.Modules => "modules",
        ConfigArea.GaPermissions => "ga_permissions",
        _ => throw new ArgumentOutOfRangeException(nameof(area)),
    };

    public static bool TryParse(string key, out ConfigArea area) => ByKey.TryGetValue(key, out area);
}
