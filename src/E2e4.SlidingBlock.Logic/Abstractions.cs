using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

public enum AllowedDirections : byte
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Both = 3,
}

/// <summary>
/// A set of interchangeable pieces. <see cref="BaseMask"/> is LSB-normalized (bit 0 is always set);
/// <see cref="ValidAnchorsMask"/> has bit s set iff placing the shape with its LSB cell on square s
/// stays inside the board and off static walls. The remaining properties default to the classic sliding behaviour.
/// </summary>
public sealed record EquivalenceGroup<TBitboard>(
    int GroupId,
    string Name,
    TBitboard BaseMask,
    TBitboard ValidAnchorsMask,
    AllowedDirections Directions,
    IReadOnlyList<(int dRow, int dCol)> RelativeCells)
    where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>
{
    /// <summary>Bit 0 = ground layer, bit 1 = upper layer, 0b11 = full-height block.</summary>
    public byte ElevationMask { get; init; } = 0b01;

    public SlidePhysics SlidePhysics { get; init; } = SlidePhysics.ContinuousFloodFill;
    public RoleFlags Roles { get; init; } = RoleFlags.None;

    public bool CanRotate { get; init; }
    public int NextCwGroupId { get; init; } = -1;
    public int NextCcwGroupId { get; init; } = -1;

    /// <summary>Offset from the LSB anchor to the top-left square of the rotation box.</summary>
    public int BoxOriginDelta { get; init; }

    public int AnchorDeltaCw { get; init; }
    public int AnchorDeltaCcw { get; init; }

    /// <summary>Side of the square rotation box.</summary>
    public int RotationBoxSize { get; init; }

    /// <summary>Origin-normalized mask of the whole rotation box (every cell swept while turning).</summary>
    public TBitboard SweptClearanceMask { get; init; }
}

public sealed record PieceTemplate(int PieceId, int GroupId, string Label, int InitialAnchor);

/// <summary>
/// What a map symbol means to the engine. Pieces with the same normalized shape, <see cref="Directions"/>, <see cref="Key"/> and physics share
/// an equivalence group; <see cref="Key"/> is opaque to the engine and lets a puzzle keep otherwise identical shapes apart.
/// Appearance (colours, labels) is the consumer's business.
/// </summary>
public sealed record PieceKind(string Name, AllowedDirections Directions = AllowedDirections.Both, string Key = "",
    byte ElevationMask = 0b01, SlidePhysics SlidePhysics = SlidePhysics.ContinuousFloodFill, RoleFlags Roles = RoleFlags.None);

/// <summary>The 64-bit hash step the packed-register domains share.</summary>
internal static class Hashing
{
    /// <summary>Spreads a register over all 64 bits (Fibonacci hashing).</summary>
    public static ulong Mix(ulong value) => value * 0x9E3779B97F4A7C15UL;
}

public interface ISlidingBlockDomain<TState, TMove> where TState : struct, IEquatable<TState>
{
    TState InitialState { get; }

    /// <summary>Upper bound on the number of moves <see cref="GenerateMoves"/> can write.</summary>
    int MaxMoves { get; }

    bool IsGoal(in TState state);
    int GenerateMoves(in TState state, Span<TMove> moveBuffer);
    TState ApplyMove(in TState state, in TMove move);
    ulong GetHashCode64(in TState state);
}
