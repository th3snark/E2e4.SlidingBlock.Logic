using System.Numerics;
using System.Runtime.CompilerServices;

namespace E2e4.SlidingBlock.Logic;

[InlineArray(10)]
public struct KlotskiAnchorBuffer
{
    private byte _element0;
}

/// <summary>
/// Klotski position: per-piece anchors (for UI identity), the 20-bit occupancy bitboard and the 60-bit
/// canonical shape key (3 bits per cell, class = groupId + 1). Equality uses the key only, so swapping
/// identical pieces yields the same state.
/// </summary>
public readonly struct KlotskiState : IEquatable<KlotskiState>
{
    private readonly KlotskiAnchorBuffer _anchors;

    public KlotskiState(PuzzleTopology topology)
    {
        if (topology.Pieces.Count > 10) throw new ArgumentException("At most 10 pieces.");
        if (topology.Groups.Count > 7) throw new ArgumentException("At most 7 shape classes fit in 3 bits.");
        var anchors = new KlotskiAnchorBuffer();
        ulong occ = 0, key = 0;
        foreach (var p in topology.Pieces)
        {
            anchors[p.PieceId] = (byte)p.InitialAnchor;
            ulong m = topology.Groups[p.GroupId].BaseMask << p.InitialAnchor;
            occ |= m;
            key |= SpreadCells(m) * (ulong)(p.GroupId + 1);
        }
        _anchors = anchors;
        OccupancyMask = occ;
        CanonicalShapeKey = key;
    }

    private KlotskiState(KlotskiAnchorBuffer anchors, ulong occ, ulong key)
    {
        _anchors = anchors;
        OccupancyMask = occ;
        CanonicalShapeKey = key;
    }

    public ulong OccupancyMask { get; }
    public ulong CanonicalShapeKey { get; }

    public int AnchorOf(int pieceId) => _anchors[pieceId];

    /// <summary>Moves a piece (already validated) and updates occupancy and the shape key with XORs.</summary>
    public KlotskiState WithPieceMoved(PuzzleTopology topology, int pieceId, int newAnchor)
    {
        int g = topology.Pieces[pieceId].GroupId;
        ulong baseMask = topology.Groups[g].BaseMask;
        ulong oldMask = baseMask << _anchors[pieceId], newMask = baseMask << newAnchor;
        var anchors = _anchors;
        anchors[pieceId] = (byte)newAnchor;
        ulong key = CanonicalShapeKey ^ ((SpreadCells(oldMask) ^ SpreadCells(newMask)) * (ulong)(g + 1));
        return new KlotskiState(anchors, OccupancyMask ^ oldMask ^ newMask, key);
    }

    /// <summary>Maps bit i to bit 3i so multiplying by a class id (1..7) writes it into 3-bit cell fields.</summary>
    public static ulong SpreadCells(ulong mask)
    {
        ulong r = 0;
        while (mask != 0)
        {
            int i = BitOperations.TrailingZeroCount(mask);
            r |= 1UL << (3 * i);
            mask &= mask - 1;
        }
        return r;
    }

    public bool Equals(KlotskiState other) => CanonicalShapeKey == other.CanonicalShapeKey;
    public override bool Equals(object? obj) => obj is KlotskiState s && Equals(s);
    public static bool operator ==(KlotskiState left, KlotskiState right) => left.Equals(right);
    public static bool operator !=(KlotskiState left, KlotskiState right) => !left.Equals(right);
    public override int GetHashCode() => CanonicalShapeKey.GetHashCode();
}

public readonly record struct KlotskiMove(byte Piece, byte From, byte To);

public sealed class KlotskiDomain : ISlidingBlockDomain<KlotskiState, KlotskiMove>
{
    public static readonly string[] ClassicMap =
    [
        "aMMb",
        "aMMb",
        "cHHd",
        "cefd",
        "g..h",
    ];

    public KlotskiDomain(PuzzleTopology topology, int masterPieceId, int goalAnchor)
    {
        Topology = topology;
        MasterPieceId = masterPieceId;
        GoalAnchor = goalAnchor;
        InitialState = new KlotskiState(topology);
    }

    /// <summary>The classic 4x5 layout: one 2x2, four 1x2 vertical, one 2x1 horizontal, four 1x1.</summary>
    public static KlotskiDomain CreateClassic()
    {
        var topo = PuzzleTopology.FromAsciiMap(ClassicMap,
        [
            ('M', new PieceStyle("Master 2x2", "#EF4444", Role: "master")),
            ('a', new PieceStyle("Vertical 1x2", "#3B82F6", Role: "vertical")),
            ('b', new PieceStyle("Vertical 1x2", "#3B82F6", Role: "vertical")),
            ('c', new PieceStyle("Vertical 1x2", "#3B82F6", Role: "vertical")),
            ('d', new PieceStyle("Vertical 1x2", "#3B82F6", Role: "vertical")),
            ('H', new PieceStyle("Horizontal 2x1", "#F59E0B", Role: "horizontal")),
            ('e', new PieceStyle("Single 1x1", "#10B981", Role: "single")),
            ('f', new PieceStyle("Single 1x1", "#10B981", Role: "single")),
            ('g', new PieceStyle("Single 1x1", "#10B981", Role: "single")),
            ('h', new PieceStyle("Single 1x1", "#10B981", Role: "single")),
        ]);
        int master = topo.Pieces.First(p => p.Label == "M").PieceId;
        return new KlotskiDomain(topo, master, 13);
    }

    public PuzzleTopology Topology { get; }
    public int MasterPieceId { get; }
    public int GoalAnchor { get; }
    public KlotskiState InitialState { get; }
    public int MaxMoves => Topology.Pieces.Count * 4;

    public bool IsGoal(in KlotskiState state) => state.AnchorOf(MasterPieceId) == GoalAnchor;

    public int GenerateMoves(in KlotskiState state, Span<KlotskiMove> moveBuffer)
    {
        int w = Topology.Width, n = 0;
        foreach (var p in Topology.Pieces)
        {
            var g = Topology.Groups[p.GroupId];
            int a = state.AnchorOf(p.PieceId);
            ulong other = state.OccupancyMask ^ (g.BaseMask << a);
            int col = a % w;
            n = TryAdd(moveBuffer, n, p.PieceId, a, a - w, g, other, true);
            n = TryAdd(moveBuffer, n, p.PieceId, a, a + w, g, other, true);
            n = TryAdd(moveBuffer, n, p.PieceId, a, a - 1, g, other, col > 0);
            n = TryAdd(moveBuffer, n, p.PieceId, a, a + 1, g, other, col < w - 1);
        }
        return n;
    }

    private static int TryAdd(Span<KlotskiMove> buf, int n, int piece, int from, int to, EquivalenceGroup g, ulong other, bool edgeOk)
    {
        // ValidAnchorsMask replaces sentinel walls; (to >= 0) guards the shift for negative targets.
        if (!edgeOk || to < 0 || to > 63) return n;
        if (((g.ValidAnchorsMask >> to) & 1UL) == 0) return n;
        if (((g.BaseMask << to) & other) != 0) return n;
        buf[n++] = new KlotskiMove((byte)piece, (byte)from, (byte)to);
        return n;
    }

    public KlotskiState ApplyMove(in KlotskiState state, in KlotskiMove move) => state.WithPieceMoved(Topology, move.Piece, move.To);

    public ulong GetHashCode64(in KlotskiState state) => state.CanonicalShapeKey * 0x9E3779B97F4A7C15UL;
}
