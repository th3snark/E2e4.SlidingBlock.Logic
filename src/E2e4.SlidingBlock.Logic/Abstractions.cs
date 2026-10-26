using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

/// <summary>Which axes a piece may slide along.</summary>
public enum AllowedDirections : byte
{
    /// <summary>The piece cannot slide.</summary>
    None = 0,
    /// <summary>Left and right only.</summary>
    Horizontal = 1,
    /// <summary>Up and down only.</summary>
    Vertical = 2,
    /// <summary>Any direction.</summary>
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

    /// <summary>How the group travels: one cell, any distance, or until blocked.</summary>
    public SlidePhysics SlidePhysics { get; init; } = SlidePhysics.ContinuousFloodFill;
    /// <summary>What the group does to other pieces when it moves: push, carry or attract them.</summary>
    public RoleFlags Roles { get; init; } = RoleFlags.None;

    /// <summary>True if the group is one orientation of a rotation ring.</summary>
    public bool CanRotate { get; init; }
    /// <summary>The group this one turns into when rotated clockwise, or -1 if it cannot rotate.</summary>
    public int NextCwGroupId { get; init; } = -1;
    /// <summary>The group this one turns into when rotated anticlockwise, or -1 if it cannot rotate.</summary>
    public int NextCcwGroupId { get; init; } = -1;

    /// <summary>Offset from the LSB anchor to the top-left square of the rotation box.</summary>
    public int BoxOriginDelta { get; init; }

    /// <summary>How far the anchor moves when the piece rotates clockwise.</summary>
    public int AnchorDeltaCw { get; init; }
    /// <summary>How far the anchor moves when the piece rotates anticlockwise.</summary>
    public int AnchorDeltaCcw { get; init; }

    /// <summary>Side of the square rotation box.</summary>
    public int RotationBoxSize { get; init; }

    /// <summary>Origin-normalized mask of the whole rotation box (every cell swept while turning).</summary>
    public TBitboard SweptClearanceMask { get; init; }
}

/// <summary>
/// A piece of the puzzle: who it is, the shape class it belongs to and where it starts.
/// </summary>
/// <param name="PieceId">The piece number, counting from 0 in the order the pieces were added.</param>
/// <param name="GroupId">The shape class (equivalence group) of the piece.</param>
/// <param name="Label">A name the consumer can show; the engine does not read it.</param>
/// <param name="InitialAnchor">The cell of the piece's first (lowest) square at the start.</param>
public sealed record PieceTemplate(int PieceId, int GroupId, string Label, int InitialAnchor);

/// <summary>
/// What a map symbol means to the engine. Pieces with the same normalized shape, <see cref="Directions"/>, <see cref="Key"/> and physics share
/// an equivalence group; <see cref="Key"/> is opaque to the engine and lets a puzzle keep otherwise identical shapes apart.
/// Appearance (colours, labels) is the consumer's business.
/// </summary>
public sealed record PieceKind(string Name, AllowedDirections Directions = AllowedDirections.Both, string Key = "",
    byte ElevationMask = 0b01, SlidePhysics SlidePhysics = SlidePhysics.ContinuousFloodFill, RoleFlags Roles = RoleFlags.None);

/// <summary>The 64-bit hash steps the packed-register domains share.</summary>
public static class Hashing
{
    /// <summary>Spreads a register over all 64 bits (Fibonacci hashing).</summary>
    public static ulong Mix(ulong value) => value * 0x9E3779B97F4A7C15UL;

    /// <summary>Folds four 64-bit words into 64 well-spread bits.</summary>
    public static ulong Mix(ulong a, ulong b, ulong c, ulong d)
    {
        ulong h = (a ^ (a >> 29)) * 0x9E3779B97F4A7C15UL ^ b;
        h = (h ^ (h >> 31)) * 0xBF58476D1CE4E5B9UL ^ c;
        h = (h ^ (h >> 29)) * 0x94D049BB133111EBUL ^ d;
        return (h ^ (h >> 31)) * 0xBF58476D1CE4E5B9UL;
    }
}

/// <summary>
/// The rules of a sliding puzzle, as the solvers see them: a start position, a goal test, the legal moves of a position and what a move does.
/// Positions are small structs and moves are written into a caller-supplied buffer, so a search allocates nothing per node (unless the domain's
/// own position type does). Every move counts one, whatever it does.
/// </summary>
public interface ISlidingBlockDomain<TState, TMove> where TState : struct, IEquatable<TState>
{
    /// <summary>The position the puzzle starts from.</summary>
    TState InitialState { get; }

    /// <summary>Upper bound on the number of moves <see cref="GenerateMoves"/> can write.</summary>
    int MaxMoves { get; }

    /// <summary>True if <paramref name="state"/> solves the puzzle.</summary>
    bool IsGoal(in TState state);
    /// <summary>
    /// Writes the legal moves of <paramref name="state"/> into <paramref name="moveBuffer"/> and returns how many.
    /// <paramref name="moveBuffer"/> must hold at least <see cref="MaxMoves"/> entries.
    /// </summary>
    int GenerateMoves(in TState state, Span<TMove> moveBuffer);
    /// <summary>The position after <paramref name="move"/>, which must be one of the moves generated for <paramref name="state"/>; it is not checked.</summary>
    TState ApplyMove(in TState state, in TMove move);
    /// <summary>A 64-bit hash of the position; equal positions give equal hashes.</summary>
    ulong GetHashCode64(in TState state);
}
