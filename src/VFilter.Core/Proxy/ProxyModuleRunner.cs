using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VFilter.Core.Logging;
using VFilter.Core.Modules;
using VFilter.Core.Sessions;
using VFilter.Security;

namespace VFilter.Core.Proxy;

/// <summary>
/// Opens a guard listener per started module and proxies each client to the real module (spec 002 US1).
/// Replaces <see cref="StandbyModuleRunner"/>.
/// </summary>
public sealed partial class ProxyModuleRunner(
    IEnumerable<IPacketInspector> inspectors,
    SessionRegistry registry,
    FilterLog filterLog,
    IOptions<ProxyOptions> options,
    TimeProvider time,
    ILogger<ProxyModuleRunner> logger) : IModuleRunner, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Listener> _listeners = new(StringComparer.OrdinalIgnoreCase);
    private readonly SecurityChecksum _checksum = SecurityChecksum.Build();

    private sealed record Listener(ModuleConfig Config, Socket Socket, CancellationTokenSource Stop, ConcurrentDictionary<ProxySession, byte> Sessions)
    {
        public Task AcceptLoop { get; set; } = Task.CompletedTask;
    }

    /// <summary>The local endpoint a module listens on (tests bind port 0 and read the real port here).</summary>
    public IPEndPoint? ListeningEndpoint(string moduleName) =>
        _listeners.TryGetValue(moduleName, out var listener) ? listener.Socket.LocalEndPoint as IPEndPoint : null;

    public Task StartAsync(ModuleConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { DualMode = true };

        try
        {
            socket.Bind(new IPEndPoint(BindAddress(config.GuardIp), config.GuardPort));
            socket.Listen(backlog: 512);
        }
        catch
        {
            socket.Dispose();

            throw;
        }

        var listener = new Listener(config, socket, new CancellationTokenSource(), new ConcurrentDictionary<ProxySession, byte>());
        _listeners[config.Name] = listener;
        listener.AcceptLoop = Task.Run(() => AcceptLoopAsync(listener), CancellationToken.None);
        LogListening(config.Name, socket.LocalEndPoint);

        return Task.CompletedTask;
    }

    public async Task StopAsync(ModuleConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!_listeners.TryRemove(config.Name, out var listener))
        {
            return;
        }

        await listener.Stop.CancelAsync().ConfigureAwait(false);
        listener.Socket.Dispose();

        foreach (var session in listener.Sessions.Keys)
        {
            await session.CloseAsync("module stopped").ConfigureAwait(false);
        }

        await listener.AcceptLoop.ConfigureAwait(false);
        listener.Stop.Dispose();
    }

    public ModuleCounts GetCounts(string moduleName)
    {
        if (!_listeners.TryGetValue(moduleName, out var listener))
        {
            return default;
        }

        var sessions = listener.Sessions.Keys.ToList();

        return new ModuleCounts(sessions.Count, sessions.Count(s => s.Live.Charname is not null));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var listener in _listeners.Values.ToList())
        {
            await StopAsync(listener.Config, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task AcceptLoopAsync(Listener listener)
    {
        var token = listener.Stop.Token;

        while (!token.IsCancellationRequested)
        {
            Socket client;

            try
            {
                client = await listener.Socket.AcceptAsync(token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(listener, client, token), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(Listener listener, Socket client, CancellationToken token)
    {
        var module = await ConnectModuleAsync(listener.Config, client, token).ConfigureAwait(false);

        if (module is null)
        {
            client.Dispose();

            return;
        }

        var applicable = inspectors.Where(i => i.AppliesTo(listener.Config.Type)).ToList();
        await using var session = new ProxySession(client, module, listener.Config, _checksum, applicable, options.Value, registry, filterLog, time);
        listener.Sessions[session] = 0;

        try
        {
            await session.RunAsync(token).ConfigureAwait(false);
        }
        finally
        {
            listener.Sessions.TryRemove(session, out _);
        }
    }

    private async Task<Socket?> ConnectModuleAsync(ModuleConfig config, Socket client, CancellationToken token)
    {
        var module = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        client.NoDelay = true;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(options.Value.ModuleConnectTimeout);
            await module.ConnectAsync(config.ModuleIp, config.ModulePort, timeout.Token).ConfigureAwait(false);

            return module;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            module.Dispose();
            var ip = (client.RemoteEndPoint as IPEndPoint)?.Address.ToString();
            filterLog.Write(FilterLogLevel.Error, "connection", $"real {config.Name} unreachable: {exception.Message}", config.Name, ip);

            return null;
        }
    }

    // Bind the guard IP when it is an address of this machine; otherwise (public IP behind NAT, or a host
    // name) listen on every interface. Players are only ever told the guard IP, never the bind address.
    private static IPAddress BindAddress(string guardIp)
    {
        if (!IPAddress.TryParse(guardIp, out var address))
        {
            return IPAddress.IPv6Any;
        }

        var local = NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Any(u => u.Address.Equals(address));

        return local || IPAddress.IsLoopback(address)
            ? (address.AddressFamily == AddressFamily.InterNetwork ? address.MapToIPv6() : address)
            : IPAddress.IPv6Any;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Module} listening on {Endpoint}")]
    private partial void LogListening(string module, EndPoint? endpoint);
}
