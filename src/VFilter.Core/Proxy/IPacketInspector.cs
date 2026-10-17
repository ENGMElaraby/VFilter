using VFilter.Core.Modules;
using VFilter.Security;

namespace VFilter.Core.Proxy;

public enum PacketDirection
{
    ClientToModule,
    ModuleToClient,
}

/// <summary>What to do with one application packet.</summary>
public sealed record Inspection(Packet? Forward, IReadOnlyList<Packet> ReplyToClient, string? CloseReason)
{
    public static Inspection Pass(Packet packet) => new(packet, [], null);

    public static Inspection Replace(Packet packet) => new(packet, [], null);

    /// <summary>Drop the packet and answer the client instead (e.g. a refused login).</summary>
    public static Inspection Answer(Packet reply) => new(null, [reply], null);

    /// <summary>Drop the packet and close the session (protocol or security violation).</summary>
    public static Inspection Close(string reason) => new(null, [], reason);
}

/// <summary>
/// Sees every application packet of one module type before it is forwarded (spec 002). Inspectors run on the
/// session's pump, in order; the first one that does not pass the packet unchanged decides.
/// </summary>
public interface IPacketInspector
{
    bool AppliesTo(ModuleType type);

    Inspection Inspect(ProxySession session, PacketDirection direction, Packet packet);
}
