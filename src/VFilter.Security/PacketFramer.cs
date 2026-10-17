// Ported from the owner's MElarabyBot (src/MElarabyBot.Core/Network), written from jMerlin's public
// description of the Silkroad protocol. See specs/002-proxy-core/research.md R1.
using System.Buffers.Binary;

namespace VFilter.Security;

/// <summary>
/// Splits a TCP byte stream into whole packets. TCP delivers bytes in arbitrary chunks, so bytes are
/// buffered until a full frame is present. The frame is
/// <c>[size:2][opcode:2][count:1][crc:1][payload:size]</c>, all little-endian; the size counts only the
/// payload, and its high bit (0x8000) marks an encrypted body. An encrypted frame is Blowfish-encrypted
/// from the opcode onwards and padded to 8-byte blocks, so it occupies <c>2 + pad8(4 + size)</c> bytes.
/// Feed bytes to <see cref="Receive"/> and pull whole frames from what it returns.
/// </summary>
public sealed class PacketFramer
{
    public const int HeaderSize = 6;
    public const int SizeFieldLength = 2;
    public const int BlockSize = 8;
    private const int SizeMask = 0x7FFF;
    private const int EncryptedFlag = 0x8000;

    private readonly int _maxPayload;
    private byte[] _buffer = [];

    /// <param name="maxPayload">A frame larger than this aborts the stream; it means the peer is out of sync or hostile.</param>
    public PacketFramer(int maxPayload = 0x7FFF)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPayload);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPayload, SizeMask);
        _maxPayload = maxPayload;
    }

    /// <summary>Adds received bytes and returns every whole frame they complete, in order.</summary>
    /// <exception cref="PacketFramingException">A frame claims a payload larger than the limit.</exception>
    public IReadOnlyList<FramedPacket> Receive(ReadOnlySpan<byte> incoming)
    {
        Append(incoming);

        var frames = new List<FramedPacket>();
        var offset = 0;
        while (TryReadFrame(offset, out var frame, out var consumed))
        {
            frames.Add(frame);
            offset += consumed;
        }

        // Keep only the tail that did not form a complete frame.
        _buffer = _buffer.AsSpan(offset).ToArray();
        return frames;
    }

    private bool TryReadFrame(int offset, out FramedPacket frame, out int consumed)
    {
        frame = default;
        consumed = 0;

        if (_buffer.Length - offset < HeaderSize)
        {
            return false;
        }

        var header = _buffer.AsSpan(offset);
        var sizeField = BinaryPrimitives.ReadUInt16LittleEndian(header);
        var payloadLength = sizeField & SizeMask;

        if (payloadLength > _maxPayload)
        {
            throw new PacketFramingException(payloadLength, _maxPayload);
        }

        var encrypted = (sizeField & EncryptedFlag) != 0;
        var total = WireLength(payloadLength, encrypted);
        if (_buffer.Length - offset < total)
        {
            return false;
        }

        // For an encrypted frame the header fields and payload are still ciphertext (the payload includes
        // the block padding); the security layer decrypts Wire and re-reads them.
        frame = new FramedPacket(
            Opcode: BinaryPrimitives.ReadUInt16LittleEndian(header[2..]),
            Count: header[4],
            Crc: header[5],
            Payload: header.Slice(HeaderSize, total - HeaderSize).ToArray(),
            EncryptedSize: encrypted,
            Wire: header[..total].ToArray());
        consumed = total;
        return true;
    }

    /// <summary>The number of bytes a frame with this payload length occupies on the wire.</summary>
    public static int WireLength(int payloadLength, bool encrypted) =>
        encrypted ? SizeFieldLength + PadToBlock(payloadLength + HeaderSize - SizeFieldLength) : HeaderSize + payloadLength;

    /// <summary>Rounds a length up to a whole number of 8-byte Blowfish blocks.</summary>
    public static int PadToBlock(int length) => (length + BlockSize - 1) / BlockSize * BlockSize;

    private void Append(ReadOnlySpan<byte> incoming)
    {
        if (incoming.IsEmpty)
        {
            return;
        }

        var combined = new byte[_buffer.Length + incoming.Length];
        _buffer.CopyTo(combined, 0);
        incoming.CopyTo(combined.AsSpan(_buffer.Length));
        _buffer = combined;
    }
}

public sealed class PacketFramingException(int payloadLength, int max)
    : Exception($"A packet claims a {payloadLength}-byte payload, over the {max}-byte limit; the stream is out of sync.");
