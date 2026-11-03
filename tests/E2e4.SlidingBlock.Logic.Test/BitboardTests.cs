using System.Numerics;

namespace E2e4.SlidingBlock.Logic.Test;

/// <summary>The two bitboard structs against built-in integers (<see cref="BigInteger"/> for the 128-bit one) on random inputs.</summary>
public class BitboardTests
{
    private static readonly BigInteger Mask128 = (BigInteger.One << 128) - 1;

    private static BigInteger Big(Bitboard128 b) => new BigInteger(b.Lower) | (new BigInteger(b.Upper) << 64);

    private static IEnumerable<ulong> Samples()
    {
        var random = new Random(12345);
        yield return 0;
        yield return 1;
        yield return ulong.MaxValue;
        yield return 1UL << 63;
        for (int i = 0; i < 40; i++) yield return ((ulong)(uint)random.Next() << 32) | (uint)random.Next();
        for (int i = 0; i < 20; i++) yield return 1UL << random.Next(64);
    }

    [Fact]
    public void Bitboard64MatchesUlong()
    {
        foreach (ulong a in Samples())
        {
            var x = new Bitboard64(a);
            foreach (ulong b in Samples().Take(12))
            {
                var y = new Bitboard64(b);
                Assert.Equal(a & b, x.And(y).Value);
                Assert.Equal(a | b, (x | y).Value);
                Assert.Equal(a ^ b, (x ^ y).Value);
                Assert.Equal(a & ~b, x.AndNot(y).Value);
                Assert.Equal((a & b) != 0, x.Intersects(y));
            }
            Assert.Equal(~a, x.Not().Value);
            Assert.Equal(a == 0, x.IsEmpty);
            Assert.Equal(a & (a - 1), x.ClearLowest().Value);
            for (int n = 0; n < 64; n++)
            {
                Assert.Equal(a << n, x.ShiftLeft(n).Value);
                Assert.Equal(a >> n, (x >> n).Value);
                Assert.Equal((a >> n & 1) != 0, x.HasBit(n));
            }
            Assert.Equal(a, x.GetWord(0));
            Assert.Equal(a.ToString("X16", System.Globalization.CultureInfo.InvariantCulture), x.ToHex()[2..]);
        }
        Assert.Equal(64, default(Bitboard64).TrailingZeroCount());
        Assert.Equal(64, default(Bitboard64).LeadingZeroCount());
        Assert.Equal(0, default(Bitboard64).PopCount());
        Assert.Equal(ulong.MaxValue, default(Bitboard64).All.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => default(Bitboard64).GetWord(1));
    }

    [Fact]
    public void Bitboard128MatchesBigInteger()
    {
        var words = Samples().ToArray();
        for (int i = 0; i < words.Length; i++)
        {
            var x = new Bitboard128(words[i], words[(i * 7 + 3) % words.Length]);
            var y = new Bitboard128(words[(i * 5 + 1) % words.Length], words[(i * 11 + 2) % words.Length]);
            BigInteger a = Big(x), b = Big(y);
            Assert.Equal(a & b, Big(x & y));
            Assert.Equal(a | b, Big(x | y));
            Assert.Equal(a ^ b, Big(x ^ y));
            Assert.Equal(Mask128 & ~a, Big(~x));
            Assert.Equal(a.IsZero, x.IsEmpty);
            Assert.Equal(!(a & b).IsZero, x.Intersects(y));
            Assert.Equal(a.IsZero ? a : a & (a - 1), Big(x.ClearLowest()));

            for (int n = 0; n < 128; n++)
            {
                Assert.Equal((a << n) & Mask128, Big(x << n));
                Assert.Equal(a >> n, Big(x >> n));
                Assert.Equal(!((a >> n) & 1).IsZero, x.HasBit(n));
            }

            int pop = 0, tz = 128, lz = 128;
            for (int n = 0; n < 128; n++)
            {
                if (((a >> n) & 1).IsZero) continue;
                pop++;
                if (tz == 128) tz = n;
                lz = 127 - n;
            }
            Assert.Equal(pop, x.PopCount());
            Assert.Equal(tz, x.TrailingZeroCount());
            Assert.Equal(lz, x.LeadingZeroCount());
            Assert.Equal(x.Lower, x.GetWord(0));
            Assert.Equal(x.Upper, x.GetWord(1));
            Assert.Equal(32 + 2, x.ToHex().Length);
        }
    }

    [Fact]
    public void ShiftCountsAreTakenModuloTheWidth()
    {
        Assert.Equal(new Bitboard128(1, 0), new Bitboard128(1, 0) << 128);
        Assert.Equal(new Bitboard128(1, 0), new Bitboard128(1, 0) >> 128);
        Assert.Equal(new Bitboard64(1), new Bitboard64(1) << 64);
    }

    [Fact]
    public void ConversionsAndEquality()
    {
        Bitboard64 a = 5UL;
        Bitboard128 b = 5UL;
        Assert.Equal(5UL, (ulong)a);
        Assert.Equal(5UL, (ulong)b);
        Assert.True(a == new Bitboard64(5));
        Assert.True(b != new Bitboard128(5, 1));
        Assert.Equal(a.GetHashCode(), new Bitboard64(5).GetHashCode());
        Assert.Equal("0x0000000000000005", a.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => b.GetWord(2));
    }
}
