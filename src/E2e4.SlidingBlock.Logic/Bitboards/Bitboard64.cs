using System.Globalization;

namespace E2e4.SlidingBlock.Logic;

/// <summary>A bitboard of 64 cells, bit 0 being the first cell. Converts implicitly from <see cref="ulong"/>.</summary>
/// <param name="value">The bits.</param>
public readonly struct Bitboard64(ulong value) : IBitboard<Bitboard64>
{
    /// <summary>The bits as a <see cref="ulong"/>.</summary>
    public ulong Value { get; } = value;

    /// <inheritdoc/>
    public int Width => 64;

    /// <inheritdoc/>
    public Bitboard64 Zero => default;

    /// <inheritdoc/>
    public Bitboard64 One => new(1);

    /// <inheritdoc/>
    public Bitboard64 All => new(ulong.MaxValue);

    /// <inheritdoc/>
    public bool IsEmpty => Value == 0;

    /// <inheritdoc/>
    public Bitboard64 And(Bitboard64 other) => new(Value & other.Value);

    /// <inheritdoc/>
    public Bitboard64 Or(Bitboard64 other) => new(Value | other.Value);

    /// <inheritdoc/>
    public Bitboard64 Xor(Bitboard64 other) => new(Value ^ other.Value);

    /// <inheritdoc/>
    public Bitboard64 Not() => new(~Value);

    /// <inheritdoc/>
    public Bitboard64 ShiftLeft(int count) => new(Value << count);

    /// <inheritdoc/>
    public Bitboard64 ShiftRight(int count) => new(Value >> count);

    /// <inheritdoc/>
    public Bitboard64 ClearLowest() => new(Value & (Value - 1));

    /// <inheritdoc/>
    public int PopCount() => BitOps.PopCount(Value);

    /// <inheritdoc/>
    public int TrailingZeroCount() => BitOps.TrailingZeroCount(Value);

    /// <inheritdoc/>
    public int LeadingZeroCount() => BitOps.LeadingZeroCount(Value);

    /// <inheritdoc/>
    public ulong GetWord(int index)
    {
        if (index != 0) throw new ArgumentOutOfRangeException(nameof(index), index, "A 64-bit board has one word.");
        return Value;
    }

    /// <inheritdoc/>
    public bool Equals(Bitboard64 other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Bitboard64 other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>The bits as 16 upper-case hex digits with a <c>0x</c> prefix.</summary>
    public override string ToString() => "0x" + Value.ToString("X16", CultureInfo.InvariantCulture);

    /// <summary>Bitwise and.</summary>
    public static Bitboard64 operator &(Bitboard64 left, Bitboard64 right) => left.And(right);

    /// <summary>Bitwise or.</summary>
    public static Bitboard64 operator |(Bitboard64 left, Bitboard64 right) => left.Or(right);

    /// <summary>Bitwise exclusive or.</summary>
    public static Bitboard64 operator ^(Bitboard64 left, Bitboard64 right) => left.Xor(right);

    /// <summary>Bitwise complement.</summary>
    public static Bitboard64 operator ~(Bitboard64 value) => value.Not();

    /// <summary>Shift left.</summary>
    public static Bitboard64 operator <<(Bitboard64 value, int count) => value.ShiftLeft(count);

    /// <summary>Shift right.</summary>
    public static Bitboard64 operator >>(Bitboard64 value, int count) => value.ShiftRight(count);

    /// <summary>True when both hold the same bits.</summary>
    public static bool operator ==(Bitboard64 left, Bitboard64 right) => left.Equals(right);

    /// <summary>True when the bits differ.</summary>
    public static bool operator !=(Bitboard64 left, Bitboard64 right) => !left.Equals(right);

    /// <summary>Wraps <paramref name="value"/>.</summary>
    public static implicit operator Bitboard64(ulong value) => new(value);

    /// <summary>The bits as a <see cref="ulong"/>.</summary>
    public static explicit operator ulong(Bitboard64 value) => value.Value;
}
