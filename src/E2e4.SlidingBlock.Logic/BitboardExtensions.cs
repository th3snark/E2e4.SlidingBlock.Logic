using System.Numerics;
using System.Text;

namespace E2e4.SlidingBlock.Logic;

/// <summary>C# 14 extension members for diagnosing 64-bit bitboards.</summary>
public static class BitboardExtensions
{
    extension(ulong mask)
    {
        /// <summary>Index of the least-significant set bit (64 when the mask is empty).</summary>
        public int Lsb => BitOperations.TrailingZeroCount(mask);

        /// <summary>Index of the most-significant set bit (-1 when the mask is empty).</summary>
        public int Msb => 63 - BitOperations.LeadingZeroCount(mask);

        public int PopCount => BitOperations.PopCount(mask);

        public bool HasBit(int index) => ((mask >> index) & 1UL) != 0;

        public string ToHex(int digits = 16) => "0x" + mask.ToString("X" + digits, System.Globalization.CultureInfo.InvariantCulture);

        public string ToBinary(int bits, int groupSize = 4)
        {
            var sb = new StringBuilder(bits + bits / groupSize);
            for (int i = bits - 1; i >= 0; i--)
            {
                sb.Append(((mask >> i) & 1UL) != 0 ? '1' : '0');
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
                    sb.Append(((mask >> (r * width + c)) & 1UL) != 0 ? '#' : '.');
                if (r != height - 1) sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
