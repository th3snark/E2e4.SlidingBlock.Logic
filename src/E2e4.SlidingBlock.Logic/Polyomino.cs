using System.Numerics;
using System.Runtime.CompilerServices;

namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// Polyomino position. Equality and hashing use only the canonical <see cref="GroupAnchorsOf"/> bitboards and the
/// permutation-invariant Zobrist hash, so swapping identical pieces never changes the state's identity.
/// <see cref="AnchorOf"/> remembers which piece sits where for UI selection/animation only.
/// </summary>
public struct PolyominoState<TBitboard> : IEquatable<PolyominoState<TBitboard>>
    where TBitboard : unmanaged, IBitboard<TBitboard>
{
    /// <summary>The most equivalence groups a position can hold.</summary>
    public const int MaxGroups = 16;

    /// <summary>The most pieces a position can hold.</summary>
    public const int MaxPieces = 16;

#pragma warning disable CS0649 // filled through the buffers' indexers
    private GroupAnchorBuffer<TBitboard> _groupAnchors;
    private PieceAnchorBuffer _pieceAnchors;
    private PieceAnchorBuffer _pieceGroups;
#pragma warning restore CS0649

    /// <summary>The position with every piece of <paramref name="topology"/> on its initial anchor.</summary>
    /// <exception cref="ArgumentException">The topology has more than <see cref="MaxGroups"/> groups or <see cref="MaxPieces"/> pieces.</exception>
    public static PolyominoState<TBitboard> FromTopology(PuzzleTopology<TBitboard> topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (topology.Groups.Count > MaxGroups) throw new ArgumentException($"At most {MaxGroups} equivalence groups.");
        if (topology.Pieces.Count > MaxPieces) throw new ArgumentException($"At most {MaxPieces} pieces.");
        var s = new PolyominoState<TBitboard>();
        foreach (var p in topology.Pieces)
        {
            var group = topology.Groups[p.GroupId];
            s._pieceAnchors[p.PieceId] = (byte)p.InitialAnchor;
            s._pieceGroups[p.PieceId] = (byte)p.GroupId;
            s._groupAnchors[p.GroupId] = s._groupAnchors[p.GroupId].Or(Bit<TBitboard>.One.ShiftLeft(p.InitialAnchor));
            s.ToggleLayers(group.ElevationMask, group.BaseMask.ShiftLeft(p.InitialAnchor));
            s.Hash ^= topology.ZobristTable[p.GroupId, p.InitialAnchor];
        }
        return s;
    }

    /// <summary>Occupancy per elevation layer; <see cref="OccupancyMask"/> is their union.</summary>
    internal LayerOccupancyBuffer<TBitboard> LayerOccupancy { get; private set; }

    /// <summary>The cells covered on either layer.</summary>
    public readonly TBitboard OccupancyMask => LayerOccupancy[0].Or(LayerOccupancy[1]);

    /// <summary>The Zobrist hash of the position, kept up to date as pieces move. Identical pieces can be swapped without changing it.</summary>
    public ulong Hash { get; private set; }

    /// <summary>
    /// The Zobrist hash computed from scratch from the pieces. <see cref="Hash"/> is kept up to date incrementally; the two agree
    /// for every position, which makes this the check for the incremental updates.
    /// </summary>
    internal readonly ulong ComputeZobrist(PuzzleTopology<TBitboard> topology)
    {
        ulong h = 0;
        foreach (var p in topology.PieceTable) h ^= topology.ZobristTable[_pieceGroups[p.PieceId], _pieceAnchors[p.PieceId]];
        return h;
    }

    /// <summary>The piece covering <paramref name="cell"/>, or -1 if the cell is empty or a wall.</summary>
    public readonly int PieceAt(PuzzleTopology<TBitboard> topology, int cell)
    {
        foreach (var p in topology.PieceTable)
            if (Grid.Has(topology.GroupTable[_pieceGroups[p.PieceId]].BaseMask.ShiftLeft(_pieceAnchors[p.PieceId]), cell)) return p.PieceId;
        return -1;
    }

    /// <summary>The lowest cell that holds neither a piece nor a wall, or -1 if the board is full.</summary>
    public readonly int FirstFreeCell(PuzzleTopology<TBitboard> topology)
    {
        TBitboard free = OccupancyMask.Or(topology.StaticWallsMask).Not();
        int cell = Grid.Lsb(free);
        return cell < topology.CellCount ? cell : -1;
    }

    /// <summary>Bit c is set when a piece of group <paramref name="groupId"/> is anchored on cell c.</summary>
    public readonly TBitboard GroupAnchorsOf(int groupId) => _groupAnchors[groupId];
    /// <summary>The cell the first square of piece <paramref name="pieceId"/> stands on.</summary>
    public readonly int AnchorOf(int pieceId) => _pieceAnchors[pieceId];

    /// <summary>The group a piece currently belongs to (changes when a piece rotates).</summary>
    public readonly int GroupOf(int pieceId) => _pieceGroups[pieceId];

    /// <summary>Occupancy of the layers in <paramref name="elevationMask"/> with <paramref name="selfMask"/> (the mover's own footprint) removed.</summary>
    public readonly TBitboard GetRelevantOccupancy(byte elevationMask, TBitboard selfMask)
    {
        TBitboard occ = Bit<TBitboard>.Zero;
        if ((elevationMask & 0b01) != 0) occ = occ.Or(LayerOccupancy[0]);
        if ((elevationMask & 0b10) != 0) occ = occ.Or(LayerOccupancy[1]);
        return occ.Xor(selfMask);
    }

    private void ToggleLayers(byte elevationMask, TBitboard mask)
    {
        var layers = LayerOccupancy;
        if ((elevationMask & 0b01) != 0) layers[0] = layers[0].Xor(mask);
        if ((elevationMask & 0b10) != 0) layers[1] = layers[1].Xor(mask);
        LayerOccupancy = layers;
    }

    /// <summary>Moves one piece to <paramref name="newAnchor"/> (caller validated) with O(1) incremental updates.</summary>
    public readonly PolyominoState<TBitboard> WithPieceMoved(PuzzleTopology<TBitboard> topology, int pieceId, int newAnchor) =>
        WithPieceChanged(topology, pieceId, _pieceGroups[pieceId], newAnchor);

    /// <summary>Moves a piece to <paramref name="newAnchor"/> and into <paramref name="newGroup"/> (a rotation), updating hash and occupancy in O(1).</summary>
    public readonly PolyominoState<TBitboard> WithPieceChanged(PuzzleTopology<TBitboard> topology, int pieceId, int newGroup, int newAnchor)
    {
        var s = this;
        s.Change(topology, pieceId, newGroup, newAnchor);
        return s;
    }

    /// <summary>Applies several piece changes at once (a push, a carry or a magnetic cluster).</summary>
    public readonly PolyominoState<TBitboard> WithDeltas(PuzzleTopology<TBitboard> topology, ReadOnlySpan<PieceDelta> deltas)
    {
        var s = this;
        foreach (var d in deltas) s.Change(topology, d.PieceId, d.NewGroupId, d.NewAnchor);
        return s;
    }

    private void Change(PuzzleTopology<TBitboard> topology, int pieceId, int newGroup, int newAnchor)
    {
        int oldGroup = _pieceGroups[pieceId], old = _pieceAnchors[pieceId];
        var og = topology.GroupTable[oldGroup];
        var ng = topology.GroupTable[newGroup];
        _pieceAnchors[pieceId] = (byte)newAnchor;
        _pieceGroups[pieceId] = (byte)newGroup;
        _groupAnchors[oldGroup] = _groupAnchors[oldGroup].Xor(Bit<TBitboard>.One.ShiftLeft(old));
        _groupAnchors[newGroup] = _groupAnchors[newGroup].Xor(Bit<TBitboard>.One.ShiftLeft(newAnchor));
        ToggleLayers(og.ElevationMask, og.BaseMask.ShiftLeft(old));
        ToggleLayers(ng.ElevationMask, ng.BaseMask.ShiftLeft(newAnchor));
        Hash ^= topology.ZobristTable[oldGroup, old] ^ topology.ZobristTable[newGroup, newAnchor];
    }

    /// <inheritdoc/>
    public readonly bool Equals(PolyominoState<TBitboard> other)
    {
        if (Hash != other.Hash) return false;
        for (int g = 0; g < GroupAnchorBuffer<TBitboard>.Length; g++)
            if (!_groupAnchors[g].Equals(other._groupAnchors[g])) return false;
        return true;
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is PolyominoState<TBitboard> s && Equals(s);
    /// <inheritdoc/>
    public override readonly int GetHashCode() => (int)(Hash ^ (Hash >> 32));
    /// <summary>True if both positions have the same groups on the same anchors.</summary>
    public static bool operator ==(PolyominoState<TBitboard> left, PolyominoState<TBitboard> right) => left.Equals(right);
    /// <summary>True if the positions differ.</summary>
    public static bool operator !=(PolyominoState<TBitboard> left, PolyominoState<TBitboard> right) => !left.Equals(right);

}

/// <summary>Moves a piece to <see cref="TargetAnchor"/> in one turn (slide, hook or glide).</summary>
public readonly record struct PolyominoMove(byte PieceId, byte TargetAnchor);

/// <summary>Move generation shared by the polyomino domains.</summary>
internal static class MoveGen<TBitboard> where TBitboard : unmanaged, IBitboard<TBitboard>
{
    /// <summary>Solid gate cells right now: closed gates of every pressure-plate rule.</summary>
    public static TBitboard ActiveDynamicWalls(PuzzleTopology<TBitboard> topology, in PolyominoState<TBitboard> state)
    {
        TBitboard walls = Bit<TBitboard>.Zero;
        foreach (var rule in topology.PlateTable)
        {
            TBitboard triggerOcc = state.GetRelevantOccupancy(rule.TriggerElevationMask, Bit<TBitboard>.Zero);
            bool isPressed = triggerOcc.Intersects(rule.TriggerCellsMask);
            bool gateClosed = rule.NormallyClosed ? !isPressed : isPressed;
            if (gateClosed) walls = walls.Or(rule.GateCellsMask);
        }
        return walls;
    }

    /// <summary>True when the move leaves a gate closed on a piece that was not under a closed gate before. <paramref name="wallsBefore"/> is <see cref="ActiveDynamicWalls"/> of <paramref name="before"/>.</summary>
    public static bool Crushes(PuzzleTopology<TBitboard> topology, TBitboard wallsBefore, in PolyominoState<TBitboard> before, in PolyominoState<TBitboard> after)
    {
        if (topology.PlateTable.Length == 0) return false;
        TBitboard now = after.OccupancyMask.And(ActiveDynamicWalls(topology, after));
        TBitboard was = before.OccupancyMask.And(wallsBefore);
        return !now.AndNot(was).IsEmpty;
    }

    public static TBitboard OtherOccupancy(PuzzleTopology<TBitboard> topology, in PolyominoState<TBitboard> state, EquivalenceGroup<TBitboard> g, int anchor,
        TBitboard walls) => state.GetRelevantOccupancy(g.ElevationMask, g.BaseMask.ShiftLeft(anchor)).Or(walls);

    /// <summary>Anchors the piece may move to in one turn according to its group's <see cref="SlidePhysics"/>.</summary>
    public static TBitboard Reachable(PuzzleTopology<TBitboard> topology, in PolyominoState<TBitboard> state, int pieceId, TBitboard walls)
    {
        var g = topology.GroupTable[state.GroupOf(pieceId)];
        int a = state.AnchorOf(pieceId);
        TBitboard other = OtherOccupancy(topology, state, g, a, walls);
        if (g.SlidePhysics == SlidePhysics.ContinuousFloodFill)
        {
            ReadOnlySpan<TBitboard> byDir = topology.HasEntryRules ? topology.ValidByDirRow(g.GroupId) : default;
            return PolyominoFloodFill.Flood(a, g.BaseMask, g.ValidAnchorsMask, other, g.Directions, topology.DeltaTable, topology.EntryTable, byDir);
        }

        TBitboard result = Bit<TBitboard>.Zero;
        for (int d = 0; d < topology.DirectionCount; d++)
        {
            if (!Grid.IsAllowed(g.Directions, d)) continue;
            int end = Glide(topology, g, d, a, other, g.SlidePhysics == SlidePhysics.SingleStep ? 1 : int.MaxValue, out _, out _, out _);
            if (end != a) result = result.Or(Bit<TBitboard>.One.ShiftLeft(end));
        }
        return result;
    }

    /// <summary>The static wall cells the group's footprint would cover on <paramref name="anchor"/>; empty when it merely leaves the board.</summary>
    private static TBitboard WallCells(PuzzleTopology<TBitboard> topology, EquivalenceGroup<TBitboard> g, int anchor)
    {
        TBitboard hit = Bit<TBitboard>.Zero;
        int w = topology.Columns, r0 = anchor / w, c0 = anchor % w;
        for (int i = 0; i < g.RelativeCells.Count; i++)
        {
            var (dr, dc) = g.RelativeCells[i];
            int r = r0 + dr, c = c0 + dc;
            if ((uint)r >= (uint)topology.Rows || (uint)c >= (uint)w) continue;
            TBitboard cell = Bit<TBitboard>.One.ShiftLeft(r * w + c);
            if (topology.StaticWallsMask.Intersects(cell)) hit = hit.Or(cell);
        }
        return hit;
    }

    /// <summary>Slides from <paramref name="start"/> along <paramref name="dir"/> until blocked (or <paramref name="maxSteps"/>); returns the final anchor.</summary>
    public static int Glide(PuzzleTopology<TBitboard> topology, EquivalenceGroup<TBitboard> g, int dir, int start, TBitboard other, int maxSteps,
        out int steps, out TBitboard swept, out TBitboard blocking)
    {
        int curr = start, cells = topology.CellCount, delta = Grid.Delta(topology.Columns, dir);
        steps = 0;
        swept = g.BaseMask.ShiftLeft(start);
        blocking = Bit<TBitboard>.Zero;
        while (steps < maxSteps)
        {
            if (!Grid.StepStaysOnBoard(curr, dir, topology.Columns, cells)) break;
            int next = curr + delta;
            if (!Grid.Has(topology.ValidAnchors(g.GroupId, dir), next))
            {
                // A wall under the footprint, or else the cells it would enter across a partition or against a diode.
                blocking = WallCells(topology, g, next);
                if (blocking.IsEmpty)
                    blocking = g.BaseMask.ShiftLeft(next).AndNot(g.BaseMask.ShiftLeft(curr)).And(topology.ForbiddenEntry(dir));
                break;
            }
            TBitboard footprint = g.BaseMask.ShiftLeft(next);
            TBitboard hit = other.And(footprint);
            if (!hit.IsEmpty) { blocking = hit; break; }
            curr = next;
            steps++;
            swept = swept.Or(footprint);
        }
        return curr;
    }
}

/// <summary>
/// The rules of a polyomino puzzle: pieces of any shape slide through free cells (one cell, any distance or until blocked, as their group says)
/// on a board with walls, one-way cells, partitions, elevation layers and pressure plates. Pieces of one group are interchangeable.
/// </summary>
/// <typeparam name="TBitboard"><see cref="ulong"/> for boards of up to 64 cells, <c>UInt128</c> for up to 128.</typeparam>
public sealed class PolyominoDomain<TBitboard> : ISlidingBlockDomain<PolyominoState<TBitboard>, PolyominoMove>
    where TBitboard : unmanaged, IBitboard<TBitboard>
{
    /// <summary>The puzzle is solved when group <paramref name="goalGroupId"/> has exactly one piece, on <paramref name="goalAnchor"/>.</summary>
    public PolyominoDomain(PuzzleTopology<TBitboard> topology, int goalGroupId, int goalAnchor)
        : this(topology, new PolyominoGoal<TBitboard>(goalGroupId, goalAnchor))
    {
    }

    /// <summary>
    /// The puzzle is solved when the position equals <paramref name="goal"/>. Pieces of one group are interchangeable, so only which
    /// group sits where matters (see <see cref="PolyominoState{TBitboard}"/>). Build the goal with <see cref="PolyominoState{TBitboard}.FromTopology"/> from a
    /// topology that has the same groups, in the same order, as <paramref name="topology"/>. Bit g of <paramref name="matchedGroups"/>
    /// says whether group g has to match the goal; groups left out may end up anywhere.
    /// </summary>
    public PolyominoDomain(PuzzleTopology<TBitboard> topology, PolyominoState<TBitboard> goal, ulong matchedGroups = ulong.MaxValue)
        : this(topology, new PolyominoGoal<TBitboard>(goal, matchedGroups))
    {
    }

    /// <summary>The puzzle is solved when <paramref name="goal"/> is reached.</summary>
    public PolyominoDomain(PuzzleTopology<TBitboard> topology, PolyominoGoal<TBitboard> goal)
    {
        Topology = topology;
        Goal = goal;
        InitialState = PolyominoState<TBitboard>.FromTopology(topology);
    }

    /// <summary>When the puzzle is solved.</summary>
    public PolyominoGoal<TBitboard> Goal { get; }

    /// <summary>The board and pieces.</summary>
    public PuzzleTopology<TBitboard> Topology { get; }

    /// <inheritdoc/>
    public PolyominoState<TBitboard> InitialState { get; }
    /// <inheritdoc/>
    public int MaxMoves => Topology.Pieces.Count * Topology.CellCount;

    /// <inheritdoc/>
    public bool IsGoal(in PolyominoState<TBitboard> state) => Goal.IsReached(state);

    /// <summary>The cells closed gates make solid in <paramref name="state"/>.</summary>
    public TBitboard ActiveDynamicWalls(in PolyominoState<TBitboard> state) => MoveGen<TBitboard>.ActiveDynamicWalls(Topology, state);

    /// <summary>The cells piece <paramref name="pieceId"/> can slide to in one move from where it stands.</summary>
    public TBitboard ReachableFor(in PolyominoState<TBitboard> state, int pieceId) =>
        MoveGen<TBitboard>.Reachable(Topology, state, pieceId, MoveGen<TBitboard>.ActiveDynamicWalls(Topology, state));

    /// <summary>The cells a piece passes through on its shortest way to <paramref name="targetAnchor"/> (start and target included), empty if it cannot get there.</summary>
    public IReadOnlyList<int> PathFor(in PolyominoState<TBitboard> state, int pieceId, int targetAnchor)
    {
        var g = Topology.GroupTable[state.GroupOf(pieceId)];
        int a = state.AnchorOf(pieceId);
        TBitboard other = MoveGen<TBitboard>.OtherOccupancy(Topology, state, g, a, ActiveDynamicWalls(state));
        ReadOnlySpan<TBitboard> byDir = Topology.HasEntryRules ? Topology.ValidByDirRow(g.GroupId) : default;
        return PolyominoFloodFill.ReconstructHookPath(a, targetAnchor, g.BaseMask, g.ValidAnchorsMask, other, Topology.Columns, g.Directions,
            Topology.Geometry, byDir);
    }

    /// <summary>
    /// Records the glide of a piece in every direction its group allows (start, terminal anchor, steps, swept footprint cells and the bits that stopped it).
    /// Returns the number of traces written.
    /// </summary>
    public int TraceRaycast(in PolyominoState<TBitboard> state, int pieceId, Span<RaycastTrace<TBitboard>> results)
    {
        var g = Topology.GroupTable[state.GroupOf(pieceId)];
        int a = state.AnchorOf(pieceId), n = 0;
        TBitboard other = MoveGen<TBitboard>.OtherOccupancy(Topology, state, g, a, ActiveDynamicWalls(state));
        for (int d = 0; d < Topology.DirectionCount && n < results.Length; d++)
        {
            if (!Grid.IsAllowed(g.Directions, d)) continue;
            int end = MoveGen<TBitboard>.Glide(Topology, g, d, a, other, int.MaxValue, out int steps, out var swept, out var blocking);
            results[n++] = new RaycastTrace<TBitboard>((GridDirection)d, a, end, steps, swept, blocking);
        }
        return n;
    }

    /// <inheritdoc/>
    public int GenerateMoves(in PolyominoState<TBitboard> state, Span<PolyominoMove> moveBuffer)
    {
        int n = 0;
        TBitboard walls = MoveGen<TBitboard>.ActiveDynamicWalls(Topology, state);
        var pieces = Topology.PieceTable;
        for (int i = 0; i < pieces.Length; i++)
        {
            int pieceId = pieces[i].PieceId;
            foreach (int target in Grid.Bits(MoveGen<TBitboard>.Reachable(Topology, state, pieceId, walls)))
            {
                if (Topology.PlateTable.Length > 0 && MoveGen<TBitboard>.Crushes(Topology, walls, state, state.WithPieceMoved(Topology, pieceId, target))) continue;
                moveBuffer[n++] = new PolyominoMove((byte)pieceId, (byte)target);
            }
        }
        return n;
    }

    /// <inheritdoc/>
    public PolyominoState<TBitboard> ApplyMove(in PolyominoState<TBitboard> state, in PolyominoMove move) =>
        state.WithPieceMoved(Topology, move.PieceId, move.TargetAnchor);

    /// <inheritdoc/>
    public ulong GetHashCode64(in PolyominoState<TBitboard> state) => state.Hash;
}
