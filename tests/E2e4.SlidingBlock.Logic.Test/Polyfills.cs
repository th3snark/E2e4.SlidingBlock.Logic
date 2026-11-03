using System.Runtime.CompilerServices;

// What the test code uses that older runtimes lack; the library has its own copies, which are internal.
#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}

namespace System
{
    internal readonly struct Index
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false) => _value = fromEnd ? ~value : value;

        public static implicit operator Index(int value) => new(value);

        public int GetOffset(int length) => _value < 0 ? length + ~_value : _value;
    }

    internal readonly struct Range
    {
        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public Index Start { get; }

        public Index End { get; }
    }
}

namespace System.Numerics
{
    internal static class BitOperations
    {
        public static int PopCount(ulong value)
        {
            int count = 0;
            for (; value != 0; value &= value - 1) count++;
            return count;
        }

        public static int TrailingZeroCount(ulong value)
        {
            if (value == 0) return 64;
            int count = 0;
            for (; (value & 1) == 0; value >>= 1) count++;
            return count;
        }

        public static int TrailingZeroCount(int value) => TrailingZeroCount((ulong)(uint)value) is var n && n > 32 ? 32 : TrailingZeroCount((ulong)(uint)value);
    }
}
#endif

#if !NET7_0_OR_GREATER
namespace System.Linq
{
    internal static class OrderExtensions
    {
        public static IOrderedEnumerable<T> Order<T>(this IEnumerable<T> source) => source.OrderBy(x => x);
    }
}
#endif
