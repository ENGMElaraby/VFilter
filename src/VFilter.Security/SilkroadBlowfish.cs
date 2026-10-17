// Ported from the owner's MElarabyBot (src/MElarabyBot.Core/Network), written from jMerlin's public
// description of the Silkroad protocol. See specs/002-proxy-core/research.md R1.
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace VFilter.Security;

/// <summary>
/// Blowfish as Silkroad uses it: the standard cipher, but each 8-byte block is split into two 32-bit
/// halves read little-endian, where textbook Blowfish reads them big-endian. We reverse each half around
/// BouncyCastle's engine to match. ECB. Used for PK2 directory blocks and for packet bodies once the
/// network handshake has agreed a key. The key schedule is computed once per instance.
/// </summary>
public sealed class SilkroadBlowfish
{
    private const int BlockSize = 8;
    private const int HalfSize = 4;

    private readonly BlowfishEngine _encryptor = new();
    private readonly BlowfishEngine _decryptor = new();

    public SilkroadBlowfish(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _encryptor.Init(true, new KeyParameter(key));
        _decryptor.Init(false, new KeyParameter(key));
    }

    /// <summary>Decrypts a buffer whose length is a multiple of 8. Returns a new array.</summary>
    public byte[] Decrypt(byte[] data) => Process(data, forEncryption: false);

    /// <summary>Encrypts a buffer whose length is a multiple of 8. Returns a new array.</summary>
    public byte[] Encrypt(byte[] data) => Process(data, forEncryption: true);

    /// <summary>Encrypts <paramref name="data"/> in place; its length must be a multiple of 8.</summary>
    public void EncryptInPlace(Span<byte> data) => ProcessInPlace(data, _encryptor);

    /// <summary>Decrypts <paramref name="data"/> in place; its length must be a multiple of 8.</summary>
    public void DecryptInPlace(Span<byte> data) => ProcessInPlace(data, _decryptor);

    private byte[] Process(byte[] data, bool forEncryption)
    {
        ArgumentNullException.ThrowIfNull(data);

        var result = (byte[])data.Clone();
        ProcessInPlace(result, forEncryption ? _encryptor : _decryptor);
        return result;
    }

    private static void ProcessInPlace(Span<byte> data, BlowfishEngine engine)
    {
        if (data.Length % BlockSize != 0)
        {
            throw new ArgumentException("Blowfish input must be a multiple of 8 bytes.", nameof(data));
        }

        Span<byte> input = stackalloc byte[BlockSize];
        Span<byte> output = stackalloc byte[BlockSize];

        for (var offset = 0; offset < data.Length; offset += BlockSize)
        {
            var block = data.Slice(offset, BlockSize);
            SwapHalves(block, input);
            engine.ProcessBlock(input, output);
            SwapHalves(output, block);
        }
    }

    // Reverses the bytes within each 4-byte half, converting between the client's little-endian
    // block layout and the big-endian layout the engine expects.
    private static void SwapHalves(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        for (var half = 0; half < BlockSize; half += HalfSize)
        {
            for (var i = 0; i < HalfSize; i++)
            {
                destination[half + i] = source[half + (HalfSize - 1 - i)];
            }
        }
    }
}
