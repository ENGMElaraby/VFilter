using VFilter.Core.Commands;
using VFilter.Core.Config;
using VFilter.Core.Logging;
using VFilter.Core.Sessions;
using VFilter.Core.Status;

namespace VFilter.Data.Tests;

[Trait("Category", "SqlServer")]
public class RepositoryTests(SqlServerFixture db) : IClassFixture<SqlServerFixture>
{
    [SqlServerFact]
    public async Task Reads_every_configuration_area()
    {
        await db.ExecuteAsync("""
            INSERT INTO filter_config_version (area, version) VALUES ('lists', 3), ('modules', 1), ('unknown', 9);
            INSERT INTO filter_settings ([key], value, value_type, [group]) VALUES ('logs.retention_days', '30', 'int', 'logs');
            INSERT INTO filter_lists (list, value, comment) VALUES ('blocked_ips', '1.2.3.0/24', N'شبكة');
            INSERT INTO filter_area_rules ([rule], area, target, params) VALUES ('cafe_limit', '-1', '5.5.5.5', '{"login_ip":20}');
            INSERT INTO filter_cooldowns (action, seconds) VALUES ('teleport', 5);
            INSERT INTO filter_modules (type, name, module_ip, module_port, guard_ip, guard_port, enabled) VALUES ('agent', 'Agent1', '10.0.0.1', 15884, '203.0.113.1', 15884, 1);
            INSERT INTO filter_ga_permissions (username, notice, ban) VALUES ('helper1', 1, 1);
            """);
        var repository = new ConfigRepository(db.Factory);

        var versions = await repository.GetVersionsAsync(CancellationToken.None);
        Assert.Equal(3, versions[ConfigArea.Lists]);
        Assert.Equal(2, versions.Count);
        Assert.Equal("30", (await repository.GetSettingsAsync(CancellationToken.None)).Single().Value);
        Assert.Equal("شبكة", (await repository.GetListsAsync(CancellationToken.None)).Single().Comment);
        Assert.Equal("{\"login_ip\":20}", (await repository.GetAreaRulesAsync(CancellationToken.None)).Single().Params);
        Assert.Equal(5, (await repository.GetCooldownsAsync(CancellationToken.None)).Single().Seconds);
        Assert.True((await repository.GetModulesAsync(CancellationToken.None)).Single().Enabled);
        Assert.Equal(["notice", "ban"], (await repository.GetGaPermissionsAsync(CancellationToken.None)).Single().AllowedCommands);
    }

    [SqlServerFact]
    public async Task Claims_pending_commands_once_and_never_cancelled_ones()
    {
        await db.ExecuteAsync("DELETE FROM filter_commands");
        await db.ExecuteAsync("""
            INSERT INTO filter_commands (command, target, status, created_by, created_at) VALUES
              ('restart', 'agent1', 'pending', 1, DATEADD(second, -5, SYSUTCDATETIME())),
              ('stop', 'all', 'cancelled', 1, DATEADD(second, -9, SYSUTCDATETIME())),
              ('start', 'gateway', 'pending', 1, DATEADD(second, -2, SYSUTCDATETIME()));
            """);
        var repository = new CommandRepository(db.Factory);

        var ids = await repository.GetPendingIdsAsync(10, CancellationToken.None);
        Assert.Equal(2, ids.Count);

        var first = await repository.TryClaimAsync(ids[0], CancellationToken.None);
        Assert.Equal(("restart", "agent1"), (first!.Command, first.Target));
        Assert.True(first.DatabaseNow > first.CreatedAt);
        Assert.Null(await repository.TryClaimAsync(ids[0], CancellationToken.None));

        await repository.FinishAsync(first.Id, CommandOutcome.Done("ok"), CancellationToken.None);
        var row = (await db.QueryAsync<(string Status, string Result, DateTime? ProcessedAt)>("SELECT status, result, processed_at FROM filter_commands WHERE id = @id", new { id = first.Id })).Single();
        Assert.Equal(("done", "ok"), (row.Status, row.Result));
        Assert.NotNull(row.ProcessedAt);
    }

    [SqlServerFact]
    public async Task A_cancel_and_a_claim_never_both_win()
    {
        var repository = new CommandRepository(db.Factory);

        for (var i = 0; i < 20; i++)
        {
            var id = await db.ScalarAsync<long>("INSERT INTO filter_commands (command, target, created_by, created_at) OUTPUT inserted.id VALUES ('stop', 'all', 1, SYSUTCDATETIME())");
            var cancel = db.ScalarAsync<int>("UPDATE filter_commands SET status = 'cancelled' WHERE id = @id AND status = 'pending'; SELECT @@ROWCOUNT", new { id });
            var claim = repository.TryClaimAsync(id, CancellationToken.None);

            var cancelled = await cancel == 1;
            var claimed = await claim is not null;

            Assert.True(cancelled ^ claimed, $"command {id}: cancelled={cancelled} claimed={claimed}");
        }
    }

    [SqlServerFact]
    public async Task Heartbeat_upserts_with_the_database_clock()
    {
        var repository = new StatusRepository(db.Factory);
        StatusRow[] rows =
        [
            new("Gateway", "running", 0, 3, "gm", "0.1.0", 90_000, null),
            new("Agent1", "error", 0, 0, null, "0.1.0", null, "port in use"),
        ];

        var now = await repository.WriteAsync(rows, CancellationToken.None);
        await repository.WriteAsync(rows, CancellationToken.None);

        var stored = (await db.QueryAsync<(string Module, string State, string? GatewayMode, DateTime? StartedAt, DateTime HeartbeatAt, string? LastError)>(
            "SELECT module, state, gateway_mode, started_at, heartbeat_at, last_error FROM filter_status ORDER BY module")).ToDictionary(r => r.Module);
        Assert.Equal(2, stored.Count);
        Assert.Equal(("running", "gm"), (stored["Gateway"].State, stored["Gateway"].GatewayMode));
        Assert.InRange((stored["Gateway"].HeartbeatAt - stored["Gateway"].StartedAt!.Value).TotalSeconds, 89, 91);
        Assert.Null(stored["Agent1"].StartedAt);
        Assert.Equal("port in use", stored["Agent1"].LastError);
        Assert.InRange((stored["Gateway"].HeartbeatAt - now).TotalSeconds, -1, 5);
    }

    [SqlServerFact]
    public async Task Mirrors_sessions()
    {
        var repository = new SessionRepository(db.Factory);
        await repository.ClearAsync(CancellationToken.None);
        var registry = new SessionRegistry(TimeProvider.System);
        var session = registry.Open("Agent1", "10.0.0.5", _ => { });

        var id = await repository.InsertAsync(session, CancellationToken.None);
        session.DatabaseId = id;
        registry.Identify(session, "sara", "فارس", "HW-1");
        await repository.UpdateIdentityAsync(session, CancellationToken.None);
        await repository.TouchAsync(id, CancellationToken.None);

        var row = (await db.QueryAsync<(string Username, string Charname, string Hwid)>("SELECT username, charname, hwid FROM filter_sessions WHERE id = @id", new { id })).Single();
        Assert.Equal(("sara", "فارس", "HW-1"), row);

        await repository.DeleteAsync(id, CancellationToken.None);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM filter_sessions"));
    }

    [SqlServerFact]
    public async Task Bulk_writes_logs_and_cuts_long_values()
    {
        var repository = new LogRepository(db.Factory);
        var events = Enumerable.Range(0, 1_000)
            .Select(i => new FilterLogEvent(DateTime.UtcNow, FilterLogLevel.Security, "Agent1", "exploit", $"blocked {i}", Ip: "10.0.0.5", Opcode: "70211234567"))
            .Append(new FilterLogEvent(DateTime.UtcNow, FilterLogLevel.Info, "Filter", "command", new string('x', 2_000), DataJson: "{\"a\":1}"))
            .ToList();

        await repository.WriteAsync(events, CancellationToken.None);

        Assert.Equal(1_001, await db.ScalarAsync<int>("SELECT COUNT(*) FROM filter_logs"));
        Assert.Equal(1_024, await db.ScalarAsync<int>("SELECT MAX(LEN(message)) FROM filter_logs"));
        Assert.Equal("70211234", await db.ScalarAsync<string>("SELECT TOP 1 opcode FROM filter_logs WHERE opcode IS NOT NULL"));
    }
}
