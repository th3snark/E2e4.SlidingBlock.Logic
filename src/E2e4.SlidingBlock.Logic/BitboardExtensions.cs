using System.Globalization;
using System.Numerics;
using System.Text;

namespace E2e4.SlidingBlock.Logic;

/// <summary>C# 14 extension members for diagnosing bitboards of any width (<see cref="ulong"/>, <see cref="UInt128"/>).</summary>
public static class BitboardExtensions
{
    extension<T>(T mask) where T : unmanaged, IBinaryInteger<T>, IUnsignedNumber<T>
    {
        /// <summary>Index of the least-significant set bit (the bit width when the mask is empty).</summary>
        public int Lsb => int.CreateTruncating(T.TrailingZeroCount(mask));

        /// <summary>Index of the most-significant set bit (-1 when the mask is empty).</summary>
        public int Msb => Grid.BitWidth<T>() - 1 - int.CreateTruncating(T.LeadingZeroCount(mask));

        /// <summary>Number of set bits.</summary>
        public int BitCount => int.CreateTruncating(T.PopCount(mask));

        public bool HasBit(int index) => ((mask >> index) & T.One) != T.Zero;

        public string ToHex(int digits = -1) =>
            "0x" + mask.ToString("X" + (digits < 0 ? Grid.BitWidth<T>() / 4 : digits).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        public string ToBinary(int bits, int groupSize = 4)
        {
            var sb = new StringBuilder(bits + bits / groupSize);
            for (int i = bits - 1; i >= 0; i--)
            {
                sb.Append(((mask >> i) & T.One) != T.Zero ? '1' : '0');
                if (groupSize > 0 && i % groupSize == 0 && i != 0) sb.Append('_');
            }
            return sb.ToString();
        }

        /// <summary>Renders the mask as a width x height grid (row 0 on top, bit 0 top-left).</summary>
        public string ToGrid(int width, int height)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                    sb.Append(((mask >> (r * width + c)) & T.One) != T.Zero ? '#' : '.');
                if (r != height - 1) sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
