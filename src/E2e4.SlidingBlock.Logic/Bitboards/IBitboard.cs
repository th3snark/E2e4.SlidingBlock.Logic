#pragma warning disable CA1716 // And/Or/Xor/Not read best in C#, this library's only audience; a VB caller escapes them as [And]

namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// A fixed-width set of cells held as bits, the value the move generators work on. <see cref="Bitboard64"/> (boards up to 64 cells) and
/// <see cref="Bitboard128"/> (up to 128) are the implementations. The interface has instance members only, so one design serves every target framework.
/// Shift counts are taken modulo <see cref="Width"/>, as for the built-in integer types.
/// </summary>
/// <typeparam name="TSelf">The implementing struct.</typeparam>
public interface IBitboard<TSelf> : IEquatable<TSelf> where TSelf : unmanaged, IBitboard<TSelf>
{
    /// <summary>The number of bits.</summary>
    int Width { get; }

    /// <summary>No bit set.</summary>
    TSelf Zero { get; }

    /// <summary>Only bit 0 set.</summary>
    TSelf One { get; }

    /// <summary>Every bit set.</summary>
    TSelf All { get; }

    /// <summary>True when no bit is set.</summary>
    bool IsEmpty { get; }

    /// <summary>The bits set in both this and <paramref name="other"/>.</summary>
    TSelf And(TSelf other);

    /// <summary>The bits set in either this or <paramref name="other"/>.</summary>
    TSelf Or(TSelf other);

    /// <summary>The bits set in exactly one of this and <paramref name="other"/>.</summary>
    TSelf Xor(TSelf other);

    /// <summary>Every bit flipped.</summary>
    TSelf Not();

    /// <summary>Moved towards the most significant bit by <paramref name="count"/>.</summary>
    TSelf ShiftLeft(int count);

    /// <summary>Moved towards the least significant bit by <paramref name="count"/>.</summary>
    TSelf ShiftRight(int count);

    /// <summary>This without its lowest set bit.</summary>
    TSelf ClearLowest();

    /// <summary>The number of set bits.</summary>
    int PopCount();

    /// <summary>The index of the lowest set bit; <see cref="Width"/> when empty.</summary>
    int TrailingZeroCount();

    /// <summary>The number of clear bits above the highest set bit; <see cref="Width"/> when empty.</summary>
    int LeadingZeroCount();

    /// <summary>The 64 bits starting at bit <c>64 * index</c>.</summary>
    ulong GetWord(int index);
}
