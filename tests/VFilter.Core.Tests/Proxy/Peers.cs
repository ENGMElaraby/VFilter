using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using VFilter.Security;

namespace VFilter.Core.Tests.Proxy;

/// <summary>One end of a Silkroad connection for tests: a socket, its security, and the packets it received.</summary>
internal sealed class Peer : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly SilkroadSecurity _security;
    private readonly Channel<Packet> _received = Channel.CreateUnbounded<Packet>();
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public Peer(Socket socket, SilkroadSecurity security, Func<Packet, IEnumerable<Packet>>? respond = null)
    {
        _socket = socket;
        _security = security;
        Respond = respond;
        _loop = Task.Run(ReceiveLoopAsync);
    }

    public Func<Packet, IEnumerable<Packet>>? Respond { get; set; }

    public TaskCompletionSource Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task<Peer> ConnectClientAsync(IPEndPoint endpoint, SecurityChecksum checksum)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, endpoint.Port));

        return new Peer(socket, SilkroadSecurity.CreateClient(checksum));
    }

    public async Task SendAsync(Packet packet)
    {
        _security.Send(packet);
        await FlushAsync();
    }

    public async Task<Packet> ReceiveAsync(TimeSpan? timeout = null) =>
        await _received.Reader.ReadAsync().AsTask().WaitAsync(timeout ?? TimeSpan.FromSeconds(5));

    public bool TryReceive(out Packet packet) => _received.Reader.TryRead(out packet!);

    public Task WaitForHandshakeAsync() => WaitUntilAsync(() => _security.IsHandshakeComplete);

    public async Task WaitDisconnectedAsync() => await Disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition not met within 5 s");
    }

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync();
        _socket.Dispose();
        await _loop;
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[16 * 1024];

        try
        {
            await FlushAsync();

            while (true)
            {
                var read = await _socket.ReceiveAsync(buffer, SocketFlags.None, _stop.Token);

                if (read == 0)
                {
                    break;
                }

                foreach (var packet in _security.Receive(buffer.AsSpan(0, read)))
                {
                    await _received.Writer.WriteAsync(packet);

                    foreach (var reply in Respond?.Invoke(packet) ?? [])
                    {
                        _security.Send(reply);
                    }
                }

                await FlushAsync();
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException or PacketSecurityException)
        {
        }
        finally
        {
            Disconnected.TrySetResult();
        }
    }

    private async Task FlushAsync()
    {
        await _write.WaitAsync();

        try
        {
            foreach (var bytes in _security.TakeOutgoingWrites())
            {
                await _socket.SendAsync(bytes, SocketFlags.None);
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            _write.Release();
        }
    }
}

/// <summary>A fake real module: accepts connections and speaks the server side of the protocol.</summary>
internal sealed class FakeModule : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly SecurityChecksum _checksum;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accept;

    public FakeModule(SecurityChecksum checksum, Func<Packet, IEnumerable<Packet>>? respond = null)
    {
        _checksum = checksum;
        Respond = respond;
        _listener.Start();
        _accept = Task.Run(AcceptLoopAsync);
    }

    public Func<Packet, IEnumerable<Packet>>? Respond { get; }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public Channel<Peer> Connections { get; } = Channel.CreateUnbounded<Peer>();

    public async Task<Peer> NextConnectionAsync() => await Connections.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accept;

        while (Connections.Reader.TryRead(out var peer))
        {
            await peer.DisposeAsync();
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (true)
            {
                var socket = await _listener.AcceptSocketAsync(_stop.Token);
                await Connections.Writer.WriteAsync(new Peer(socket, SilkroadSecurity.CreateServer(SecurityFlags.Standard, _checksum), Respond));
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }
}
