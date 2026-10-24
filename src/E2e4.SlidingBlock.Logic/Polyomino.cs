using System.Numerics;
using System.Runtime.CompilerServices;

namespace E2e4.SlidingBlock.Logic;

[InlineArray(Length)]
public struct GroupAnchorBuffer<TBitboard> where TBitboard : unmanaged
{
    /// <summary>The most equivalence groups a <see cref="PolyominoState{TBitboard}"/> can hold.</summary>
    public const int Length = 16;

    private TBitboard _element0;
}

[InlineArray(Length)]
public struct PieceAnchorBuffer
{
    /// <summary>The most pieces a <see cref="PolyominoState{TBitboard}"/> can hold.</summary>
    public const int Length = 16;

    private byte _element0;
}

/// <summary>Occupancy of the two elevation layers (0 = ground, 1 = upper).</summary>
[InlineArray(2)]
public struct LayerOccupancyBuffer<T> where T : unmanaged
{
    private T _layer0;
}

/// <summary>
/// Polyomino position. Equality and hashing use only the canonical <see cref="GroupAnchorsOf"/> bitboards and the
/// permutation-invariant Zobrist hash, so swapping identical pieces never changes the state's identity.
/// <see cref="AnchorOf"/> remembers which piece sits where for UI selection/animation only.
/// </summary>
public struct PolyominoState<TBitboard> : IEquatable<PolyominoState<TBitboard>>
    where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>
{
    private GroupAnchorBuffer<TBitboard> _groupAnchors;
    private PieceAnchorBuffer _pieceAnchors;
    private PieceAnchorBuffer _pieceGroups;

    public static PolyominoState<TBitboard> FromTopology(PuzzleTopology<TBitboard> topology)
    {
        if (topology.Groups.Count > GroupAnchorBuffer<TBitboard>.Length) throw new ArgumentException($"At most {GroupAnchorBuffer<TBitboard>.Length} equivalence groups.");
        if (topology.Pieces.Count > PieceAnchorBuffer.Length) throw new ArgumentException($"At most {PieceAnchorBuffer.Length} pieces.");
        var s = new PolyominoState<TBitboard>();
        foreach (var p in topology.Pieces)
        {
            var group = topology.Groups[p.GroupId];
            s._pieceAnchors[p.PieceId] = (byte)p.InitialAnchor;
            s._pieceGroups[p.PieceId] = (byte)p.GroupId;
            s._groupAnchors[p.GroupId] |= TBitboard.One << p.InitialAnchor;
            s.ToggleLayers(group.ElevationMask, group.BaseMask << p.InitialAnchor);
            s.ZobristHash ^= topology.ZobristTable[p.GroupId, p.InitialAnchor];
        }
        return s;
    }

    /// <summary>Occupancy per elevation layer; <see cref="TotalOccupancy"/> is their union.</summary>
    public LayerOccupancyBuffer<TBitboard> LayerOccupancy { get; private set; }

    public readonly TBitboard TotalOccupancy => LayerOccupancy[0] | LayerOccupancy[1];

    /// <summary>All occupied cells whatever the layer (same as <see cref="TotalOccupancy"/>).</summary>
    public readonly TBitboard OccupancyMask => TotalOccupancy;

    public ulong ZobristHash { get; private set; }

    public readonly TBitboard GroupAnchorsOf(int groupId) => _groupAnchors[groupId];
    public readonly int AnchorOf(int pieceId) => _pieceAnchors[pieceId];

    /// <summary>The group a piece currently belongs to (changes when a piece rotates).</summary>
    public readonly int GroupOf(int pieceId) => _pieceGroups[pieceId];

    /// <summary>Occupancy of the layers in <paramref name="elevationMask"/> with <paramref name="selfMask"/> (the mover's own footprint) removed.</summary>
    public readonly TBitboard GetRelevantOccupancy(byte elevationMask, TBitboard selfMask)
    {
        TBitboard occ = TBitboard.Zero;
        if ((elevationMask & 0b01) != 0) occ |= LayerOccupancy[0];
        if ((elevationMask & 0b10) != 0) occ |= LayerOccupancy[1];
        return occ ^ selfMask;
    }

    private void ToggleLayers(byte elevationMask, TBitboard mask)
    {
        var layers = LayerOccupancy;
        if ((elevationMask & 0b01) != 0) layers[0] ^= mask;
        if ((elevationMask & 0b10) != 0) layers[1] ^= mask;
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
        var og = topology.Groups[oldGroup];
        var ng = topology.Groups[newGroup];
        _pieceAnchors[pieceId] = (byte)newAnchor;
        _pieceGroups[pieceId] = (byte)newGroup;
        _groupAnchors[oldGroup] ^= TBitboard.One << old;
        _groupAnchors[newGroup] ^= TBitboard.One << newAnchor;
        ToggleLayers(og.ElevationMask, og.BaseMask << old);
        ToggleLayers(ng.ElevationMask, ng.BaseMask << newAnchor);
        ZobristHash ^= topology.ZobristTable[oldGroup, old] ^ topology.ZobristTable[newGroup, newAnchor];
    }

    public readonly bool Equals(PolyominoState<TBitboard> other)
    {
        if (ZobristHash != other.ZobristHash) return false;
        for (int g = 0; g < GroupAnchorBuffer<TBitboard>.Length; g++)
            if (_groupAnchors[g] != other._groupAnchors[g]) return false;
        return true;
    }

    public override readonly bool Equals(object? obj) => obj is PolyominoState<TBitboard> s && Equals(s);
    public override readonly int GetHashCode() => (int)(ZobristHash ^ (ZobristHash >> 32));
    public static bool operator ==(PolyominoState<TBitboard> left, PolyominoState<TBitboard> right) => left.Equals(right);
    public static bool operator !=(PolyominoState<TBitboard> left, PolyominoState<TBitboard> right) => !left.Equals(right);

    private static TBitboard FreeAnchors(int startAnchor, TBitboard baseMask, TBitboard validAnchorsMask, TBitboard otherOccupancy)
    {
        TBitboard start = TBitboard.One << startAnchor;
        TBitboard free = start;
        for (TBitboard v = validAnchorsMask; v != TBitboard.Zero; v &= v - TBitboard.One)
        {
            int a = Grid.Lsb(v);
            if (((baseMask << a) & otherOccupancy) == TBitboard.Zero) free |= TBitboard.One << a;
        }
        return free & (validAnchorsMask | start);
    }

    private static int Prepare(int width, AllowedDirections allowed, GridGeometry geometry, Span<int> dirs, Span<int> deltas, Span<TBitboard> entry)
    {
        int n = 0;
        for (int d = 0; d < Grid.DirectionCount(geometry); d++)
        {
            if (!Grid.IsAllowed(allowed, d)) continue;
            dirs[n] = d;
            deltas[n] = Grid.Delta(width, d);
            entry[n] = Grid.EntryMask<TBitboard>(d, width);
            n++;
        }
        return n;
    }

    private static TBitboard StepMask(TBitboard frontier, int slot, ReadOnlySpan<int> dirs, ReadOnlySpan<int> deltas, ReadOnlySpan<TBitboard> entry,
        ReadOnlySpan<TBitboard> validByDir)
    {
        TBitboard step = Grid.Shift(frontier, deltas[slot]) & entry[slot];
        if (!validByDir.IsEmpty) step &= validByDir[dirs[slot]];
        return step;
    }

    /// <summary>
    /// Register-only BFS flood fill over anchors. Returns every anchor (excluding the start) reachable by
    /// sliding one square at a time, including multi-step corner "hook" paths. Entry masks stop shifts
    /// from wrapping across rows. <paramref name="validByDir"/> (one mask per direction) restricts which anchors a step may enter, e.g. for diodes.
    /// </summary>
    public static TBitboard FindReachableAnchors(int startAnchor, TBitboard baseMask, TBitboard validAnchorsMask, TBitboard otherOccupancy, int boardWidth,
        AllowedDirections directions = AllowedDirections.Both, GridGeometry geometry = GridGeometry.Orthogonal4Way, ReadOnlySpan<TBitboard> validByDir = default)
    {
        TBitboard free = FreeAnchors(startAnchor, baseMask, validAnchorsMask, otherOccupancy);
        Span<int> dirs = stackalloc int[Grid.MaxDirections], deltas = stackalloc int[Grid.MaxDirections];
        Span<TBitboard> entry = stackalloc TBitboard[Grid.MaxDirections];
        int n = Prepare(boardWidth, directions, geometry, dirs, deltas, entry);

        TBitboard start = TBitboard.One << startAnchor;
        TBitboard visitedOrInvalid = ~free | start;
        TBitboard frontier = start, reachable = TBitboard.Zero;
        while (frontier != TBitboard.Zero)
        {
            TBitboard next = TBitboard.Zero;
            for (int i = 0; i < n; i++) next |= StepMask(frontier, i, dirs, deltas, entry, validByDir);
            next &= ~visitedOrInvalid;
            visitedOrInvalid |= next;
            reachable |= next;
            frontier = next;
        }
        return reachable;
    }

    /// <summary>
    /// Flood fill that also records the shortest-path tree. <paramref name="parentMap"/> (length at least the cell count) is filled with the predecessor anchor of every reachable anchor (-1 elsewhere, the start points at itself).
    /// Destinations whose shortest path keeps one axis are returned in <paramref name="straightMovesMask"/>, those that turn a corner in
    /// <paramref name="hookMovesMask"/>. The result is the union of the two.
    /// </summary>
    public static TBitboard FindReachableAnchors(int startAnchor, TBitboard baseMask, TBitboard validAnchorsMask, TBitboard otherOccupancy, int boardWidth,
        Span<sbyte> parentMap, out TBitboard straightMovesMask, out TBitboard hookMovesMask, AllowedDirections directions = AllowedDirections.Both,
        GridGeometry geometry = GridGeometry.Orthogonal4Way, ReadOnlySpan<TBitboard> validByDir = default)
    {
        TBitboard free = FreeAnchors(startAnchor, baseMask, validAnchorsMask, otherOccupancy);
        Span<int> dirs = stackalloc int[Grid.MaxDirections], deltas = stackalloc int[Grid.MaxDirections];
        Span<TBitboard> entry = stackalloc TBitboard[Grid.MaxDirections];
        int n = Prepare(boardWidth, directions, geometry, dirs, deltas, entry);
        int bits = Grid.BitWidth<TBitboard>();

        parentMap.Fill(-1);
        parentMap[startAnchor] = (sbyte)startAnchor;
        TBitboard startBit = TBitboard.One << startAnchor;
        TBitboard visitedOrInvalid = ~free | startBit;
        TBitboard frontier = startBit, reachable = TBitboard.Zero;
        while (frontier != TBitboard.Zero)
        {
            TBitboard next = TBitboard.Zero;
            for (int i = 0; i < n; i++) next |= StepMask(frontier, i, dirs, deltas, entry, validByDir);
            next &= ~visitedOrInvalid;
            visitedOrInvalid |= next;
            reachable |= next;
            for (TBitboard m = next; m != TBitboard.Zero; m &= m - TBitboard.One)
            {
                int a = Grid.Lsb(m);
                TBitboard abit = TBitboard.One << a;
                int parent = int.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    int src = a - deltas[i];
                    if ((uint)src >= (uint)bits || ((frontier >> src) & TBitboard.One) == TBitboard.Zero) continue;
                    if ((StepMask(TBitboard.One << src, i, dirs, deltas, entry, validByDir) & abit) == TBitboard.Zero) continue;
                    if (src < parent) parent = src;
                }
                parentMap[a] = (sbyte)parent;
            }
            frontier = next;
        }

        straightMovesMask = TBitboard.Zero;
        hookMovesMask = TBitboard.Zero;
        for (TBitboard r = reachable; r != TBitboard.Zero; r &= r - TBitboard.One)
        {
            int dest = Grid.Lsb(r);
            int firstAxis = -1;
            bool hook = false;
            for (int a = dest; a != startAnchor; a = parentMap[a])
            {
                int axis = AxisOfStep(a - parentMap[a], boardWidth, geometry);
                if (firstAxis < 0) firstAxis = axis;
                else if (axis != firstAxis) hook = true;
            }
            if (hook) hookMovesMask |= TBitboard.One << dest; else straightMovesMask |= TBitboard.One << dest;
        }
        return reachable;
    }

    private static int AxisOfStep(int diff, int width, GridGeometry geometry)
    {
        for (int d = 0; d < Grid.DirectionCount(geometry); d++)
            if (Grid.Delta(width, d) == diff) return Grid.Axis(d);
        return 1;
    }

    /// <summary>Shortest step-by-step anchor trajectory (start..target inclusive) or empty if unreachable.</summary>
    public static IReadOnlyList<int> ReconstructHookPath(int startAnchor, int targetAnchor, TBitboard baseMask, TBitboard validAnchorsMask,
        TBitboard otherOccupancy, int boardWidth, AllowedDirections directions = AllowedDirections.Both,
        GridGeometry geometry = GridGeometry.Orthogonal4Way, ReadOnlySpan<TBitboard> validByDir = default)
    {
        if (startAnchor == targetAnchor) return [startAnchor];
        TBitboard free = FreeAnchors(startAnchor, baseMask, validAnchorsMask, otherOccupancy);
        int bits = Grid.BitWidth<TBitboard>();
        Span<int> parent = stackalloc int[128];
        parent.Fill(-1);
        Span<int> queue = stackalloc int[128];
        int head = 0, tail = 0;
        queue[tail++] = startAnchor;
        parent[startAnchor] = startAnchor;
        int cells = Math.Min(bits, 128);

        while (head < tail)
        {
            int a = queue[head++];
            for (int d = 0; d < Grid.DirectionCount(geometry); d++)
            {
                if (!Grid.IsAllowed(directions, d) || !Grid.StepStaysOnBoard(a, d, boardWidth, cells)) continue;
                int n = a + Grid.Delta(boardWidth, d);
                if (parent[n] != -1 || ((free >> n) & TBitboard.One) == TBitboard.Zero) continue;
                if (!validByDir.IsEmpty && ((validByDir[d] >> n) & TBitboard.One) == TBitboard.Zero) continue;
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

/// <summary>Moves a piece to <see cref="TargetAnchor"/> in one turn (slide, hook or glide).</summary>
public readonly record struct PolyominoMove(byte PieceId, byte TargetAnchor);

/// <summary>Move generation shared by the polyomino domains.</summary>
internal static class MoveGen<TBitboard> where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>
{
    /// <summary>Solid gate cells right now: closed gates of every pressure-plate rule.</summary>
    public static TBitboard ActiveDynamicWalls(PuzzleTopology<TBitboard> topology, in PolyominoState<TBitboard> state)
    {
        TBitboard walls = TBitboard.Zero;
        foreach (var rule in topology.PressurePlates)
        {
            TBitboard triggerOcc = state.GetRelevantOccupancy(rule.TriggerElevationMask, TBitboard.Zero);
            bool isPressed = (triggerOcc & rule.TriggerCellsMask) != TBitboard.Zero;
            bool gateClosed = rule.NormallyClosed ? !isPressed : isPressed;
            if (gateClosed) walls |= rule.GateCellsMask;
        }
        return walls;
    }

    /// <summary>True when the move leaves a gate closed on a piece that was not under a closed gate before.</summary>
    public static bool Crushes(PuzzleTopology<TBitboard> topology, in PolyominoState<TBitboard> before, in PolyominoState<TBitboard> after)
    {
        if (topology.PressurePlates.Count == 0) return false;
        TBitboard now = after.TotalOccupancy & ActiveDynamicWalls(topology, after);
        TBitboard was = before.TotalOccupancy & ActiveDynamicWalls(topology, before);
        return (now & ~was) != TBitboard.Zero;
    }

    public static TBitboard OtherOccupancy(PuzzleTopology<TBitboard> topology, in PolyominoState<TBitboard> state, EquivalenceGroup<TBitboard> g, int anchor,
        TBitboard walls) => state.GetRelevantOccupancy(g.ElevationMask, g.BaseMask << anchor) | walls;

    private static TBitboard[] ByDirRow(PuzzleTopology<TBitboard> topology, int groupId)
    {
        var row = new TBitboard[topology.DirectionCount];
        for (int d = 0; d < row.Length; d++) row[d] = topology.ValidAnchorsByDir[groupId, d];
        return row;
    }

    /// <summary>Anchors the piece may move to in one turn according to its group's <see cref="SlidePhysics"/>.</summary>
    public static TBitboard Reachable(PuzzleTopology<TBitboard> topology, in PolyominoState<TBitboard> state, int pieceId, TBitboard walls)
    {
        var g = topology.Groups[state.GroupOf(pieceId)];
        int a = state.AnchorOf(pieceId);
        TBitboard other = OtherOccupancy(topology, state, g, a, walls);
        if (g.SlidePhysics == SlidePhysics.ContinuousFloodFill)
        {
            ReadOnlySpan<TBitboard> byDir = topology.Diodes.Count > 0 ? ByDirRow(topology, g.GroupId) : default;
            return PolyominoState<TBitboard>.FindReachableAnchors(a, g.BaseMask, g.ValidAnchorsMask, other, topology.Width, g.Directions, topology.Geometry, byDir);
        }

        TBitboard result = TBitboard.Zero;
        for (int d = 0; d < topology.DirectionCount; d++)
        {
            if (!Grid.IsAllowed(g.Directions, d)) continue;
            int end = Glide(topology, g, d, a, other, g.SlidePhysics == SlidePhysics.SingleStep ? 1 : int.MaxValue, out _, out _, out _);
            if (end != a) result |= TBitboard.One << end;
        }
        return result;
    }

    /// <summary>Slides from <paramref name="start"/> along <paramref name="dir"/> until blocked (or <paramref name="maxSteps"/>); returns the final anchor.</summary>
    public static int Glide(PuzzleTopology<TBitboard> topology, EquivalenceGroup<TBitboard> g, int dir, int start, TBitboard other, int maxSteps,
        out int steps, out TBitboard swept, out TBitboard blocking)
    {
        int curr = start, cells = topology.CellCount, delta = Grid.Delta(topology.Width, dir);
        steps = 0;
        swept = g.BaseMask << start;
        blocking = TBitboard.Zero;
        while (steps < maxSteps)
        {
            if (!Grid.StepStaysOnBoard(curr, dir, topology.Width, cells)) break;
            int next = curr + delta;
            if (((topology.ValidAnchorsByDir[g.GroupId, dir] >> next) & TBitboard.One) == TBitboard.Zero) break;
            TBitboard footprint = g.BaseMask << next;
            TBitboard hit = other & footprint;
            if (hit != TBitboard.Zero) { blocking = hit; break; }
            curr = next;
            steps++;
            swept |= footprint;
        }
        return curr;
    }
}

public sealed class PolyominoDomain<TBitboard> : ISlidingBlockDomain<PolyominoState<TBitboard>, PolyominoMove>
    where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>
{
    /// <summary>The puzzle is solved when group <paramref name="goalGroupId"/> has exactly one piece, on <paramref name="goalAnchor"/>.</summary>
    public PolyominoDomain(PuzzleTopology<TBitboard> topology, int goalGroupId, int goalAnchor)
    {
        Topology = topology;
        GoalGroupId = goalGroupId;
        GoalAnchor = goalAnchor;
        InitialState = PolyominoState<TBitboard>.FromTopology(topology);
    }

    /// <summary>
    /// The puzzle is solved when the position equals <paramref name="goal"/>. Pieces of one group are interchangeable, so only which
    /// group sits where matters (see <see cref="PolyominoState{TBitboard}"/>). Build the goal with <see cref="PolyominoState{TBitboard}.FromTopology"/> from a
    /// topology that has the same groups, in the same order, as <paramref name="topology"/>. Bit g of <paramref name="matchedGroups"/>
    /// says whether group g has to match the goal; groups left out may end up anywhere.
    /// </summary>
    public PolyominoDomain(PuzzleTopology<TBitboard> topology, PolyominoState<TBitboard> goal, ulong matchedGroups = ulong.MaxValue)
    {
        Topology = topology;
        GoalGroupId = GoalAnchor = -1;
        _goal = goal;
        _matchedGroups = matchedGroups;
        _hasGoalPosition = true;
        InitialState = PolyominoState<TBitboard>.FromTopology(topology);
    }

    private readonly PolyominoState<TBitboard> _goal;
    private readonly ulong _matchedGroups;
    private readonly bool _hasGoalPosition;

    public PuzzleTopology<TBitboard> Topology { get; }

    /// <summary>Goal of the single-group constructor; -1 when the goal is a whole position (<see cref="GoalPosition"/>).</summary>
    public int GoalGroupId { get; }
    public int GoalAnchor { get; }

    /// <summary>The goal position when the domain was built with one, otherwise null.</summary>
    public PolyominoState<TBitboard>? GoalPosition => _hasGoalPosition ? _goal : null;

    public PolyominoState<TBitboard> InitialState { get; }
    public int MaxMoves => Topology.Pieces.Count * Topology.CellCount;

    public bool IsGoal(in PolyominoState<TBitboard> state)
    {
        if (!_hasGoalPosition) return state.GroupAnchorsOf(GoalGroupId) == TBitboard.One << GoalAnchor;
        if (_matchedGroups == ulong.MaxValue) return state.Equals(_goal);
        for (ulong m = _matchedGroups; m != 0; m &= m - 1)
        {
            int g = BitOperations.TrailingZeroCount(m);
            if (g >= GroupAnchorBuffer<TBitboard>.Length) break;
            if (state.GroupAnchorsOf(g) != _goal.GroupAnchorsOf(g)) return false;
        }
        return true;
    }

    /// <summary>The cells closed gates make solid in <paramref name="state"/>.</summary>
    public TBitboard ActiveDynamicWalls(in PolyominoState<TBitboard> state) => MoveGen<TBitboard>.ActiveDynamicWalls(Topology, state);

    public TBitboard ReachableFor(in PolyominoState<TBitboard> state, int pieceId) =>
        MoveGen<TBitboard>.Reachable(Topology, state, pieceId, MoveGen<TBitboard>.ActiveDynamicWalls(Topology, state));

    /// <summary>The cells a piece passes through on its shortest way to <paramref name="targetAnchor"/> (start and target included), empty if it cannot get there.</summary>
    public IReadOnlyList<int> PathFor(in PolyominoState<TBitboard> state, int pieceId, int targetAnchor)
    {
        var g = Topology.Groups[state.GroupOf(pieceId)];
        int a = state.AnchorOf(pieceId);
        TBitboard other = MoveGen<TBitboard>.OtherOccupancy(Topology, state, g, a, ActiveDynamicWalls(state));
        TBitboard[] byDir = [];
        if (Topology.Diodes.Count > 0)
        {
            byDir = new TBitboard[Topology.DirectionCount];
            for (int d = 0; d < byDir.Length; d++) byDir[d] = Topology.ValidAnchorsByDir[g.GroupId, d];
        }
        return PolyominoState<TBitboard>.ReconstructHookPath(a, targetAnchor, g.BaseMask, g.ValidAnchorsMask, other, Topology.Width, g.Directions,
            Topology.Geometry, byDir);
    }

    /// <summary>
    /// Records the glide of a piece in every direction its group allows (start, terminal anchor, steps, swept footprint cells and the bits that stopped it).
    /// Returns the number of traces written.
    /// </summary>
    public int TraceRaycast(in PolyominoState<TBitboard> state, int pieceId, Span<RaycastTrace<TBitboard>> results)
    {
        var g = Topology.Groups[state.GroupOf(pieceId)];
        int a = state.AnchorOf(pieceId), n = 0;
        TBitboard other = MoveGen<TBitboard>.OtherOccupancy(Topology, state, g, a, ActiveDynamicWalls(state));
        for (int d = 0; d < Topology.DirectionCount && n < results.Length; d++)
        {
            if (!Grid.IsAllowed(g.Directions, d)) continue;
            int end = MoveGen<TBitboard>.Glide(Topology, g, d, a, other, int.MaxValue, out int steps, out var swept, out var blocking);
            results[n++] = new RaycastTrace<TBitboard>(d, a, end, steps, swept, blocking);
        }
        return n;
    }

    public int GenerateMoves(in PolyominoState<TBitboard> state, Span<PolyominoMove> moveBuffer)
    {
        int n = 0;
        TBitboard walls = MoveGen<TBitboard>.ActiveDynamicWalls(Topology, state);
        foreach (var p in Topology.Pieces)
            for (TBitboard r = MoveGen<TBitboard>.Reachable(Topology, state, p.PieceId, walls); r != TBitboard.Zero; r &= r - TBitboard.One)
            {
                int target = Grid.Lsb(r);
                if (Topology.PressurePlates.Count > 0 && MoveGen<TBitboard>.Crushes(Topology, state, state.WithPieceMoved(Topology, p.PieceId, target))) continue;
                moveBuffer[n++] = new PolyominoMove((byte)p.PieceId, (byte)target);
            }
        return n;
    }

    public PolyominoState<TBitboard> ApplyMove(in PolyominoState<TBitboard> state, in PolyominoMove move) =>
        state.WithPieceMoved(Topology, move.PieceId, move.TargetAnchor);

    public ulong GetHashCode64(in PolyominoState<TBitboard> state) => state.ZobristHash;
}
