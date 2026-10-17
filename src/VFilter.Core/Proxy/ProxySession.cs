using System.Net;
using System.Net.Sockets;
using VFilter.Core.Logging;
using VFilter.Core.Modules;
using VFilter.Core.Sessions;
using VFilter.Security;

namespace VFilter.Core.Proxy;

/// <summary>
/// One player connection: the client socket and its own connection to the real module, each with its own
/// Silkroad security (server role toward the client, client role toward the module). Every application
/// packet passes the inspectors and is re-encoded for the other side, in order (spec 002 US1).
/// Pattern adapted from the owner's MElarabyBot SecureRelay.
/// </summary>
public sealed class ProxySession : IAsyncDisposable
{
    private const int BufferSize = 16 * 1024;

    private readonly Socket _client;
    private readonly Socket _module;
    private readonly SilkroadSecurity _clientSide;
    private readonly SilkroadSecurity _moduleSide;
    private readonly IReadOnlyList<IPacketInspector> _inspectors;
    private readonly ProxyOptions _options;
    private readonly FilterLog _log;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _clientWrite = new(1, 1);
    private readonly SemaphoreSlim _moduleWrite = new(1, 1);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Lock _closeGate = new();
    private Task? _closing;
    private long _lastClientActivity;
    private int _closed;

    public ProxySession(
        Socket client,
        Socket module,
        ModuleConfig config,
        SecurityChecksum checksum,
        IReadOnlyList<IPacketInspector> inspectors,
        ProxyOptions options,
        SessionRegistry registry,
        FilterLog log,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(registry);
        _client = client;
        _module = module;
        Config = config;
        _inspectors = inspectors;
        _options = options;
        _log = log;
        _time = time;
        _clientSide = SilkroadSecurity.CreateServer(SecurityFlags.Standard, checksum);
        _moduleSide = SilkroadSecurity.CreateClient(checksum);
        ClientAddress = (client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
        Registry = registry;
        Live = registry.Open(config.Name, ClientAddress.ToString(), reason => _ = CloseAsync($"kicked: {reason}"));
        _lastClientActivity = time.GetTimestamp();
    }

    public ModuleConfig Config { get; }

    public IPAddress ClientAddress { get; }

    public LiveSession Live { get; }

    public SessionRegistry Registry { get; }

    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    /// <summary>Why the session ended (null while open or for an ordinary disconnect).</summary>
    public string? CloseReason { get; private set; }

    /// <summary>Username the client asked to log in with; confirmed only by the server's success answer.</summary>
    public string? PendingUsername { get; set; }

    /// <summary>Character the client selected; confirmed only by the server's success answer.</summary>
    public string? PendingCharname { get; set; }

    /// <summary>Raised once when the session has closed.</summary>
    public event Action<ProxySession>? Closed;

    /// <summary>Runs both directions until either side ends. Never throws.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation.Token);
        var token = linked.Token;

        try
        {
            await FlushAsync(_clientSide, _client, _clientWrite, token).ConfigureAwait(false);
            var fromClient = PumpAsync(_client, _clientSide, PacketDirection.ClientToModule, token);
            var fromModule = PumpAsync(_module, _moduleSide, PacketDirection.ModuleToClient, token);
            var watchdog = WatchAsync(token);
            var first = await Task.WhenAny(fromClient, fromModule, watchdog).ConfigureAwait(false);
            var reason = await first.ConfigureAwait(false);
            await CloseAsync(reason).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsDisconnect(exception))
        {
            await CloseAsync(null).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes both sockets. Every caller (pumps, kick, stop, dispose) shares one close task, so a kick racing a
    /// disconnect can never leave half a session behind. A non-null reason is logged.
    /// </summary>
    public Task CloseAsync(string? reason)
    {
        lock (_closeGate)
        {
            return _closing ??= Task.Run(() => CloseCore(reason));
        }
    }

    private void CloseCore(string? reason)
    {
        Volatile.Write(ref _closed, 1);
        CloseReason = reason;
        _cancellation.Cancel();
        _client.Dispose();
        _module.Dispose();
        Registry.Close(Live);

        if (reason is not null)
        {
            var security = reason.StartsWith("security:", StringComparison.Ordinal);
            Log(security ? FilterLogLevel.Security : FilterLogLevel.Info, security ? "exploit" : "connection", $"session closed: {reason}");
        }

        Closed?.Invoke(this);
    }

    /// <summary>Sends a packet the filter made itself to the client.</summary>
    public Task SendToClientAsync(Packet packet)
    {
        _clientSide.Send(packet);

        return FlushAsync(_clientSide, _client, _clientWrite, _cancellation.Token);
    }

    public void Log(FilterLogLevel level, string category, string message, string? opcode = null) =>
        _log.Write(level, category, message, Config.Name, ClientAddress.ToString(), Live.Username, Live.Charname, opcode);

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(null).ConfigureAwait(false);
        _cancellation.Dispose();
        _clientWrite.Dispose();
        _moduleWrite.Dispose();
    }

    // Returns why the pump ended: null for an ordinary disconnect. Never throws: anything unexpected that a
    // client can trigger ends only this session, with a log entry.
    private async Task<string?> PumpAsync(Socket from, SilkroadSecurity security, PacketDirection direction, CancellationToken token)
    {
        var buffer = new byte[BufferSize];

        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await from.ReceiveAsync(buffer, SocketFlags.None, token).ConfigureAwait(false);

                if (read == 0)
                {
                    return direction == PacketDirection.ModuleToClient ? "module closed the connection" : null;
                }

                if (direction == PacketDirection.ClientToModule)
                {
                    Interlocked.Exchange(ref _lastClientActivity, _time.GetTimestamp());
                    Registry.Touch(Live);
                }

                var packets = security.Receive(buffer.AsSpan(0, read));
                var closeReason = Forward(packets, direction);

                if (closeReason is not null)
                {
                    return closeReason;
                }

                await FlushAsync(_clientSide, _client, _clientWrite, token).ConfigureAwait(false);
                await FlushAsync(_moduleSide, _module, _moduleWrite, token).ConfigureAwait(false);
            }

            return null;
        }
        catch (Exception exception) when (exception is PacketSecurityException or PacketFramingException or EndOfPacketException)
        {
            return direction == PacketDirection.ClientToModule
                ? $"security: {exception.Message}"
                : $"module protocol error: {exception.Message}";
        }
        catch (Exception exception) when (IsDisconnect(exception))
        {
            return null;
        }
        catch (Exception exception)
        {
            return direction == PacketDirection.ClientToModule
                ? $"security: unexpected client packet ({exception.GetType().Name}: {exception.Message})"
                : $"internal: {exception.GetType().Name}: {exception.Message}";
        }
    }

    private string? Forward(IReadOnlyList<Packet> packets, PacketDirection direction)
    {
        var target = direction == PacketDirection.ClientToModule ? _moduleSide : _clientSide;

        foreach (var packet in packets)
        {
            var result = Inspect(direction, packet);

            if (result.CloseReason is not null)
            {
                return result.CloseReason;
            }

            foreach (var reply in result.ReplyToClient)
            {
                _clientSide.Send(reply);
            }

            if (result.Forward is { } forward)
            {
                target.Send(forward);
            }
        }

        return null;
    }

    // Every inspector sees the packet as the previous one left it, so a later check (e.g. the gateway mode gate
    // or the address rewrite) is never skipped because an earlier inspector changed the packet.
    private Inspection Inspect(PacketDirection direction, Packet packet)
    {
        Packet? current = packet;
        var replies = new List<Packet>();

        foreach (var inspector in _inspectors)
        {
            var result = inspector.Inspect(this, direction, current);
            replies.AddRange(result.ReplyToClient);

            if (result.CloseReason is not null)
            {
                return Inspection.Close(result.CloseReason);
            }

            current = result.Forward;

            if (current is null)
            {
                break;
            }
        }

        return new Inspection(current, replies, null);
    }

    private async Task<string?> WatchAsync(CancellationToken token)
    {
        var started = _time.GetTimestamp();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _time);

        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            if (!_clientSide.IsHandshakeComplete && _time.GetElapsedTime(started) > _options.HandshakeTimeout)
            {
                return "security: handshake not completed in time";
            }

            // A module that never completes its handshake would otherwise let client packets pile up unsent.
            if (!_moduleSide.IsHandshakeComplete && _time.GetElapsedTime(started) > _options.HandshakeTimeout)
            {
                return "module handshake not completed in time";
            }

            if (_time.GetElapsedTime(Interlocked.Read(ref _lastClientActivity)) > _options.IdleTimeout)
            {
                return "idle timeout";
            }
        }

        return null;
    }

    // Takes and writes under one lock so a side's packets are never reordered. A peer that cannot take
    // MaxPendingBytes, or blocks a write for WriteTimeout, is disconnected instead of growing memory.
    private async Task FlushAsync(SilkroadSecurity security, Socket socket, SemaphoreSlim gate, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);

        try
        {
            // Counts packets still held for the handshake too, so nothing can pile up unbounded.
            if (security.PendingBytes > _options.MaxPendingBytes)
            {
                _ = CloseAsync("output buffer limit exceeded");

                return;
            }

            var writes = security.TakeOutgoingWrites();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(_options.WriteTimeout);

            foreach (var bytes in writes)
            {
                var sent = 0;

                while (sent < bytes.Length)
                {
                    sent += await socket.SendAsync(bytes.AsMemory(sent), SocketFlags.None, timeout.Token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsDisconnect(Exception exception) =>
        exception is OperationCanceledException or SocketException or ObjectDisposedException;
}
