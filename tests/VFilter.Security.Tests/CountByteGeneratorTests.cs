
namespace VFilter.Security.Tests;

public sealed class CountByteGeneratorTests
{
    [Fact]
    public void The_same_seed_gives_the_same_sequence()
    {
        var a = new CountByteGenerator(0x12345678);
        var b = new CountByteGenerator(0x12345678);

        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(a.Next(), b.Next());
        }
    }

    [Fact]
    public void Different_seeds_give_different_sequences()
    {
        var a = new CountByteGenerator(0x11111111);
        var b = new CountByteGenerator(0x22222222);

        var sequenceA = Enumerable.Range(0, 8).Select(_ => a.Next());
        var sequenceB = Enumerable.Range(0, 8).Select(_ => b.Next());

        Assert.NotEqual(sequenceA, sequenceB);
    }

    [Fact]
    public void Peek_returns_the_next_value_without_advancing()
    {
        var generator = new CountByteGenerator(0xDEADBEEF);

        var peeked = generator.Peek();

        Assert.Equal(peeked, generator.Peek());
        Assert.Equal(peeked, generator.Next());
    }

    [Fact]
    public void A_zero_seed_falls_back_and_still_produces_values()
    {
        var generator = new CountByteGenerator(0);
        var fallback = new CountByteGenerator(0x9ABFB3B6);

        Assert.Equal(fallback.Next(), generator.Next());
    }
}
