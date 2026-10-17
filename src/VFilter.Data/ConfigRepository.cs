using Dapper;
using VFilter.Core.Config;

namespace VFilter.Data;

/// <summary>Reads the configuration tables of shared contract 0.3.2. Read only.</summary>
public sealed class ConfigRepository(SqlConnectionFactory db) : IConfigRepository
{
    private static readonly string[] GaCommands =
    [
        "make_item", "zoe", "load_monster", "mobkill0", "mobkill1", "notice", "filter_cmd", "invisible",
        "invincible", "ban", "recall_user", "recall_guild", "move_to_user", "move_to_npc", "to_town",
        "go_town", "reset_q", "wp", "gm_skill", "world_status",
    ];

    public async Task<IReadOnlyDictionary<ConfigArea, long>> GetVersionsAsync(CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<(string Area, long Version)>("SELECT area, version FROM filter_config_version", cancellationToken).ConfigureAwait(false);
        var versions = new Dictionary<ConfigArea, long>();

        foreach (var (area, version) in rows)
        {
            if (ConfigAreaNames.TryParse(area, out var parsed))
            {
                versions[parsed] = version;
            }
        }

        return versions;
    }

    public Task<IReadOnlyList<SettingRow>> GetSettingsAsync(CancellationToken cancellationToken) =>
        QueryAsync<SettingRow>("SELECT [key] AS [Key], value AS Value, value_type AS ValueType FROM filter_settings", cancellationToken);

    public Task<IReadOnlyList<ListRow>> GetListsAsync(CancellationToken cancellationToken) =>
        QueryAsync<ListRow>("SELECT list AS List, value AS Value, comment AS Comment FROM filter_lists ORDER BY id", cancellationToken);

    public Task<IReadOnlyList<AreaRuleRow>> GetAreaRulesAsync(CancellationToken cancellationToken) =>
        QueryAsync<AreaRuleRow>("SELECT id AS Id, [rule] AS [Rule], area AS Area, target AS Target, params AS Params, enabled AS Enabled FROM filter_area_rules ORDER BY id", cancellationToken);

    public Task<IReadOnlyList<CooldownRow>> GetCooldownsAsync(CancellationToken cancellationToken) =>
        QueryAsync<CooldownRow>("SELECT action AS Action, seconds AS Seconds FROM filter_cooldowns", cancellationToken);

    public Task<IReadOnlyList<ModuleRow>> GetModulesAsync(CancellationToken cancellationToken) =>
        QueryAsync<ModuleRow>("SELECT id AS Id, type AS Type, name AS Name, module_ip AS ModuleIp, module_port AS ModulePort, guard_ip AS GuardIp, guard_port AS GuardPort, enabled AS Enabled FROM filter_modules", cancellationToken);

    public async Task<IReadOnlyList<GaPermissionRow>> GetGaPermissionsAsync(CancellationToken cancellationToken)
    {
        var sql = $"SELECT username, {string.Join(", ", GaCommands)} FROM filter_ga_permissions";
        await using var connection = await db.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync(new CommandDefinition(sql, commandTimeout: db.CommandTimeout, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(row =>
        {
            var values = (IDictionary<string, object?>)row;

            return new GaPermissionRow((string)values["username"]!, [.. GaCommands.Where(c => values[c] is true)]);
        })];
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken).ConfigureAwait(false);

        return [.. await connection.QueryAsync<T>(new CommandDefinition(sql, commandTimeout: db.CommandTimeout, cancellationToken: cancellationToken)).ConfigureAwait(false)];
    }
}
