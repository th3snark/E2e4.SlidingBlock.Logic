using System.Globalization;
using System.Text;

namespace E2e4.SlidingBlock.Logic;

/// <summary>Derived operations and diagnostics for any <see cref="IBitboard{TSelf}"/>.</summary>
public static class BitboardExtensions
{
    /// <summary>The bits of <paramref name="a"/> that are not in <paramref name="b"/>.</summary>
    public static T AndNot<T>(this T a, T b) where T : unmanaged, IBitboard<T> => a.And(b.Not());

    /// <summary>True if <paramref name="a"/> and <paramref name="b"/> have a bit in common.</summary>
    public static bool Intersects<T>(this T a, T b) where T : unmanaged, IBitboard<T> => !a.And(b).IsEmpty;

    /// <summary>True if bit <paramref name="index"/> is set.</summary>
    public static bool HasBit<T>(this T a, int index) where T : unmanaged, IBitboard<T> => !a.ShiftRight(index).And(a.One).IsEmpty;

    extension<T>(T mask) where T : unmanaged, IBitboard<T>
    {
        /// <summary>Index of the least-significant set bit (the bit width when the mask is empty).</summary>
        public int Lsb => mask.TrailingZeroCount();

        /// <summary>Index of the most-significant set bit (-1 when the mask is empty).</summary>
        public int Msb => mask.Width - 1 - mask.LeadingZeroCount();

        /// <summary>The mask as <c>0x</c> and upper-case hex digits; <paramref name="digits"/> defaults to the full width of the type.</summary>
        public string ToHex(int digits = -1)
        {
            var all = new StringBuilder();
            for (int w = mask.Width / 64 - 1; w >= 0; w--) all.Append(mask.GetWord(w).ToString("X16", CultureInfo.InvariantCulture));
            string hex = all.ToString();
            if (digits < 0) digits = hex.Length;
            return "0x" + (hex.Length >= digits ? hex.Substring(hex.Length - digits) : hex.PadLeft(digits, '0'));
        }

        /// <summary>The low <paramref name="bits"/> bits, most significant first, with an underscore between groups of <paramref name="groupSize"/> (0 for no groups).</summary>
        public string ToBinary(int bits, int groupSize = 4)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bits);
            ArgumentOutOfRangeException.ThrowIfNegative(groupSize);
            var sb = new StringBuilder(groupSize > 0 ? bits + bits / groupSize : bits);
            for (int i = bits - 1; i >= 0; i--)
            {
                sb.Append(mask.HasBit(i) ? '1' : '0');
                if (groupSize > 0 && i % groupSize == 0 && i != 0) sb.Append('_');
            }
            return sb.ToString();
        }

        /// <summary>Renders the mask as a width x height grid (row 0 on top, bit 0 top-left), rows separated by <c>\n</c> on every platform.</summary>
        public string ToGrid(int width, int height)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                    sb.Append(mask.HasBit(r * width + c) ? '#' : '.');
                if (r != height - 1) sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
