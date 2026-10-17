// Ported from the owner's MElarabyBot (src/MElarabyBot.Core/Network), written from jMerlin's public
// description of the Silkroad protocol. See specs/002-proxy-core/research.md R1.
namespace VFilter.Security;

/// <summary>
/// A packet as it sits on the wire, before the security layer checks the count and CRC. When
/// <see cref="EncryptedSize"/> is set, every field but the size is still ciphertext; decode <see cref="Wire"/>.
/// </summary>
/// <param name="EncryptedSize">True when the wire size field had its high bit set, marking an encrypted body.</param>
/// <param name="Wire">The whole frame exactly as received, size field included.</param>
public readonly record struct FramedPacket(ushort Opcode, byte Count, byte Crc, byte[] Payload, bool EncryptedSize, byte[]? Wire = null)
{
    public Packet ToPacket() => new(Opcode, Payload);
}
