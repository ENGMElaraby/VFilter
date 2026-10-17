using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VFilter.Core.Config;
using VFilter.Core.Logging;
using VFilter.Core.Modules;
using VFilter.Core.Proxy;
using VFilter.Core.Sessions;
using VFilter.Core.Status;
using VFilter.Security;

namespace VFilter.Core.Tests.Proxy;

/// <summary>
/// The proxy over real loopback sockets: fake modules and fake clients speak the full Silkroad security
/// protocol (handshake, Blowfish, count and checksum bytes) through it.
/// </summary>
public sealed class ProxyTests : IAsyncLifetime
{
    private static readonly SecurityChecksum Checksum = SecurityChecksum.Build();

    private readonly FakeConfigRepository _repository = new();
    private readonly List<IAsyncDisposable> _cleanup = [];
    private FakeModule _gateway = null!;
    private FakeModule _agent = null!;

    private ConfigStore Config { get; set; } = null!;

    private GatewayModeState Mode { get; } = new();

    private SessionRegistry Sessions { get; } = new(TimeProvider.System);

    private FilterLog Log { get; } = new(new DatabaseClockOffset(TimeProvider.System));

    private ProxyModuleRunner Runner { get; set; } = null!;

    private ModuleController Modules { get; set; } = null!;

    public async Task InitializeAsync()
    {
        _gateway = new FakeModule(Checksum, packet => packet.Opcode switch
        {
            Opcodes.GatewayLoginRequest => [LoginAnswer("127.0.0.1", (ushort)AgentPort)],
            0x6100 => [PatchAnswer("127.0.0.1", (ushort)((IPEndPoint)_silentModule.LocalEndpoint).Port)],
            _ => [],
        });
        _agent = new FakeModule(Checksum, packet => packet.Opcode switch
        {
            0x7777 => [new Packet(0x7778, packet.Payload, packet.Encrypted)],
            Opcodes.AgentAuthRequest => [new Packet(Opcodes.AgentAuthResponse, [Opcodes.LoginSucceeded])],
            Opcodes.LobbySelectCharacter => [new Packet(Opcodes.LobbySelectCharacterResponse, [Opcodes.LoginSucceeded])],
            _ => [],
        });
        _silentModule = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        _silentModule.Start();
        _cleanup.AddRange([_gateway, _agent]);

        _repository.Modules.Clear();
        _repository.Modules.Add(new ModuleRow(1, "gateway", "Gateway", "127.0.0.1", _gateway.Port, "127.0.0.1", 0, true));
        _repository.Modules.Add(new ModuleRow(3, "agent", "Agent1", "127.0.0.1", _agent.Port, "203.0.113.7", 0, true));
        // Download points at a module that accepts but never starts its handshake.
        _repository.Modules.Add(new ModuleRow(2, "download", "Download", "127.0.0.1", ((IPEndPoint)_silentModule.LocalEndpoint).Port, "198.51.100.5", 0, true));
        Config = new ConfigStore(_repository, NullLogger<ConfigStore>.Instance);
        await Config.RefreshAsync(force: false, CancellationToken.None);
        Mode.Set(GatewayMode.Public);

        Runner = new ProxyModuleRunner(
            [_rewriter, _recorder, new GatewayInspector(Config, Mode), new AgentInspector()],
            Sessions,
            Log,
            Options.Create(new ProxyOptions { HandshakeTimeout = TimeSpan.FromSeconds(2) }),
            TimeProvider.System,
            NullLogger<ProxyModuleRunner>.Instance);
        Modules = new ModuleController(Runner, Config, TimeProvider.System, NullLogger<ModuleController>.Instance);
        await Modules.StartAllAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Runner.DisposeAsync();
        _silentModule.Stop();

        foreach (var item in _cleanup)
        {
            await item.DisposeAsync();
        }

        Modules.Dispose();
        Config.Dispose();
    }

    private int AgentPort => _agent.Port;

    private readonly ReplacingInspector _rewriter = new();
    private readonly RecordingInspector _recorder = new();
    private System.Net.Sockets.TcpListener _silentModule = null!;

    private sealed class ReplacingInspector : IPacketInspector
    {
        public bool AppliesTo(ModuleType type) => true;

        public Inspection Inspect(ProxySession session, PacketDirection direction, Packet packet) =>
            packet.Opcode == 0x7777 ? Inspection.Replace(packet with { Payload = [9] }) : Inspection.Pass(packet);
    }

    private sealed class RecordingInspector : IPacketInspector
    {
        public System.Collections.Concurrent.ConcurrentQueue<byte[]> Seen { get; } = new();

        public bool AppliesTo(ModuleType type) => true;

        public Inspection Inspect(ProxySession session, PacketDirection direction, Packet packet)
        {
            if (packet.Opcode == 0x7777)
            {
                Seen.Enqueue(packet.Payload);
            }

            return Inspection.Pass(packet);
        }
    }

    [Fact]
    public async Task Forwards_plain_encrypted_and_massive_packets_both_ways()
    {
        await using var client = await ConnectAsync("Agent1");
        await client.WaitForHandshakeAsync();
        var module = await _agent.NextConnectionAsync();

        await client.SendAsync(new Packet(0x7021, [1, 2, 3], Encrypted: false));
        await client.SendAsync(new Packet(0x7022, [4, 5, 6, 7, 8, 9, 10, 11, 12], Encrypted: true));
        var big = Enumerable.Range(0, 10_000).Select(i => (byte)i).ToArray();
        await module.SendAsync(new Packet(0x3013, big, Massive: true));
        await module.SendAsync(new Packet(0xB021, [9], Encrypted: true));

        var first = await module.ReceiveAsync();
        var second = await module.ReceiveAsync();
        Assert.Equal((0x7021, false), (first.Opcode, first.Encrypted));
        Assert.Equal(new byte[] { 1, 2, 3 }, first.Payload);
        Assert.Equal((0x7022, true), (second.Opcode, second.Encrypted));
        Assert.Equal(new byte[] { 4, 5, 6, 7, 8, 9, 10, 11, 12 }, second.Payload);

        var massive = await client.ReceiveAsync();
        var small = await client.ReceiveAsync();
        Assert.Equal(0x3013, massive.Opcode);
        Assert.Equal(big, massive.Payload);
        Assert.Equal((0xB021, true), (small.Opcode, small.Encrypted));
    }

    [Fact]
    public async Task Sends_the_client_to_the_agent_guard_address_never_the_real_one()
    {
        await using var client = await ConnectAsync("Gateway");
        await client.WaitForHandshakeAsync();

        await client.SendAsync(LoginRequest("sara"));
        var answer = (await client.ReceiveAsync()).OpenReader();

        Assert.Equal(Opcodes.LoginSucceeded, answer.ReadByte());
        Assert.Equal(0xC0FFEEu, answer.ReadUInt32());
        Assert.Equal("203.0.113.7", answer.ReadAscii());
        Assert.Equal(0, answer.ReadUInt16());
        Assert.Equal(new byte[] { 0xAA }, answer.ReadBytes(answer.Remaining));
    }

    [Fact]
    public async Task Closes_the_session_when_the_gateway_names_an_unknown_agent()
    {
        _repository.Modules[1] = _repository.Modules[1] with { ModuleIp = "10.1.1.1" };
        _repository.Versions[ConfigArea.Modules] = 2;
        await Config.RefreshAsync(false, CancellationToken.None);
        await using var client = await ConnectAsync("Gateway");
        await client.WaitForHandshakeAsync();

        await client.SendAsync(LoginRequest("sara"));

        await client.WaitDisconnectedAsync();
        Assert.False(client.TryReceive(out _));
        Assert.Contains(Log.Take(100), e => e.Level == FilterLogLevel.Security && e.Message.Contains("unknown agent address", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(GatewayMode.Gm, null, 6)]
    [InlineData(GatewayMode.Inspection, "9", 9)]
    public async Task Refuses_logins_from_other_ips_outside_public_mode(GatewayMode mode, string? inspectionCode, int expectedCode)
    {
        Mode.Set(mode);

        if (inspectionCode is not null)
        {
            _repository.Settings.Add(new SettingRow(GatewayInspector.InspectionRefusalCodeKey, inspectionCode, "int"));
            _repository.Versions[ConfigArea.Settings] = 5;
            await Config.RefreshAsync(false, CancellationToken.None);
        }

        await using var client = await ConnectAsync("Gateway");
        await client.WaitForHandshakeAsync();
        var module = await _gateway.NextConnectionAsync();

        await client.SendAsync(LoginRequest("sara"));
        var answer = (await client.ReceiveAsync()).OpenReader();

        Assert.Equal((Opcodes.LoginFailed, (byte)expectedCode), (answer.ReadByte(), answer.ReadByte()));
        await Task.Delay(200);
        Assert.False(module.TryReceive(out _));
    }

    [Fact]
    public async Task Lets_allowed_gm_ips_log_in_in_gm_mode()
    {
        Mode.Set(GatewayMode.Gm);
        _repository.Lists.Add(new ListRow(GatewayInspector.AllowedIpsList, "127.0.0.0/8", null));
        _repository.Versions[ConfigArea.Lists] = 3;
        await Config.RefreshAsync(false, CancellationToken.None);
        await using var client = await ConnectAsync("Gateway");
        await client.WaitForHandshakeAsync();

        await client.SendAsync(LoginRequest("gm"));

        Assert.Equal(Opcodes.LoginSucceeded, (await client.ReceiveAsync()).OpenReader().ReadByte());
    }

    [Fact]
    public async Task Tracks_who_is_on_an_agent_counts_them_and_kicks()
    {
        await using var client = await ConnectAsync("Agent1");
        await client.WaitForHandshakeAsync();
        await _agent.NextConnectionAsync();

        var auth = new PacketWriter();
        auth.WriteUInt32(0xC0FFEE);
        auth.WriteAscii("sara");
        auth.WriteAscii("secret");
        auth.WriteByte(22);
        auth.WriteBytes(new byte[6]);
        await client.SendAsync(auth.ToPacket(Opcodes.AgentAuthRequest, encrypted: true));
        var select = new PacketWriter();
        select.WriteAscii("Knight");
        await client.SendAsync(select.ToPacket(Opcodes.LobbySelectCharacter));

        await Peer.WaitUntilAsync(() => Runner.GetCounts("Agent1") == new ModuleCounts(1, 1));

        Assert.Equal(1, Sessions.Kick("knight", "test"));
        await client.WaitDisconnectedAsync();
        await Peer.WaitUntilAsync(() => Runner.GetCounts("Agent1") == default(ModuleCounts) && Sessions.Count == 0);
    }

    [Fact]
    public async Task A_forged_client_packet_closes_only_its_own_session()
    {
        await using var good = await ConnectAsync("Agent1");
        await good.WaitForHandshakeAsync();
        var goodModule = await _agent.NextConnectionAsync();

        var otherTable = SecurityChecksum.FromPolynomials(Enumerable.Range(0, SecurityPolynomials.RequiredLength).Select(i => (byte)(i * 31)).ToArray());
        await using var forger = await Peer.ConnectClientAsync(Runner.ListeningEndpoint("Agent1")!, otherTable);

        await forger.WaitDisconnectedAsync();
        await good.SendAsync(new Packet(0x7777, [5]));

        var received = await goodModule.ReceiveAsync();
        Assert.Equal(0x7777, received.Opcode);
        // Inspectors run as a chain: the second one saw what the first one replaced.
        Assert.Equal(new byte[] { 9 }, received.Payload);
        Assert.Equal(new byte[] { 9 }, Assert.Single(_recorder.Seen));
        Assert.Equal(0x7778, (await good.ReceiveAsync()).Opcode);
        Assert.Contains(Log.Take(100), e => e.Level == FilterLogLevel.Security);
    }

    [Fact]
    public async Task Disconnects_a_client_that_never_finishes_the_handshake()
    {
        using var silent = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await silent.ConnectAsync(new IPEndPoint(IPAddress.Loopback, Runner.ListeningEndpoint("Agent1")!.Port));
        var buffer = new byte[1024];

        // The opening 0x5000 arrives, then the connection is closed after the 2 s handshake timeout.
        Assert.True(await silent.ReceiveAsync(buffer) > 0);
        var closed = await silent.ReceiveAsync(buffer).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, closed);
    }

    [Fact]
    public async Task Disconnects_the_client_when_the_real_module_is_down()
    {
        await _agent.DisposeAsync();
        _cleanup.Remove(_agent);

        await using var client = await ConnectAsync("Agent1");

        await client.WaitDisconnectedAsync();
        Assert.Contains(Log.Take(100), e => e.Level == FilterLogLevel.Error && e.Message.Contains("unreachable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stopping_a_module_closes_its_listener_and_sessions()
    {
        await using var client = await ConnectAsync("Agent1");
        await client.WaitForHandshakeAsync();

        await Modules.StopAsync("agent1", CancellationToken.None);

        await client.WaitDisconnectedAsync();
        Assert.Null(Runner.ListeningEndpoint("Agent1"));
    }

    [Fact]
    public async Task A_guard_port_in_use_puts_only_that_module_in_error()
    {
        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        var port = ((IPEndPoint)blocker.LocalEndpoint).Port;
        _repository.Modules[1] = _repository.Modules[1] with { GuardIp = "127.0.0.1", GuardPort = port };
        _repository.Versions[ConfigArea.Modules] = 7;
        await Config.RefreshAsync(false, CancellationToken.None);

        await Modules.RestartAsync("agent1", CancellationToken.None);

        var statuses = Modules.Statuses().ToDictionary(s => s.Name);
        Assert.Equal(ModuleState.Error, statuses["Agent1"].State);
        Assert.Equal(ModuleState.Running, statuses["Gateway"].State);
    }

    [Fact]
    public async Task Sends_patching_clients_to_the_download_guard_address()
    {
        await using var client = await ConnectAsync("Gateway");
        await client.WaitForHandshakeAsync();

        var request = new PacketWriter();
        request.WriteByte(22);
        request.WriteAscii("SR_Client");
        request.WriteUInt32(187);
        await client.SendAsync(request.ToPacket(0x6100));
        var answer = (await client.ReceiveAsync()).OpenReader();

        Assert.Equal((Opcodes.LoginFailed, Opcodes.PatchAvailable), (answer.ReadByte(), answer.ReadByte()));
        Assert.Equal("198.51.100.5", answer.ReadAscii());
        Assert.Equal(0, answer.ReadUInt16());
        Assert.Equal(188u, answer.ReadUInt32());
    }

    [Fact]
    public async Task Names_only_what_the_server_confirmed()
    {
        await using var client = await ConnectAsync("Gateway");
        await client.WaitForHandshakeAsync();
        _repository.Modules[1] = _repository.Modules[1] with { ModuleIp = "10.1.1.1" };
        _repository.Versions[ConfigArea.Modules] = 9;
        await Config.RefreshAsync(false, CancellationToken.None);
        Mode.Set(GatewayMode.Gm);

        // Refused in GM mode: the claimed name is never recorded.
        await client.SendAsync(LoginRequest("admin"));
        await client.ReceiveAsync();

        Assert.Equal(0, Sessions.Kick("admin", "test"));
    }

    [Fact]
    public async Task Closes_a_session_whose_module_never_starts_its_handshake()
    {
        await using var client = await ConnectAsync("Download");

        await client.WaitDisconnectedAsync();
        Assert.Contains(Log.Take(100), e => e.Message.Contains("module handshake not completed in time", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_kick_racing_a_disconnect_leaves_no_session_behind()
    {
        for (var i = 0; i < 20; i++)
        {
            var client = await ConnectAsync("Agent1");
            await client.WaitForHandshakeAsync();
            await Peer.WaitUntilAsync(() => Sessions.Count == 1);

            var kick = Task.Run(() => Sessions.Kick("127.0.0.1", "race"));
            var leave = client.DisposeAsync().AsTask();
            await Task.WhenAll(kick, leave);

            await Peer.WaitUntilAsync(() => Sessions.Count == 0 && Runner.GetCounts("Agent1") == default(ModuleCounts));
        }
    }

    private async Task<Peer> ConnectAsync(string module)
    {
        var peer = await Peer.ConnectClientAsync(Runner.ListeningEndpoint(module)!, Checksum);
        _cleanup.Add(peer);

        return peer;
    }

    private static Packet LoginRequest(string username)
    {
        var writer = new PacketWriter();
        writer.WriteByte(22);
        writer.WriteAscii(username);
        writer.WriteAscii("secret");
        writer.WriteUInt16(64);

        return writer.ToPacket(Opcodes.GatewayLoginRequest, encrypted: true);
    }

    private static Packet PatchAnswer(string downloadIp, ushort downloadPort)
    {
        var writer = new PacketWriter();
        writer.WriteByte(Opcodes.LoginFailed);
        writer.WriteByte(Opcodes.PatchAvailable);
        writer.WriteAscii(downloadIp);
        writer.WriteUInt16(downloadPort);
        writer.WriteUInt32(188);
        writer.WriteByte(0);

        return writer.ToPacket(Opcodes.GatewayPatchResponse, massive: true);
    }

    private static Packet LoginAnswer(string agentIp, ushort agentPort)
    {
        var writer = new PacketWriter();
        writer.WriteByte(Opcodes.LoginSucceeded);
        writer.WriteUInt32(0xC0FFEE);
        writer.WriteAscii(agentIp);
        writer.WriteUInt16(agentPort);
        writer.WriteByte(0xAA);

        return writer.ToPacket(Opcodes.GatewayLoginResponse);
    }
}
