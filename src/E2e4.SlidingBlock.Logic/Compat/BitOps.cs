#if !NET
using System.Runtime.CompilerServices;
#else
using System.Numerics;
#endif

namespace E2e4.SlidingBlock.Logic;

/// <summary>The 64-bit counting intrinsics; <c>System.Numerics.BitOperations</c> exists only from .NET Core 3.0.</summary>
internal static class BitOps
{
#if !NET
    public static int PopCount(ulong value)
    {
        value -= (value >> 1) & 0x5555555555555555UL;
        value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
        value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
        return (int)((value * 0x0101010101010101UL) >> 56);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int TrailingZeroCount(ulong value) => value == 0 ? 64 : PopCount((value & (0 - value)) - 1);

    public static int LeadingZeroCount(ulong value)
    {
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        value |= value >> 32;
        return PopCount(~value);
    }
#else
    public static int PopCount(ulong value) => BitOperations.PopCount(value);

    public static int TrailingZeroCount(ulong value) => BitOperations.TrailingZeroCount(value);

    public static int LeadingZeroCount(ulong value) => BitOperations.LeadingZeroCount(value);
#endif
}
