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
/// stays inside the board and off static walls.
/// </summary>
public sealed record EquivalenceGroup(
    int GroupId,
    string Name,
    string ColorHex,
    ulong BaseMask,
    ulong ValidAnchorsMask,
    AllowedDirections Directions,
    IReadOnlyList<(int dRow, int dCol)> RelativeCells);

public sealed record PieceTemplate(int PieceId, int GroupId, string Label, int InitialAnchor);

public sealed record PieceStyle(string Name, string ColorHex, AllowedDirections Directions = AllowedDirections.Both, string Role = "");

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
