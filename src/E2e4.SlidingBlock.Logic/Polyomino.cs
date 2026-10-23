using System.Numerics;
using System.Runtime.CompilerServices;

namespace E2e4.SlidingBlock.Logic;

[InlineArray(8)]
public struct GroupAnchorBuffer
{
    private ulong _element0;
}

[InlineArray(16)]
public struct PieceAnchorBuffer
{
    private byte _element0;
}

/// <summary>
/// Polyomino position. Equality and hashing use only the canonical <see cref="GroupAnchorsOf"/> bitboards and the
/// permutation-invariant Zobrist hash, so swapping identical pieces never changes the state's identity.
/// <see cref="AnchorOf"/> remembers which piece sits where for UI selection/animation only.
/// </summary>
public struct PolyominoState : IEquatable<PolyominoState>
{
    private GroupAnchorBuffer _groupAnchors;
    private PieceAnchorBuffer _pieceAnchors;

    public static PolyominoState FromTopology(PuzzleTopology topology)
    {
        if (topology.Groups.Count > 8) throw new ArgumentException("At most 8 equivalence groups.");
        if (topology.Pieces.Count > 16) throw new ArgumentException("At most 16 pieces.");
        var s = new PolyominoState { OccupancyMask = topology.StaticWallsMask & 0 };
        foreach (var p in topology.Pieces)
        {
            s._pieceAnchors[p.PieceId] = (byte)p.InitialAnchor;
            s._groupAnchors[p.GroupId] |= 1UL << p.InitialAnchor;
            s.OccupancyMask |= topology.Groups[p.GroupId].BaseMask << p.InitialAnchor;
            s.ZobristHash ^= topology.ZobristTable[p.GroupId, p.InitialAnchor];
        }
        return s;
    }

    public ulong OccupancyMask { get; private set; }
    public ulong ZobristHash { get; private set; }

    public readonly ulong GroupAnchorsOf(int groupId) => _groupAnchors[groupId];
    public readonly int AnchorOf(int pieceId) => _pieceAnchors[pieceId];

    /// <summary>Moves one piece to <paramref name="newAnchor"/> (caller validated) with O(1) incremental updates.</summary>
    public readonly PolyominoState WithPieceMoved(PuzzleTopology topology, int pieceId, int newAnchor)
    {
        var s = this;
        int g = topology.Pieces[pieceId].GroupId;
        int old = s._pieceAnchors[pieceId];
        ulong baseMask = topology.Groups[g].BaseMask;
        s._pieceAnchors[pieceId] = (byte)newAnchor;
        s._groupAnchors[g] ^= (1UL << old) | (1UL << newAnchor);
        s.OccupancyMask ^= (baseMask << old) ^ (baseMask << newAnchor);
        s.ZobristHash ^= topology.ZobristTable[g, old] ^ topology.ZobristTable[g, newAnchor];
        return s;
    }

    public readonly bool Equals(PolyominoState other)
    {
        if (ZobristHash != other.ZobristHash) return false;
        for (int g = 0; g < 8; g++)
            if (_groupAnchors[g] != other._groupAnchors[g]) return false;
        return true;
    }

    public override readonly bool Equals(object? obj) => obj is PolyominoState s && Equals(s);
    public override readonly int GetHashCode() => (int)(ZobristHash ^ (ZobristHash >> 32));
    public static bool operator ==(PolyominoState left, PolyominoState right) => left.Equals(right);
    public static bool operator !=(PolyominoState left, PolyominoState right) => !left.Equals(right);

    private static ulong FreeAnchors(int startAnchor, ulong baseMask, ulong validAnchorsMask, ulong otherOccupancy)
    {
        ulong free = 1UL << startAnchor;
        for (ulong v = validAnchorsMask; v != 0; v &= v - 1)
        {
            int a = BitOperations.TrailingZeroCount(v);
            if (((baseMask << a) & otherOccupancy) == 0) free |= 1UL << a;
        }
        return free & (validAnchorsMask | (1UL << startAnchor));
    }

    /// <summary>
    /// Register-only BFS flood fill over anchors. Returns every anchor (excluding the start) reachable by
    /// sliding one square at a time, including multi-step corner "hook" paths. Column masks stop +-1 shifts
    /// from wrapping across rows.
    /// </summary>
    public static ulong FindReachableAnchors(int startAnchor, ulong baseMask, ulong validAnchorsMask, ulong otherOccupancy, int boardWidth,
        AllowedDirections directions = AllowedDirections.Both)
    {
        ulong free = FreeAnchors(startAnchor, baseMask, validAnchorsMask, otherOccupancy);
        ulong col0 = 0;
        for (int i = 0; i < 64; i += boardWidth) col0 |= 1UL << i;
        ulong notCol0 = ~col0, notLastCol = ~(col0 << (boardWidth - 1));

        ulong visitedOrInvalid = ~free | (1UL << startAnchor);
        ulong frontier = 1UL << startAnchor, reachable = 0;
        bool horiz = (directions & AllowedDirections.Horizontal) != 0, vert = (directions & AllowedDirections.Vertical) != 0;
        while (frontier != 0)
        {
            ulong next = 0;
            if (horiz) next |= ((frontier << 1) & notCol0) | ((frontier >> 1) & notLastCol);
            if (vert) next |= (frontier << boardWidth) | (frontier >> boardWidth);
            next &= ~visitedOrInvalid;
            visitedOrInvalid |= next;
            reachable |= next;
            frontier = next;
        }
        return reachable;
    }

    /// <summary>Shortest step-by-step anchor trajectory (start..target inclusive) or empty if unreachable.</summary>
    public static IReadOnlyList<int> ReconstructHookPath(int startAnchor, int targetAnchor, ulong baseMask, ulong validAnchorsMask,
        ulong otherOccupancy, int boardWidth, AllowedDirections directions = AllowedDirections.Both)
    {
        if (startAnchor == targetAnchor) return [startAnchor];
        ulong free = FreeAnchors(startAnchor, baseMask, validAnchorsMask, otherOccupancy);
        Span<int> parent = stackalloc int[64];
        parent.Fill(-1);
        Span<int> queue = stackalloc int[64];
        Span<int> nbr = stackalloc int[4];
        int head = 0, tail = 0;
        queue[tail++] = startAnchor;
        parent[startAnchor] = startAnchor;
        bool horiz = (directions & AllowedDirections.Horizontal) != 0, vert = (directions & AllowedDirections.Vertical) != 0;

        while (head < tail)
        {
            int a = queue[head++];
            int col = a % boardWidth;
            int k = 0;
            if (horiz && col > 0) nbr[k++] = a - 1;
            if (horiz && col < boardWidth - 1) nbr[k++] = a + 1;
            if (vert) { nbr[k++] = a - boardWidth; nbr[k++] = a + boardWidth; }
            for (int i = 0; i < k; i++)
            {
                int n = nbr[i];
                if (n < 0 || n > 63 || parent[n] != -1 || ((free >> n) & 1UL) == 0) continue;
                parent[n] = a;
                if (n == targetAnchor)
                {
                    var path = new List<int>();
                    for (int p = n; p != startAnchor; p = parent[p]) path.Add(p);
                    path.Add(startAnchor);
                    path.Reverse();
                    return path;
                }
                queue[tail++] = n;
            }
        }
        return [];
    }
}

/// <summary>Moves a piece to <see cref="TargetAnchor"/> in one turn (slide or hook).</summary>
public readonly record struct PolyominoMove(byte PieceId, byte TargetAnchor);

public sealed class PolyominoDomain : ISlidingBlockDomain<PolyominoState, PolyominoMove>
{
    public PolyominoDomain(PuzzleTopology topology, int goalGroupId, int goalAnchor)
    {
        Topology = topology;
        GoalGroupId = goalGroupId;
        GoalAnchor = goalAnchor;
        InitialState = PolyominoState.FromTopology(topology);
    }

    public const int LabWidth = 5;

    /// <summary>The 5x5 Lab 2 board: L-tromino, upward T, J-tromino, two identical 1x1 blocks, wall at index 12.</summary>
    public static PolyominoDomain CreateLab2()
    {
        var b = PuzzleTopology.CreateBuilder(5, 5);
        b.AddWall(2, 2);
        int l = b.AddGroup("Master L-Tromino", "#F5B301", [(0, 0), (1, 0), (1, 1)]);
        int t = b.AddGroup("Upward T-Tetromino", "#22D3EE", [(0, 0), (1, -1), (1, 0), (1, 1)]);
        int j = b.AddGroup("J-Tromino", "#A855F7", [(0, 0), (1, -1), (1, 0)]);
        int s = b.AddGroup("Identical 1x1 Blocks", "#10B981", [(0, 0)]);
        b.AddPiece(l, "L", 0);
        b.AddPiece(t, "T", 16);
        b.AddPiece(j, "J", 4);
        b.AddPiece(s, "A", 2);
        b.AddPiece(s, "B", 14);
        return new PolyominoDomain(b.Build(), goalGroupId: 0, goalAnchor: 16);
    }

    public PuzzleTopology Topology { get; }
    public int GoalGroupId { get; }
    public int GoalAnchor { get; }
    public PolyominoState InitialState { get; }
    public int MaxMoves => Topology.Pieces.Count * Topology.CellCount;

    public bool IsGoal(in PolyominoState state) => state.GroupAnchorsOf(GoalGroupId) == 1UL << GoalAnchor;

    public ulong ReachableFor(in PolyominoState state, int pieceId)
    {
        var p = Topology.Pieces[pieceId];
        var g = Topology.Groups[p.GroupId];
        int a = state.AnchorOf(pieceId);
        ulong other = state.OccupancyMask ^ (g.BaseMask << a);
        return PolyominoState.FindReachableAnchors(a, g.BaseMask, g.ValidAnchorsMask, other, Topology.Width, g.Directions);
    }

    public int GenerateMoves(in PolyominoState state, Span<PolyominoMove> moveBuffer)
    {
        int n = 0;
        foreach (var p in Topology.Pieces)
            for (ulong r = ReachableFor(state, p.PieceId); r != 0; r &= r - 1)
                moveBuffer[n++] = new PolyominoMove((byte)p.PieceId, (byte)BitOperations.TrailingZeroCount(r));
        return n;
    }

    public PolyominoState ApplyMove(in PolyominoState state, in PolyominoMove move) =>
        state.WithPieceMoved(Topology, move.PieceId, move.TargetAnchor);

    public ulong GetHashCode64(in PolyominoState state) => state.ZobristHash;
}
