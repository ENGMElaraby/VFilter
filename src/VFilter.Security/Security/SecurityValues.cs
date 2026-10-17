// Ported from the owner's MElarabyBot (src/MElarabyBot.Core/Network), written from jMerlin's public
// description of the Silkroad protocol. See specs/002-proxy-core/research.md R1.
namespace VFilter.Security;

/// <summary>
/// The arithmetic building blocks of Silkroad's packet security (jMerlin's public "security bytes for
/// SRO" algorithms): the value generator, the Diffie-Hellman modular power, and the key-mixing transform.
/// </summary>
public static class SecurityValues
{
    /// <summary>Advances a 32-bit value through 32 rounds of the security LFSR and returns it.</summary>
    public static uint Generate(ref uint value)
    {
        for (var i = 0; i < 32; i++)
        {
            var low = (((((((((((value >> 2) ^ value) >> 2) ^ value) >> 1) ^ value) >> 1) ^ value) >> 1) ^ value) & 1);
            value = low | ((((value & 1) << 31) | (value >> 1)) & 0xFFFFFFFE);
        }

        return value;
    }

    /// <summary>Computes <c>(g ^ x) mod p</c> for the handshake key exchange.</summary>
    public static uint ModPow(uint g, uint x, uint p)
    {
        if (x == 0)
        {
            return 1;
        }

        long result = 1;
        long factor = g;
        while (x != 0)
        {
            if ((x & 1) != 0)
            {
                result = result * factor % p;
            }

            x >>= 1;
            factor = factor * factor % p;
        }

        return (uint)result;
    }

    /// <summary>Mixes a 64-bit handshake value with a 32-bit key and a key byte (jMerlin's Func_X_2).</summary>
    public static ulong KeyTransform(ulong value, uint key, byte keyByte)
    {
        var bytes = BitConverter.GetBytes(value);
        var keyBytes = BitConverter.GetBytes(key);
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] ^= (byte)(bytes[i] + keyBytes[i % keyBytes.Length] + keyByte);
        }

        return BitConverter.ToUInt64(bytes, 0);
    }
}
