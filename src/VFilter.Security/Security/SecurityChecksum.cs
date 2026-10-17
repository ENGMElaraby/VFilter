// Ported from the owner's MElarabyBot (src/MElarabyBot.Core/Network), written from jMerlin's public
// description of the Silkroad protocol. See specs/002-proxy-core/research.md R1.
namespace VFilter.Security;

/// <summary>
/// The per-packet checksum ("CRC") byte. It uses a 65536-entry lookup built from the 256
/// <see cref="SecurityPolynomials"/> (256 CRC variants × 256 entries); the handshake picks which
/// variant with a seed. Build the table once and reuse it.
/// </summary>
public sealed class SecurityChecksum
{
    private const int VariantSize = 256;
    private readonly uint[] _table;

    private SecurityChecksum(uint[] table) => _table = table;

    /// <summary>Builds the lookup from the polynomial constant.</summary>
    /// <exception cref="InvalidOperationException">The polynomial table has not been supplied.</exception>
    public static SecurityChecksum Build()
    {
        if (!SecurityPolynomials.IsAvailable)
        {
            throw new InvalidOperationException(
                "The security polynomial table is not set; paste it into SecurityPolynomials.Bytes.");
        }

        return FromPolynomials(SecurityPolynomials.Bytes);
    }

    /// <summary>The shared checksum built from <see cref="SecurityPolynomials"/>, or null until that constant is supplied.</summary>
    public static SecurityChecksum? Default => DefaultInstance.Value;

    private static readonly Lazy<SecurityChecksum?> DefaultInstance =
        new(() => SecurityPolynomials.IsAvailable ? Build() : null);

    /// <summary>Builds the lookup from an explicit 1024-byte polynomial table (used by tests).</summary>
    public static SecurityChecksum FromPolynomials(byte[] polynomials)
    {
        ArgumentNullException.ThrowIfNull(polynomials);
        if (polynomials.Length != SecurityPolynomials.RequiredLength)
        {
            throw new ArgumentException(
                $"The polynomial table must be {SecurityPolynomials.RequiredLength} bytes.", nameof(polynomials));
        }

        return new SecurityChecksum(BuildTable(polynomials));
    }

    /// <summary>The checksum byte for a packet, using the CRC variant chosen by <paramref name="seed"/>.</summary>
    public byte Compute(uint seed, byte[] data, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(data);

        var checksum = 0xFFFFFFFFu;
        var baseIndex = seed << 8;
        for (var i = offset; i < offset + length; i++)
        {
            checksum = (checksum >> 8) ^ _table[baseIndex + ((data[i] ^ checksum) & 0xFF)];
        }

        return (byte)(((checksum >> 24) & 0xFF) + ((checksum >> 16) & 0xFF) + ((checksum >> 8) & 0xFF) + (checksum & 0xFF));
    }

    // For each of the 256 polynomials, generate a 256-entry reflected-CRC row.
    private static uint[] BuildTable(byte[] polynomials)
    {
        var table = new uint[VariantSize * VariantSize];
        var index = 0;
        for (var p = 0; p < polynomials.Length; p += sizeof(uint))
        {
            var polynomial = BitConverter.ToUInt32(polynomials, p);
            for (uint entry = 0; entry < VariantSize; entry++)
            {
                var value = entry >> 1;
                if ((entry & 1) != 0)
                {
                    value ^= polynomial;
                }

                for (var bit = 0; bit < 7; bit++)
                {
                    if ((value & 1) != 0)
                    {
                        value = (value >> 1) ^ polynomial;
                    }
                    else
                    {
                        value >>= 1;
                    }
                }

                table[index++] = value;
            }
        }

        return table;
    }
}
