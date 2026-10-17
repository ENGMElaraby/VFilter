// Ported from the owner's MElarabyBot (src/MElarabyBot.Core/Network), written from jMerlin's public
// description of the Silkroad protocol. See specs/002-proxy-core/research.md R1.
namespace VFilter.Security;

/// <summary>
/// Produces the per-packet "count" byte both sides use to prove packets arrive in order. Seeded from a
/// value in the handshake; each side runs an identical generator and compares.
/// </summary>
public sealed class CountByteGenerator
{
    private const uint FallbackSeed = 0x9ABFB3B6;

    private byte _seed0;
    private byte _seed1;
    private byte _seed2;

    public CountByteGenerator(uint seed)
    {
        if (seed == 0)
        {
            seed = FallbackSeed;
        }

        var value = seed;
        var v0 = SecurityValues.Generate(ref value);
        var v1 = SecurityValues.Generate(ref value);
        var v2 = SecurityValues.Generate(ref value);
        SecurityValues.Generate(ref value);

        var byte1 = (byte)((value & 0xFF) ^ (v2 & 0xFF));
        var byte2 = (byte)((v0 & 0xFF) ^ (v1 & 0xFF));
        if (byte1 == 0)
        {
            byte1 = 1;
        }

        if (byte2 == 0)
        {
            byte2 = 1;
        }

        _seed0 = (byte)(byte1 ^ byte2);
        _seed1 = byte2;
        _seed2 = byte1;
    }

    /// <summary>Returns the next count byte and advances the generator.</summary>
    public byte Next()
    {
        var result = (byte)(_seed2 * (~_seed0 + _seed1));
        result = (byte)(result ^ (result >> 4));
        _seed0 = result;
        return result;
    }

    /// <summary>Returns the next count byte without advancing, to check a received packet.</summary>
    public byte Peek()
    {
        var result = (byte)(_seed2 * (~_seed0 + _seed1));
        return (byte)(result ^ (result >> 4));
    }
}
