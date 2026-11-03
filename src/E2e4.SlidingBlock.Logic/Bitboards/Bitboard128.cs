using System.Globalization;

namespace E2e4.SlidingBlock.Logic;

/// <summary>A bitboard of 128 cells, bit 0 being the first cell, held as two 64-bit words. Converts implicitly from <see cref="ulong"/>.</summary>
/// <param name="lower">Bits 0 to 63.</param>
/// <param name="upper">Bits 64 to 127.</param>
public readonly struct Bitboard128(ulong lower, ulong upper) : IBitboard<Bitboard128>
{
    /// <summary>Bits 0 to 63.</summary>
    public ulong Lower { get; } = lower;

    /// <summary>Bits 64 to 127.</summary>
    public ulong Upper { get; } = upper;

    /// <inheritdoc/>
    public int Width => 128;

    /// <inheritdoc/>
    public Bitboard128 Zero => default;

    /// <inheritdoc/>
    public Bitboard128 One => new(1, 0);

    /// <inheritdoc/>
    public Bitboard128 All => new(ulong.MaxValue, ulong.MaxValue);

    /// <inheritdoc/>
    public bool IsEmpty => (Lower | Upper) == 0;

    /// <inheritdoc/>
    public Bitboard128 And(Bitboard128 other) => new(Lower & other.Lower, Upper & other.Upper);

    /// <inheritdoc/>
    public Bitboard128 Or(Bitboard128 other) => new(Lower | other.Lower, Upper | other.Upper);

    /// <inheritdoc/>
    public Bitboard128 Xor(Bitboard128 other) => new(Lower ^ other.Lower, Upper ^ other.Upper);

    /// <inheritdoc/>
    public Bitboard128 Not() => new(~Lower, ~Upper);

    /// <inheritdoc/>
    public Bitboard128 ShiftLeft(int count)
    {
        count &= 127;
        if (count == 0) return this;
        if (count >= 64) return new(0, Lower << (count - 64));
        return new(Lower << count, (Upper << count) | (Lower >> (64 - count)));
    }

    /// <inheritdoc/>
    public Bitboard128 ShiftRight(int count)
    {
        count &= 127;
        if (count == 0) return this;
        if (count >= 64) return new(Upper >> (count - 64), 0);
        return new((Lower >> count) | (Upper << (64 - count)), Upper >> count);
    }

    /// <inheritdoc/>
    public Bitboard128 ClearLowest() => Lower != 0 ? new(Lower & (Lower - 1), Upper) : new(0, Upper & (Upper - 1));

    /// <inheritdoc/>
    public int PopCount() => BitOps.PopCount(Lower) + BitOps.PopCount(Upper);

    /// <inheritdoc/>
    public int TrailingZeroCount() => Lower != 0 ? BitOps.TrailingZeroCount(Lower) : 64 + BitOps.TrailingZeroCount(Upper);

    /// <inheritdoc/>
    public int LeadingZeroCount() => Upper != 0 ? BitOps.LeadingZeroCount(Upper) : 64 + BitOps.LeadingZeroCount(Lower);

    /// <inheritdoc/>
    public ulong GetWord(int index)
    {
        if (index is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(index), index, "A 128-bit board has two words.");
        return index == 0 ? Lower : Upper;
    }

    /// <inheritdoc/>
    public bool Equals(Bitboard128 other) => Lower == other.Lower && Upper == other.Upper;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Bitboard128 other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => unchecked((Lower.GetHashCode() * 31) + Upper.GetHashCode());

    /// <summary>The bits as 32 upper-case hex digits with a <c>0x</c> prefix.</summary>
    public override string ToString() =>
        "0x" + Upper.ToString("X16", CultureInfo.InvariantCulture) + Lower.ToString("X16", CultureInfo.InvariantCulture);

    /// <summary>Bitwise and.</summary>
    public static Bitboard128 operator &(Bitboard128 left, Bitboard128 right) => left.And(right);

    /// <summary>Bitwise or.</summary>
    public static Bitboard128 operator |(Bitboard128 left, Bitboard128 right) => left.Or(right);

    /// <summary>Bitwise exclusive or.</summary>
    public static Bitboard128 operator ^(Bitboard128 left, Bitboard128 right) => left.Xor(right);

    /// <summary>Bitwise complement.</summary>
    public static Bitboard128 operator ~(Bitboard128 value) => value.Not();

    /// <summary>Shift left.</summary>
    public static Bitboard128 operator <<(Bitboard128 value, int count) => value.ShiftLeft(count);

    /// <summary>Shift right.</summary>
    public static Bitboard128 operator >>(Bitboard128 value, int count) => value.ShiftRight(count);

    /// <summary>True when both hold the same bits.</summary>
    public static bool operator ==(Bitboard128 left, Bitboard128 right) => left.Equals(right);

    /// <summary>True when the bits differ.</summary>
    public static bool operator !=(Bitboard128 left, Bitboard128 right) => !left.Equals(right);

    /// <summary>Wraps <paramref name="value"/> as the lower 64 bits.</summary>
    public static implicit operator Bitboard128(ulong value) => new(value, 0);

    /// <summary>The lower 64 bits; the upper bits are discarded.</summary>
    public static explicit operator ulong(Bitboard128 value) => value.Lower;
}
