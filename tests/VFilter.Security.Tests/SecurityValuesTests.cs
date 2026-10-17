
namespace VFilter.Security.Tests;

public sealed class SecurityValuesTests
{
    [Fact]
    public void Generate_is_deterministic_for_the_same_seed()
    {
        var a = 0x12345678u;
        var b = 0x12345678u;

        Assert.Equal(SecurityValues.Generate(ref a), SecurityValues.Generate(ref b));
    }

    [Fact]
    public void Generate_advances_the_value()
    {
        var value = 0x9ABFB3B6u;

        Assert.NotEqual(0x9ABFB3B6u, SecurityValues.Generate(ref value));
    }

    [Theory]
    [InlineData(3u, 4u, 7u, 4u)]      // 3^4 = 81, 81 mod 7 = 4
    [InlineData(2u, 10u, 1000u, 24u)] // 1024 mod 1000 = 24
    [InlineData(5u, 0u, 97u, 1u)]     // anything^0 = 1
    [InlineData(7u, 1u, 97u, 7u)]
    public void ModPow_matches_hand_computed_values(uint g, uint x, uint p, uint expected)
    {
        Assert.Equal(expected, SecurityValues.ModPow(g, x, p));
    }

    [Fact]
    public void KeyTransform_is_deterministic()
    {
        var first = SecurityValues.KeyTransform(0x1122334455667788, 0xDEADBEEF, 0x03);
        var second = SecurityValues.KeyTransform(0x1122334455667788, 0xDEADBEEF, 0x03);

        Assert.Equal(first, second);
    }

    [Fact]
    public void KeyTransform_depends_on_the_key_byte()
    {
        var a = SecurityValues.KeyTransform(0x1122334455667788, 0xDEADBEEF, 0x03);
        var b = SecurityValues.KeyTransform(0x1122334455667788, 0xDEADBEEF, 0x04);

        Assert.NotEqual(a, b);
    }
}
