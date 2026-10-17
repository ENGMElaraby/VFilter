using VFilter.Core.Modules;

namespace VFilter.Core.Tests;

public class CommandProcessorTests
{
    private static async Task<Harness> RunAsync(Action<Harness> arrange)
    {
        var h = await new Harness().LoadedAsync();
        arrange(h);
        await h.Processor.ProcessPendingAsync(CancellationToken.None);

        return h;
    }

    [Fact]
    public async Task Restarts_a_module_and_reports_done()
    {
        FakeCommandRepository.Row? row = null;
        var h = await RunAsync(h => row = h.Commands.Add("restart", "agent1"));

        Assert.Equal("done", row!.Status);
        Assert.Equal("stop: Agent1 already stopped; start: Agent1 started", row.Result);
        Assert.Equal(["start Agent1"], h.Runner.Calls);
        Assert.Contains(h.Log.Take(10), e => e.Category == "command" && e.Message.StartsWith("restart agent1: done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Runs_commands_oldest_first()
    {
        var h = await RunAsync(h =>
        {
            h.Commands.Add("start", "gateway", age: TimeSpan.FromSeconds(1));
            h.Commands.Add("start", "download", age: TimeSpan.FromSeconds(5));
        });

        Assert.Equal(["start Download", "start Gateway"], h.Runner.Calls);
    }

    [Fact]
    public async Task Never_runs_a_command_it_could_not_claim()
    {
        FakeCommandRepository.Row? cancelled = null;
        var h = await RunAsync(h =>
        {
            cancelled = h.Commands.Add("stop", "all");
            cancelled.Status = "cancelled";
        });

        Assert.Equal("cancelled", cancelled!.Status);
        Assert.Empty(h.Runner.Calls);
    }

    [Fact]
    public async Task Expires_commands_older_than_ten_minutes()
    {
        FakeCommandRepository.Row? row = null;
        var h = await RunAsync(h => row = h.Commands.Add("stop", "all", age: TimeSpan.FromMinutes(11)));

        Assert.Equal(("failed", "expired"), (row!.Status, row.Result));
        Assert.Empty(h.Runner.Calls);
    }

    [Fact]
    public async Task Switches_the_gateway_mode()
    {
        FakeCommandRepository.Row? row = null;
        var h = await RunAsync(h => row = h.Commands.Add("set_gateway_mode", "gateway", "{\"mode\":\"public\"}"));

        Assert.Equal(GatewayMode.Public, h.GatewayMode.Mode);
        Assert.Equal(("done", "gateway mode gm → public"), (row!.Status, row.Result));
    }

    [Theory]
    [InlineData("set_gateway_mode", "gateway", "{\"mode\":\"party\"}", "bad payload: mode must be gm, inspection or public")]
    [InlineData("set_gateway_mode", "gateway", "{not json", "bad payload")]
    [InlineData("set_gateway_mode", "agent1", "{\"mode\":\"public\"}", "gateway mode applies to the gateway only")]
    [InlineData("start", "agent7", null, "unknown target agent7")]
    [InlineData("notice", "all", "{\"text\":\"hi\"}", "not supported yet")]
    [InlineData("teleport", "all", null, "unknown command teleport")]
    [InlineData("kick", "Knight", null, "player not online")]
    public async Task Fails_clearly_and_keeps_going(string command, string target, string? payload, string result)
    {
        FakeCommandRepository.Row? bad = null;
        FakeCommandRepository.Row? next = null;
        await RunAsync(h =>
        {
            bad = h.Commands.Add(command, target, payload, age: TimeSpan.FromSeconds(3));
            next = h.Commands.Add("start", "download", age: TimeSpan.FromSeconds(2));
        });

        Assert.Equal(("failed", result), (bad!.Status, bad.Result));
        Assert.Equal("done", next!.Status);
    }

    [Fact]
    public async Task Reload_reads_every_area_again()
    {
        FakeCommandRepository.Row? row = null;
        await RunAsync(h => row = h.Commands.Add("reload", "all"));

        Assert.Equal(("done", "reloaded: settings, lists, area_rules, cooldowns, modules, ga_permissions"), (row!.Status, row.Result));
    }

    [Fact]
    public async Task Kick_disconnects_matching_sessions()
    {
        var reasons = new List<string>();
        FakeCommandRepository.Row? row = null;
        await RunAsync(h =>
        {
            var session = h.Sessions.Open("Agent1", "10.0.0.5", reasons.Add);
            h.Sessions.Identify(session, "sara", "Knight", null);
            h.Sessions.Open("Agent1", "10.0.0.6", reasons.Add);
            row = h.Commands.Add("kick", "knight", "{\"session_id\":1,\"ip\":\"10.0.0.5\",\"reason\":\"afk\"}");
        });

        Assert.Equal(("done", "disconnected 1 session(s)"), (row!.Status, row.Result));
        Assert.Equal(["afk"], reasons);
    }

    [Fact]
    public async Task Ban_disconnects_but_reports_the_account_ban_as_not_done_yet()
    {
        var reasons = new List<string>();
        FakeCommandRepository.Row? row = null;
        await RunAsync(h =>
        {
            var session = h.Sessions.Open("Agent1", "10.0.0.5", reasons.Add);
            h.Sessions.Identify(session, "sara", null, null);
            row = h.Commands.Add("ban", "sara", "{\"session_id\":1,\"reason\":\"speed hack\",\"duration_minutes\":1440}");
        });

        Assert.Equal(("failed", "account ban not supported yet; disconnected 1 session(s)"), (row!.Status, row.Result));
        Assert.Equal(["speed hack"], reasons);
    }

    [Fact]
    public void Long_results_are_cut_to_500_characters()
    {
        var outcome = Commands.CommandOutcome.Failed(new string('x', 900));

        Assert.Equal(500, outcome.Result.Length);
    }
}
