using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VFilter.Core.Config;
using VFilter.Core.Logging;
using VFilter.Core.Modules;
using VFilter.Core.Proxy;
using VFilter.Core.Sessions;
using VFilter.Core.Status;
using VFilter.Security;
using Xunit.Abstractions;

namespace VFilter.Core.Tests.Proxy;

/// <summary>
/// SC-004 measurement: round trips through the proxy versus straight to the module, with 200 concurrent
/// sessions. The difference is the latency the proxy adds (both directions).
/// </summary>
[Trait("Category", "Performance")]
public sealed class LatencyTests(ITestOutputHelper output)
{
    private const int Sessions = 200;
    private const int RoundTripsPerSession = 25;

    [Fact]
    public async Task Proxy_adds_little_latency_with_200_sessions()
    {
        var checksum = SecurityChecksum.Build();
        await using var module = new FakeModule(checksum, packet => [new Packet((ushort)(packet.Opcode + 1), packet.Payload, packet.Encrypted)]);
        var repository = new FakeConfigRepository();
        repository.Modules.Clear();
        repository.Modules.Add(new ModuleRow(3, "agent", "Agent1", "127.0.0.1", module.Port, "127.0.0.1", 0, true));
        using var config = new ConfigStore(repository, NullLogger<ConfigStore>.Instance);
        await config.RefreshAsync(false, CancellationToken.None);
        var registry = new SessionRegistry(TimeProvider.System);
        await using var runner = new ProxyModuleRunner([new AgentInspector()], registry, new FilterLog(new DatabaseClockOffset(TimeProvider.System)), Options.Create(new ProxyOptions()), TimeProvider.System, NullLogger<ProxyModuleRunner>.Instance);
        await runner.StartAsync(config.Modules.Modules[0], CancellationToken.None);

        var direct = await MeasureAsync(() => Peer.ConnectClientAsync(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, module.Port), checksum));
        var proxied = await MeasureAsync(() => Peer.ConnectClientAsync(runner.ListeningEndpoint("Agent1")!, checksum));

        output.WriteLine($"direct  p50 {direct.P50:F3} ms  p99 {direct.P99:F3} ms");
        output.WriteLine($"proxied p50 {proxied.P50:F3} ms  p99 {proxied.P99:F3} ms");
        output.WriteLine($"added   p50 {proxied.P50 - direct.P50:F3} ms  p99 {proxied.P99 - direct.P99:F3} ms (round trip = 2 proxy passes)");

        // Loose guard so CI noise does not fail builds; the printed numbers are the measurement.
        Assert.True(proxied.P99 - direct.P99 < 20, "proxy added more than 20 ms at p99");
    }

    private static async Task<(double P50, double P99)> MeasureAsync(Func<Task<Peer>> connect)
    {
        var peers = new List<Peer>();

        for (var i = 0; i < Sessions; i++)
        {
            var peer = await connect();
            await peer.WaitForHandshakeAsync();
            peers.Add(peer);
        }

        var samples = new System.Collections.Concurrent.ConcurrentBag<double>();
        await Task.WhenAll(peers.Select(async peer =>
        {
            for (var i = 0; i < RoundTripsPerSession; i++)
            {
                var started = Stopwatch.GetTimestamp();
                await peer.SendAsync(new Packet(0x7000, new byte[64], Encrypted: i % 2 == 0));
                await peer.ReceiveAsync();
                samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }));

        foreach (var peer in peers)
        {
            await peer.DisposeAsync();
        }

        var sorted = samples.Order().ToArray();

        return (sorted[sorted.Length / 2], sorted[(int)(sorted.Length * 0.99)]);
    }
}
