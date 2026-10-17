using VFilter.Core.Config;
using VFilter.Core.Modules;

namespace VFilter.Core.Tests;

public class ModuleControllerTests
{
    [Fact]
    public async Task Starts_download_then_agents_then_gateway_and_stops_in_reverse()
    {
        var h = await new Harness().LoadedAsync();

        await h.Modules.StartAllAsync(CancellationToken.None);
        await h.Modules.StopAsync("all", CancellationToken.None);

        Assert.Equal(["start Download", "start Agent1", "start Gateway", "stop Gateway", "stop Agent1", "stop Download"], h.Runner.Calls);
    }

    [Fact]
    public async Task Reports_state_uptime_and_counts()
    {
        var h = await new Harness().LoadedAsync();
        await h.Modules.StartAsync("agent1", CancellationToken.None);
        h.Time.Advance(TimeSpan.FromMinutes(3));

        var agent = h.Modules.Statuses().Single(s => s.Name == "Agent1");
        var gateway = h.Modules.Statuses().Single(s => s.Name == "Gateway");

        Assert.Equal(ModuleState.Running, agent.State);
        Assert.Equal(TimeSpan.FromMinutes(3), agent.Uptime);
        Assert.Equal(10, agent.OnlinePlayers);
        Assert.Equal(ModuleState.Stopped, gateway.State);
        Assert.Null(gateway.Uptime);
    }

    [Fact]
    public async Task Disabled_and_unknown_targets_fail()
    {
        var h = new Harness();
        h.ConfigRepository.Modules[1] = h.ConfigRepository.Modules[1] with { Enabled = false };
        await h.Config.RefreshAsync(false, CancellationToken.None);

        await Assert.ThrowsAsync<CommandFailedException>(() => h.Modules.StartAsync("download", CancellationToken.None));
        await Assert.ThrowsAsync<CommandFailedException>(() => h.Modules.StartAsync("agent7", CancellationToken.None));

        await h.Modules.StartAllAsync(CancellationToken.None);
        Assert.DoesNotContain("start Download", h.Runner.Calls);
    }

    [Fact]
    public async Task A_failed_start_is_reported_as_error()
    {
        var h = await new Harness().LoadedAsync();
        h.Runner.FailOnStart.Add("Gateway");

        var result = await h.Modules.StartAsync("gateway", CancellationToken.None);

        Assert.Contains("failed to start: port in use", result, StringComparison.Ordinal);
        var status = h.Modules.Statuses().Single(s => s.Name == "Gateway");
        Assert.Equal(ModuleState.Error, status.State);
        Assert.Equal("port in use", status.LastError);
    }

    [Fact]
    public async Task Repeated_start_and_stop_say_so()
    {
        var h = await new Harness().LoadedAsync();

        Assert.Equal("Agent1 already stopped", await h.Modules.StopAsync("agent1", CancellationToken.None));
        await h.Modules.StartAsync("agent1", CancellationToken.None);
        Assert.Equal("Agent1 already running", await h.Modules.StartAsync("agent1", CancellationToken.None));
    }

    [Fact]
    public async Task Flags_a_running_module_whose_addresses_changed()
    {
        var h = await new Harness().LoadedAsync();
        await h.Modules.StartAsync("agent1", CancellationToken.None);

        h.ConfigRepository.Modules[2] = h.ConfigRepository.Modules[2] with { GuardPort = 25884 };
        h.ConfigRepository.Versions[ConfigArea.Modules] = 2;
        await h.Config.RefreshAsync(false, CancellationToken.None);

        Assert.True(h.Modules.NeedsRestart("Agent1"));
        await h.Modules.RestartAsync("agent1", CancellationToken.None);
        Assert.False(h.Modules.NeedsRestart("Agent1"));
    }

    [Theory]
    [InlineData("1", GatewayMode.Inspection)]
    [InlineData("0", GatewayMode.Gm)]
    public async Task Gateway_starts_in_gm_unless_inspection_is_configured(string value, GatewayMode expected)
    {
        var h = new Harness();
        h.ConfigRepository.Settings.Add(new SettingRow(GatewayModeState.StartInInspectionKey, value, "bool"));
        await h.Config.RefreshAsync(false, CancellationToken.None);

        h.GatewayMode.Initialize(h.Config.Settings);

        Assert.Equal(expected, h.GatewayMode.Mode);
    }
}
