// Ported from the owner's MElarabyBot (src/MElarabyBot.Core/Network), written from jMerlin's public
// description of the Silkroad protocol. See specs/002-proxy-core/research.md R1.
namespace VFilter.Security;

/// <summary>
/// The options byte at the start of a 0x5000 handshake packet. The server picks them; the client follows.
/// </summary>
[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "Named after the protocol's options byte.")]
public enum SecurityFlags : byte
{
    /// <summary>No security at all.</summary>
    None = 0x01,

    /// <summary>Packet bodies may be Blowfish-encrypted.</summary>
    Blowfish = 0x02,

    /// <summary>The client adds a count byte and a checksum byte to every packet it sends.</summary>
    SecurityBytes = 0x04,

    /// <summary>A Diffie-Hellman exchange agrees the final Blowfish key.</summary>
    Handshake = 0x08,

    /// <summary>The server's second 0x5000, carrying its half of the challenge.</summary>
    HandshakeResponse = 0x10,

    /// <summary>What vSRO servers use: all three protections.</summary>
    Standard = Blowfish | SecurityBytes | Handshake,
}

/// <summary>Which end of the connection a <see cref="SilkroadSecurity"/> plays.</summary>
public enum SecurityRole
{
    /// <summary>We connect to a server and follow its handshake (the bot towards the real server).</summary>
    Client,

    /// <summary>We accept a client and lead the handshake (the bot towards the real game client).</summary>
    Server,
}

/// <summary>Opcodes the security layer handles itself.</summary>
public static class SecurityOpcodes
{
    public const ushort Handshake = 0x5000;
    public const ushort HandshakeAccept = 0x9000;
    public const ushort Massive = 0x600D;
}

/// <summary>A packet failed a security check: bad count or checksum, a failed challenge, or a protocol violation.</summary>
public sealed class PacketSecurityException(string message) : Exception(message);
