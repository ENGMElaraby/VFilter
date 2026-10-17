
namespace VFilter.Security.Tests;

public sealed class SilkroadBlowfishTests
{
    private static readonly byte[] Key = [0x32, 0xCE, 0xDD, 0x7C, 0xBC, 0xA8, 0x2E, 0x3B];

    [Fact]
    public void Decrypt_reverses_encrypt()
    {
        var blowfish = new SilkroadBlowfish(Key);
        var plain = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        var roundTripped = blowfish.Decrypt(blowfish.Encrypt(plain));

        Assert.Equal(plain, roundTripped);
    }

    [Fact]
    public void Encrypt_changes_the_data()
    {
        var blowfish = new SilkroadBlowfish(Key);
        var plain = new byte[8];

        Assert.NotEqual(plain, blowfish.Encrypt(plain));
    }

    [Fact]
    public void Input_must_be_a_multiple_of_the_block_size()
    {
        var blowfish = new SilkroadBlowfish(Key);

        Assert.Throws<ArgumentException>(() => blowfish.Decrypt(new byte[7]));
    }
}
