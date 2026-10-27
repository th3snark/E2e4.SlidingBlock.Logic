using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// One step of a train, or one turn of a turntable. An engine step moves the engine one slot out of its lead end, pushing everything in front of it
/// one slot ahead and pulling the first <see cref="RearLength"/> pieces behind it. The two choice words record which branch was taken at each switch
/// (2 bits per slot), so the move says exactly which slots it covers; a train is at most 32 pieces long.
/// A turn has <see cref="Mover"/> 255, the turntable in <see cref="Lead"/> and the new alignment in <see cref="FrontLength"/>.
/// </summary>
/// <param name="Mover">The group number of the engine, or 255 for a turntable turn.</param>
/// <param name="Lead">The end (0 or 1) of its slot the engine leaves through, or the turntable number for a turn.</param>
/// <param name="FrontLength">How many pieces move ahead of and including the engine, or the new alignment for a turn.</param>
/// <param name="RearLength">How many pieces behind the engine are pulled along.</param>
/// <param name="FrontChoices">The branch taken at each step of the front path.</param>
/// <param name="RearChoices">The branch taken at each step of the rear path.</param>
public readonly record struct ShuntingMove(byte Mover, byte Lead, byte FrontLength, byte RearLength, ulong FrontChoices, ulong RearChoices)
{
    /// <summary>The <see cref="Mover"/> value of a turntable turn.</summary>
    public const byte TurnMover = 255;

    /// <summary>True if the move turns a turntable.</summary>
    public bool IsTurn => Mover == TurnMover;

    /// <summary>The turntable a turn moves (only for a turn).</summary>
    public int Turntable => Lead;

    /// <summary>The alignment a turn ends in (only for a turn).</summary>
    public int Alignment => FrontLength;
}

/// <summary>
/// A shunting position: which slot each group's pieces stand on, which way the tracked pieces face, how the turntables stand, and which engine
/// moved last and which way (the momentum, which decides what the next move costs). Pieces of one group are interchangeable.
/// </summary>
public struct ShuntingState<TBitboard> : IEquatable<ShuntingState<TBitboard>>
    where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>
{
    /// <summary>The most groups a position can hold.</summary>
    public const int MaxGroups = GroupAnchorBuffer<TBitboard>.Length;

    /// <summary>The most turntables a track may have; each stands in one of up to 16 alignments.</summary>
    public const int MaxTurntables = 16;

    internal const byte NoEngine = ShuntingMove.TurnMover;

    internal GroupAnchorBuffer<TBitboard> Anchors;
    internal TBitboard OccupancyBits;
    internal TBitboard HeadingBits;
    internal ulong Alignments;
    internal byte Engine;
    internal byte SenseBit;
    internal ulong Hash;

    /// <summary>Bit s is set when a piece stands on slot s.</summary>
    public readonly TBitboard Occupancy => OccupancyBits;

    /// <summary>Bit s is set when the tracked piece on slot s faces end 1 of its slot; untracked pieces are always 0.</summary>
    public readonly TBitboard Heading => HeadingBits;

    /// <summary>Bit s is set when a piece of group <paramref name="group"/> stands on slot s.</summary>
    public readonly TBitboard GroupSlots(int group) => Anchors[group];

    /// <summary>The alignment turntable <paramref name="turntable"/> stands in.</summary>
    public readonly int Alignment(int turntable) => (int)((Alignments >> (4 * turntable)) & 15);

    /// <summary>The group of the engine that moved last, or -1 at rest (at the start and after a turntable turn).</summary>
    public readonly int ActiveEngine => Engine == NoEngine ? -1 : Engine;

    /// <summary>0 if the active engine last drove front first, 1 if rear first; meaningless at rest.</summary>
    public readonly int Sense => SenseBit;

    /// <inheritdoc/>
    public readonly bool Equals(ShuntingState<TBitboard> other)
    {
        if (Hash != other.Hash || Engine != other.Engine || SenseBit != other.SenseBit || Alignments != other.Alignments) return false;
        if (HeadingBits != other.HeadingBits || OccupancyBits != other.OccupancyBits) return false;
        for (int g = 0; g < MaxGroups; g++)
        {
            if (Anchors[g] != other.Anchors[g]) return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is ShuntingState<TBitboard> other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode() => (int)Hash;

    /// <summary>Whether two positions are the same, momentum included.</summary>
    public static bool operator ==(ShuntingState<TBitboard> left, ShuntingState<TBitboard> right) => left.Equals(right);

    /// <summary>Whether two positions differ.</summary>
    public static bool operator !=(ShuntingState<TBitboard> left, ShuntingState<TBitboard> right) => !left.Equals(right);
}
