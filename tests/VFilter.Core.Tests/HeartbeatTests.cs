using Microsoft.Extensions.Logging.Abstractions;
using VFilter.Core.Modules;
using VFilter.Core.Status;

namespace VFilter.Core.Tests;

public class HeartbeatTests
{
    private sealed class RecordingStatus : IStatusRepository
    {
        public List<IReadOnlyList<StatusRow>> Writes { get; } = [];

        public bool Fail { get; set; }

        public Task<DateTime> WriteAsync(IReadOnlyList<StatusRow> rows, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("db down");
            }

            Writes.Add(rows);

            return Task.FromResult(new DateTime(2026, 10, 17, 12, 0, 30, DateTimeKind.Utc));
        }
    }

    [Fact]
    public async Task Reports_every_configured_module_with_the_gateway_mode()
    {
        var h = await new Harness().LoadedAsync();
        var repository = new RecordingStatus();
        var clock = new DatabaseClockOffset(h.Time);
        var heartbeat = new HeartbeatService(h.Modules, h.GatewayMode, repository, clock, h.Time, NullLogger<HeartbeatService>.Instance);
        await h.Modules.StartAsync("agent1", CancellationToken.None);
        h.GatewayMode.Set(GatewayMode.Inspection);
        h.Time.Advance(TimeSpan.FromSeconds(10));

        await heartbeat.BeatAsync(CancellationToken.None);

        var rows = repository.Writes.Single().ToDictionary(r => r.Module);
        Assert.Equal(["Download", "Agent1", "Gateway"], rows.Keys);
        Assert.Equal(("running", 10, 12, 10_000L), (rows["Agent1"].State, rows["Agent1"].OnlineCount, rows["Agent1"].Connections, rows["Agent1"].UptimeMilliseconds));
        Assert.Equal("inspection", rows["Gateway"].GatewayMode);
        Assert.Null(rows["Agent1"].GatewayMode);
        Assert.Equal(("stopped", (long?)null), (rows["Gateway"].State, rows["Gateway"].UptimeMilliseconds));
        Assert.Equal(TimeSpan.FromSeconds(20), clock.Offset);
    }

    [Fact]
    public async Task A_failed_write_does_not_throw()
    {
        var h = await new Harness().LoadedAsync();
        var heartbeat = new HeartbeatService(h.Modules, h.GatewayMode, new RecordingStatus { Fail = true }, new DatabaseClockOffset(h.Time), h.Time, NullLogger<HeartbeatService>.Instance);

        await heartbeat.BeatAsync(CancellationToken.None);
    }
}
