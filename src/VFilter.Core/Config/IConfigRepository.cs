namespace VFilter.Core.Config;

/// <summary>Reads configuration tables. Implemented in VFilter.Data.</summary>
public interface IConfigRepository
{
    Task<IReadOnlyDictionary<ConfigArea, long>> GetVersionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SettingRow>> GetSettingsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ListRow>> GetListsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AreaRuleRow>> GetAreaRulesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CooldownRow>> GetCooldownsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ModuleRow>> GetModulesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GaPermissionRow>> GetGaPermissionsAsync(CancellationToken cancellationToken);
}
