// Ported from the owner's MElarabyBot (src/MElarabyBot.Core/Network), written from jMerlin's public
// description of the Silkroad protocol. See specs/002-proxy-core/research.md R1.
using System.Buffers.Binary;
using System.Text;

namespace VFilter.Security;

/// <summary>
/// Reads a packet payload. Packets come from the network, so a reader is untrusted input:
/// every read past the end throws <see cref="EndOfPacketException"/> instead of returning garbage.
/// </summary>
public sealed class PacketReader
{
    private readonly byte[] _data;
    private int _position;

    public PacketReader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
    }

    public int Remaining => _data.Length - _position;

    public byte ReadByte() => Take(1)[0];

    /// <summary>The next byte without consuming it.</summary>
    public byte Peek()
    {
        var value = Take(1)[0];
        _position--;
        return value;
    }

    public sbyte ReadSByte() => (sbyte)ReadByte();

    public bool ReadBool() => ReadByte() != 0;

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(sizeof(ushort)));

    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Take(sizeof(short)));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(sizeof(uint)));

    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)));

    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(sizeof(ulong)));

    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(sizeof(long)));

    public float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(Take(sizeof(float)));

    public byte[] ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return Take(count).ToArray();
    }

    public string ReadAscii() => Encoding.ASCII.GetString(Take(ReadUInt16()));

    public string ReadUnicode() => Encoding.Unicode.GetString(Take(ReadUInt16() * 2));

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count > Remaining)
        {
            throw new EndOfPacketException(count, Remaining);
        }

        var span = _data.AsSpan(_position, count);
        _position += count;
        return span;
    }
}

public sealed class EndOfPacketException(int requested, int available)
    : Exception($"Tried to read {requested} bytes but only {available} remain in the packet.");
