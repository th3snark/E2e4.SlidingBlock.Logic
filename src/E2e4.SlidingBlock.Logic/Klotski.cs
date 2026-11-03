using System.Numerics;
using System.Runtime.CompilerServices;

namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// Klotski position: per-piece anchors (for UI identity), the occupancy bitboard (one bit per cell) and the
/// canonical shape key (3 bits per cell, class = groupId + 1; 60 bits on the classic 4x5 board). Equality uses the key only, so swapping
/// identical pieces yields the same state.
/// </summary>
public readonly struct KlotskiState : IEquatable<KlotskiState>
{
    /// <summary>The most cells a board may have: the shape key spends 3 bits on each of them.</summary>
    public const int MaxCells = 21;

    /// <summary>The most pieces a position holds.</summary>
    public const int MaxPieces = 10;

    /// <summary>The most shape classes: the class of a cell is its group id plus one, in 3 bits.</summary>
    public const int MaxShapes = 7;

    private readonly KlotskiAnchorBuffer _anchors;

    /// <summary>Reads the starting position from the pieces of <paramref name="topology"/>.</summary>
    /// <exception cref="ArgumentException">More than 10 pieces, more than 7 shapes, or more than 21 cells (the key has 3 bits per cell in 64).</exception>
    public KlotskiState(PuzzleTopology<Bitboard64> topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (topology.Width * topology.Height > MaxCells) throw new ArgumentException($"At most {MaxCells} cells fit the 64-bit shape key.");
        if (topology.Pieces.Count > MaxPieces) throw new ArgumentException($"At most {MaxPieces} pieces.");
        if (topology.Groups.Count > MaxShapes) throw new ArgumentException($"At most {MaxShapes} shape classes fit in 3 bits.");
        var anchors = new KlotskiAnchorBuffer();
        ulong occ = 0, key = 0;
        foreach (var p in topology.PieceTable)
        {
            anchors[p.PieceId] = (byte)p.InitialAnchor;
            ulong m = topology.GroupTable[p.GroupId].BaseMask.Value << p.InitialAnchor;
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

    /// <summary>Bit c is set when a piece covers cell c.</summary>
    public ulong OccupancyMask { get; }

    /// <summary>Three bits per cell holding the shape class of the piece on it, so equal keys mean the same position up to swapping identical pieces.</summary>
    internal ulong CanonicalShapeKey { get; }

    /// <summary>The anchor cell of piece <paramref name="pieceId"/>.</summary>
    public int AnchorOf(int pieceId) => _anchors[pieceId];

    /// <summary>Moves a piece (already validated) and updates occupancy and the shape key with XORs.</summary>
    public KlotskiState WithPieceMoved(PuzzleTopology<Bitboard64> topology, int pieceId, int newAnchor) =>
        WithPieceMoved(topology, SpreadCells(topology.GroupTable[topology.PieceTable[pieceId].GroupId].BaseMask.Value), pieceId, newAnchor);

    /// <summary>
    /// As <see cref="WithPieceMoved(PuzzleTopology{Bitboard64},int,int)"/> with the spread base mask of the piece's group already at hand:
    /// spreading a mask moved by a cells equals spreading it and shifting by 3a, so no bit loop is needed.
    /// </summary>
    internal KlotskiState WithPieceMoved(PuzzleTopology<Bitboard64> topology, ulong spreadBase, int pieceId, int newAnchor)
    {
        int g = topology.PieceTable[pieceId].GroupId, old = _anchors[pieceId];
        ulong baseMask = topology.GroupTable[g].BaseMask.Value;
        var anchors = _anchors;
        anchors[pieceId] = (byte)newAnchor;
        ulong key = CanonicalShapeKey ^ (((spreadBase << (3 * old)) ^ (spreadBase << (3 * newAnchor))) * (ulong)(g + 1));
        return new KlotskiState(anchors, OccupancyMask ^ (baseMask << old) ^ (baseMask << newAnchor), key);
    }

    /// <summary>Maps bit i to bit 3i so multiplying by a class id (1..7) writes it into 3-bit cell fields.</summary>
    internal static ulong SpreadCells(ulong mask)
    {
        ulong r = 0;
        while (mask != 0)
        {
            int i = BitOps.TrailingZeroCount(mask);
            r |= 1UL << (3 * i);
            mask &= mask - 1;
        }
        return r;
    }

    /// <inheritdoc/>
    public bool Equals(KlotskiState other) => CanonicalShapeKey == other.CanonicalShapeKey;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is KlotskiState s && Equals(s);

    /// <summary>True if both positions have the same shape on every cell, whichever identical piece is where.</summary>
    public static bool operator ==(KlotskiState left, KlotskiState right) => left.Equals(right);

    /// <summary>True if the positions differ.</summary>
    public static bool operator !=(KlotskiState left, KlotskiState right) => !left.Equals(right);

    /// <inheritdoc/>
    public override int GetHashCode() => CanonicalShapeKey.GetHashCode();
}

/// <summary>Piece <see cref="Piece"/> steps one cell from anchor <see cref="From"/> to anchor <see cref="To"/>.</summary>
/// <param name="Piece">The piece id.</param>
/// <param name="From">Anchor cell before the move.</param>
/// <param name="To">Anchor cell after the move.</param>
public readonly record struct KlotskiMove(byte Piece, byte From, byte To);

/// <summary>
/// Klotski rules: pieces of any shape step one cell at a time on a board of at most <see cref="KlotskiState.MaxCells"/> cells. Only walls and
/// the per-shape valid anchors are honoured; diodes, partitions, elevation and plates of the topology are ignored (use
/// <see cref="PolyominoDomain{TBitboard}"/> for those).
/// </summary>
public sealed class KlotskiDomain : ISlidingBlockDomain<KlotskiState, KlotskiMove>
{
    /// <summary>The puzzle is solved when piece <paramref name="masterPieceId"/> sits on <paramref name="goalAnchor"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="masterPieceId"/> is not a piece of the topology.</exception>
    /// <exception cref="ArgumentException">The master piece shares its shape with another piece (the position key could not tell them apart).</exception>
    public KlotskiDomain(PuzzleTopology<Bitboard64> topology, int masterPieceId, int goalAnchor)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentOutOfRangeException.ThrowIfNegative(masterPieceId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(masterPieceId, topology.Pieces.Count);
        int masterGroup = topology.PieceTable[masterPieceId].GroupId;
        foreach (var p in topology.PieceTable)
            if (p.PieceId != masterPieceId && p.GroupId == masterGroup)
                throw new ArgumentException("The master piece must have a shape of its own.", nameof(masterPieceId));
        Topology = topology;
        MasterPieceId = masterPieceId;
        GoalAnchor = goalAnchor;
        InitialState = new KlotskiState(topology);
        _spreadBase = new ulong[topology.GroupTable.Length];
        for (int g = 0; g < _spreadBase.Length; g++) _spreadBase[g] = KlotskiState.SpreadCells(topology.GroupTable[g].BaseMask.Value);
    }

    private readonly ulong[] _spreadBase;

    /// <summary>The board and pieces.</summary>
    public PuzzleTopology<Bitboard64> Topology { get; }

    /// <summary>The piece that has to reach <see cref="GoalAnchor"/>.</summary>
    public int MasterPieceId { get; }

    /// <summary>The anchor cell that solves the puzzle for the master piece.</summary>
    public int GoalAnchor { get; }

    /// <inheritdoc/>
    public KlotskiState InitialState { get; }

    /// <inheritdoc/>
    public int MaxMoves => Topology.Pieces.Count * 4;

    /// <inheritdoc/>
    public bool IsGoal(in KlotskiState state) => state.AnchorOf(MasterPieceId) == GoalAnchor;

    /// <inheritdoc/>
    public int GenerateMoves(in KlotskiState state, Span<KlotskiMove> moveBuffer)
    {
        int w = Topology.Width, n = 0;
        foreach (var p in Topology.PieceTable)
        {
            var g = Topology.GroupTable[p.GroupId];
            int a = state.AnchorOf(p.PieceId);
            ulong other = state.OccupancyMask ^ (g.BaseMask.Value << a);
            int col = a % w;
            n = TryAdd(moveBuffer, n, p.PieceId, a, a - w, g, other, true);
            n = TryAdd(moveBuffer, n, p.PieceId, a, a + w, g, other, true);
            n = TryAdd(moveBuffer, n, p.PieceId, a, a - 1, g, other, col > 0);
            n = TryAdd(moveBuffer, n, p.PieceId, a, a + 1, g, other, col < w - 1);
        }
        return n;
    }

    private static int TryAdd(Span<KlotskiMove> buf, int n, int piece, int from, int to, EquivalenceGroup<Bitboard64> g, ulong other, bool edgeOk)
    {
        // ValidAnchorsMask replaces sentinel walls; (to >= 0) guards the shift for negative targets.
        if (!edgeOk || to < 0 || to > 63) return n;
        if (((g.ValidAnchorsMask >> to) & 1UL) == 0) return n;
        if (((g.BaseMask.Value << to) & other) != 0) return n;
        buf[n++] = new KlotskiMove((byte)piece, (byte)from, (byte)to);
        return n;
    }

    /// <inheritdoc/>
    public KlotskiState ApplyMove(in KlotskiState state, in KlotskiMove move) =>
        state.WithPieceMoved(Topology, _spreadBase[Topology.PieceTable[move.Piece].GroupId], move.Piece, move.To);

    /// <inheritdoc/>
    public ulong GetHashCode64(in KlotskiState state) => Hashing.Mix(state.CanonicalShapeKey);
}
