// Ported from the owner's MElarabyBot (src/MElarabyBot.Core/Network), written from jMerlin's public
// description of the Silkroad protocol. See specs/002-proxy-core/research.md R1.
using System.Buffers.Binary;
using System.Text;

namespace VFilter.Security;

/// <summary>Builds a packet payload. Silkroad is little-endian; strings are length-prefixed.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "MemoryStream holds no unmanaged resources.")]
public sealed class PacketWriter
{
    private readonly MemoryStream _buffer = new();

    public int Length => (int)_buffer.Length;

    public void WriteByte(byte value) => _buffer.WriteByte(value);

    public void WriteSByte(sbyte value) => _buffer.WriteByte((byte)value);

    public void WriteBool(bool value) => _buffer.WriteByte(value ? (byte)1 : (byte)0);

    public void WriteUInt16(ushort value)
    {
        Span<byte> span = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(span, value);
        _buffer.Write(span);
    }

    public void WriteInt16(short value)
    {
        Span<byte> span = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(span, value);
        _buffer.Write(span);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> span = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        _buffer.Write(span);
    }

    public void WriteInt32(int value)
    {
        Span<byte> span = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(span, value);
        _buffer.Write(span);
    }

    public void WriteUInt64(ulong value)
    {
        Span<byte> span = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        _buffer.Write(span);
    }

    public void WriteInt64(long value)
    {
        Span<byte> span = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(span, value);
        _buffer.Write(span);
    }

    public void WriteBytes(ReadOnlySpan<byte> value) => _buffer.Write(value);

    /// <summary>A 2-byte length followed by ASCII bytes (the common Silkroad string).</summary>
    public void WriteAscii(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var bytes = Encoding.ASCII.GetBytes(value);
        WriteLength(bytes.Length);
        _buffer.Write(bytes);
    }

    /// <summary>A 2-byte character count followed by UTF-16 little-endian text.</summary>
    public void WriteUnicode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        WriteLength(value.Length);
        _buffer.Write(Encoding.Unicode.GetBytes(value));
    }

    public byte[] ToArray() => _buffer.ToArray();

    public Packet ToPacket(ushort opcode, bool encrypted = false, bool massive = false) =>
        new(opcode, ToArray(), encrypted, massive);

    private void WriteLength(int length)
    {
        if (length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "A Silkroad string is at most 65535 long.");
        }

        WriteUInt16((ushort)length);
    }
}
