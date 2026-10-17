using VFilter.Core.Config;

namespace VFilter.Core.Tests;

public class ConfigStoreTests
{
    [Fact]
    public async Task Loads_every_area_on_first_refresh()
    {
        var h = new Harness();
        h.ConfigRepository.Settings.Add(new SettingRow("server.start_in_inspection", "1", "bool"));
        h.ConfigRepository.Lists.Add(new ListRow("blocked_ips", "1.2.3.0/24", "lan"));
        h.ConfigRepository.Cooldowns.Add(new CooldownRow("teleport", 5));
        h.ConfigRepository.GaPermissions.Add(new GaPermissionRow("Helper1", ["notice"]));

        var changed = await h.Config.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(ConfigAreaNames.All, changed);
        Assert.True(h.Config.IsLoaded);
        Assert.True(h.Config.Settings.GetBool("server.start_in_inspection", false));
        Assert.Equal("1.2.3.0/24", h.Config.Lists.Get("blocked_ips").Single().Value);
        Assert.Equal(5, h.Config.Cooldowns.Seconds["teleport"]);
        Assert.True(h.Config.GaPermissions.Allows("helper1", "notice"));
        Assert.Equal(["Download", "Agent1", "Gateway"], h.Config.Modules.Modules.Select(m => m.Name));
    }

    [Fact]
    public async Task Reloads_only_areas_whose_counter_moved()
    {
        var h = await new Harness().LoadedAsync();
        h.ConfigRepository.Loaded.Clear();
        h.ConfigRepository.Lists.Add(new ListRow("blocked_ips", "9.9.9.9", null));
        h.ConfigRepository.Versions[ConfigArea.Lists] = 1;

        var changed = await h.Config.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal([ConfigArea.Lists], changed);
        Assert.Equal([ConfigArea.Lists], h.ConfigRepository.Loaded);
        Assert.Equal(1, h.Config.Lists.Version);
        Assert.Empty(await h.Config.RefreshAsync(force: false, CancellationToken.None));
    }

    [Fact]
    public async Task Force_reloads_everything()
    {
        var h = await new Harness().LoadedAsync();

        Assert.Equal(6, (await h.Config.RefreshAsync(force: true, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Keeps_the_last_good_configuration_when_the_database_fails()
    {
        var h = await new Harness().LoadedAsync();
        h.ConfigRepository.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Config.RefreshAsync(force: false, CancellationToken.None));

        Assert.Equal(3, h.Config.Modules.Modules.Length);
    }

    [Fact]
    public async Task Skips_unreadable_rows_and_keeps_the_rest()
    {
        var h = new Harness();
        h.ConfigRepository.Settings.AddRange([new("ok", "5", "int"), new("bad.int", "five", "int"), new("bad.type", "x", "weird")]);
        h.ConfigRepository.AreaRules.AddRange([new(1, "cafe_limit", "-1", "5.5.5.5", "{\"login_ip\":20}", true), new(2, "x", "-1", null, "{not json", true)]);
        h.ConfigRepository.Modules.Add(new(9, "mystery", "Agent9", "10.0.0.1", 1, "10.0.0.1", 2, true));

        await h.Config.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal("ok", Assert.Single(h.Config.Settings.Values.Keys));
        Assert.Equal(20, h.Config.AreaRules.Rules.Single().Params!.Value.GetProperty("login_ip").GetInt32());
        Assert.DoesNotContain(h.Config.Modules.Modules, m => m.Name == "Agent9");
    }

    [Fact]
    public async Task Poller_swallows_failures_and_recovers()
    {
        var h = await new Harness().LoadedAsync();
        var poller = new ConfigPoller(h.Config, h.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigPoller>.Instance);
        h.ConfigRepository.Fail = true;

        await poller.PollOnceAsync(CancellationToken.None);

        h.ConfigRepository.Fail = false;
        h.ConfigRepository.Versions[ConfigArea.Modules] = 4;
        await poller.PollOnceAsync(CancellationToken.None);

        Assert.Equal(4, h.Config.Modules.Version);
    }
}
