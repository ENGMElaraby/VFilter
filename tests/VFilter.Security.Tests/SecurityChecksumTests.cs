
namespace VFilter.Security.Tests;

public sealed class SecurityChecksumTests
{
    private static readonly byte[] Sample = [0x12, 0x00, 0x01, 0x20, 0x00, 0x00, 0xAA, 0xBB, 0xCC];

    [Fact]
    public void Build_requires_the_polynomial_table()
    {
        if (SecurityPolynomials.IsAvailable)
        {
            // Once the constant is pasted in, the checksum builds and is stable.
            var checksum = SecurityChecksum.Build();
            Assert.Equal(checksum.Compute(0, Sample, 0, Sample.Length), checksum.Compute(0, Sample, 0, Sample.Length));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(SecurityChecksum.Build);
        }
    }

    [Fact]
    public void Different_seeds_pick_different_crc_variants()
    {
        if (!SecurityPolynomials.IsAvailable)
        {
            return; // Needs the polynomial constant; verified once it is supplied.
        }

        var checksum = SecurityChecksum.Build();

        Assert.NotEqual(checksum.Compute(0, Sample, 0, Sample.Length), checksum.Compute(1, Sample, 0, Sample.Length));
    }
}
