// Ported from the owner's MElarabyBot (src/MElarabyBot.Core/Network), written from jMerlin's public
// description of the Silkroad protocol. See specs/002-proxy-core/research.md R1.
namespace VFilter.Security;

/// <summary>
/// One Silkroad message: an opcode and its payload. Immutable; build the payload with a
/// <see cref="PacketWriter"/> and read it with <see cref="PacketReader"/>.
/// </summary>
/// <param name="Encrypted">Whether the body is sent Blowfish-encrypted on the wire.</param>
/// <param name="Massive">Part of a "massive" (6-byte-header-batched) message group.</param>
public sealed record Packet(ushort Opcode, byte[] Payload, bool Encrypted = false, bool Massive = false)
{
    public static Packet Empty(ushort opcode, bool encrypted = false) => new(opcode, [], encrypted);

    public PacketReader OpenReader() => new(Payload);

    /// <summary>A short label for the log, e.g. <c>0x2001 (12 bytes)</c>.</summary>
    public string Describe() => $"0x{Opcode:X4} ({Payload.Length} bytes)";
}
