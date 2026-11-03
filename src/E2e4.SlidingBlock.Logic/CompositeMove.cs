using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace E2e4.SlidingBlock.Logic;

/// <summary>One piece changing group and/or anchor as part of a <see cref="CompositeMove"/>.</summary>
public readonly record struct PieceDelta(byte PieceId, byte OldGroupId, byte NewGroupId, byte OldAnchor, byte NewAnchor);

/// <summary>Several pieces changing at once: a slide, a rotation, a push, a carry or a magnetic cluster.</summary>
public struct CompositeMove : IEquatable<CompositeMove>
{
    /// <summary>The most pieces one move can change.</summary>
    public const int MaxPieces = 4;

    /// <summary>Number of changes in this move (1..4); read them through <see cref="AsSpan"/>.</summary>
    public byte Count { get; internal set; }

    /// <summary>-1 for a rotation, 0..5 for a one-step slide in that direction, -2 for a multi-step slide or glide.</summary>
    public sbyte DirectionIndex { get; internal set; }

    /// <summary>The changes; only the first <see cref="Count"/> are meaningful.</summary>
    internal MoveDeltaBuffer Deltas;

    /// <summary>The changes of this move.</summary>
    [UnscopedRef]
    public ReadOnlySpan<PieceDelta> AsSpan() => Deltas.Slice(Count);

    /// <inheritdoc/>
    public readonly bool Equals(CompositeMove other)
    {
        if (Count != other.Count || DirectionIndex != other.DirectionIndex) return false;
        for (int i = 0; i < Count; i++)
            if (Deltas[i] != other.Deltas[i]) return false;
        return true;
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is CompositeMove m && Equals(m);

    /// <inheritdoc/>
    public override readonly int GetHashCode()
    {
        int h = Count * 31 + DirectionIndex;
        for (int i = 0; i < Count; i++) h = h * 31 + Deltas[i].GetHashCode();
        return h;
    }

    /// <summary>True if both moves change the same pieces in the same way.</summary>
    public static bool operator ==(CompositeMove left, CompositeMove right) => left.Equals(right);
    /// <summary>True if the moves differ.</summary>
    public static bool operator !=(CompositeMove left, CompositeMove right) => !left.Equals(right);

    internal static CompositeMove Single(int pieceId, int group, int oldAnchor, int newAnchor, int direction) =>
        Rotated(pieceId, group, group, oldAnchor, newAnchor, direction);

    internal static CompositeMove Rotated(int pieceId, int oldGroup, int newGroup, int oldAnchor, int newAnchor, int direction)
    {
        var m = new CompositeMove { Count = 1, DirectionIndex = (sbyte)direction };
        m.Deltas[0] = new PieceDelta((byte)pieceId, (byte)oldGroup, (byte)newGroup, (byte)oldAnchor, (byte)newAnchor);
        return m;
    }
}

/// <summary>
/// Polyomino rules with coupled kinematics: slides and glides, rotation through orientation rings, pushing, hollow carriers and
/// magnetic clusters. Built on a <see cref="PolyominoDomain{TBitboard}"/> that supplies the goal and initial position.
/// </summary>
public sealed class CompositeDomain<TBitboard> : ISlidingBlockDomain<PolyominoState<TBitboard>, CompositeMove>
    where TBitboard : unmanaged, IBitboard<TBitboard>
{
    private const RoleFlags ClusterRoles = RoleFlags.Pusher | RoleFlags.HollowCarrier | RoleFlags.Magnetic;

    /// <summary>Plays on the topology of <paramref name="goalDomain"/> and is solved when its goal is reached.</summary>
    /// <param name="goalDomain">Supplies the topology and the goal; the initial position is the topology's.</param>
    /// <param name="maxPushDepth">How many pieces deep a push chains: 1 moves only what the pusher touches directly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="goalDomain"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxPushDepth"/> is below 1.</exception>
    public CompositeDomain(PolyominoDomain<TBitboard> goalDomain, int maxPushDepth = 1)
        : this((goalDomain ?? throw new ArgumentNullException(nameof(goalDomain))).Topology, goalDomain.Goal, maxPushDepth)
    {
    }

    /// <summary>
    /// Plays on <paramref name="topology"/> and is solved when <paramref name="goal"/> is reached.
    /// </summary>
    /// <param name="topology">The board and pieces.</param>
    /// <param name="goal">When the puzzle is solved.</param>
    /// <param name="maxPushDepth">How many pieces deep a push chains: 1 moves only what the pusher touches directly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="topology"/> or <paramref name="goal"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxPushDepth"/> is below 1.</exception>
    public CompositeDomain(PuzzleTopology<TBitboard> topology, PolyominoGoal<TBitboard> goal, int maxPushDepth = 1)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPushDepth, 1);
        Topology = topology;
        Goal = goal;
        MaxPushDepth = maxPushDepth;
        InitialState = PolyominoState<TBitboard>.FromTopology(topology);
    }

    /// <summary>The board and pieces.</summary>
    public PuzzleTopology<TBitboard> Topology { get; }
    /// <summary>When the puzzle is solved.</summary>
    public PolyominoGoal<TBitboard> Goal { get; }
    /// <summary>How many pieces deep a push chains.</summary>
    public int MaxPushDepth { get; }
    /// <inheritdoc/>
    public PolyominoState<TBitboard> InitialState { get; }
    /// <inheritdoc/>
    public int MaxMoves => Topology.Pieces.Count * (Topology.CellCount + 8);

    /// <inheritdoc/>
    public bool IsGoal(in PolyominoState<TBitboard> state) => Goal.IsReached(state);
    /// <inheritdoc/>
    public ulong GetHashCode64(in PolyominoState<TBitboard> state) => state.Hash;

    /// <inheritdoc/>
    public PolyominoState<TBitboard> ApplyMove(in PolyominoState<TBitboard> state, in CompositeMove move)
    {
        var m = move;
        return state.WithDeltas(Topology, m.AsSpan());
    }

    /// <inheritdoc/>
    public int GenerateMoves(in PolyominoState<TBitboard> state, Span<CompositeMove> moveBuffer)
    {
        var topo = Topology;
        int n = 0;
        TBitboard walls = MoveGen<TBitboard>.ActiveDynamicWalls(topo, state);
        foreach (var p in topo.PieceTable)
        {
            int id = p.PieceId, a = state.AnchorOf(id);
            var g = topo.GroupTable[state.GroupOf(id)];

            if (g.CanRotate)
            {
                if (TryRotate(state, id, g, true, walls, out var cw)) Add(state, walls, cw, moveBuffer, ref n);
                if (TryRotate(state, id, g, false, walls, out var ccw) && !(g.NextCwGroupId == g.NextCcwGroupId && g.AnchorDeltaCw == g.AnchorDeltaCcw))
                    Add(state, walls, ccw, moveBuffer, ref n);
            }

            if (g.SlidePhysics != SlidePhysics.FrictionlessRaycast && ((g.Roles & ClusterRoles) != 0 || g.SlidePhysics == SlidePhysics.SingleStep))
            {
                for (int d = 0; d < topo.DirectionCount; d++)
                    if (TryCluster(state, id, d, walls, out var move)) Add(state, walls, move, moveBuffer, ref n);
            }
            else
            {
                var deltas = topo.GetDirectionDeltas();
                for (TBitboard r = MoveGen<TBitboard>.Reachable(topo, state, id, walls); !r.IsEmpty; r = r.ClearLowest())
                {
                    int target = Grid.Lsb(r), dir = -2;
                    for (int d = 0; d < deltas.Length; d++)
                        if (target - a == deltas[d] && Grid.StepStaysOnBoard(a, d, topo.Columns, topo.CellCount)) dir = d;
                    Add(state, walls, CompositeMove.Single(id, g.GroupId, a, target, dir), moveBuffer, ref n);
                }
            }
        }
        return n;
    }

    private void Add(in PolyominoState<TBitboard> state, TBitboard walls, CompositeMove move, Span<CompositeMove> buffer, ref int n)
    {
        if (Topology.PlateTable.Length > 0 && MoveGen<TBitboard>.Crushes(Topology, walls, state, state.WithDeltas(Topology, move.AsSpan()))) return;
        buffer[n++] = move;
    }

    private bool TryRotate(in PolyominoState<TBitboard> state, int pieceId, EquivalenceGroup<TBitboard> g, bool clockwise, TBitboard walls, out CompositeMove move)
    {
        move = default;
        var topo = Topology;
        int next = clockwise ? g.NextCwGroupId : g.NextCcwGroupId;
        if (next < 0) return false;
        int a = state.AnchorOf(pieceId);
        int nextAnchor = a + (clockwise ? g.AnchorDeltaCw : g.AnchorDeltaCcw);
        if ((uint)nextAnchor >= (uint)topo.CellCount || !topo.GroupTable[next].ValidAnchorsMask.HasBit(nextAnchor)) return false;

        int boxOrigin = a + g.BoxOriginDelta, size = g.RotationBoxSize;
        if (boxOrigin < 0 || boxOrigin % topo.Columns + size > topo.Columns || boxOrigin / topo.Columns + size > topo.Rows) return false;
        TBitboard blocked = MoveGen<TBitboard>.OtherOccupancy(topo, state, g, a, walls).Or(topo.StaticWallsMask);
        if (blocked.Intersects(g.SweptClearanceMask.ShiftLeft(boxOrigin))) return false;
        if (topo.PartitionTable.Length > 0 && topo.HasPartitionWithin(boxOrigin, size)) return false;

        move = CompositeMove.Rotated(pieceId, g.GroupId, next, a, nextAnchor, -1);
        return true;
    }

    private bool TryCluster(in PolyominoState<TBitboard> state, int pieceId, int dir, TBitboard walls, out CompositeMove move)
    {
        move = default;
        var topo = Topology;
        int delta = Grid.Delta(topo.Columns, dir);
        Span<int> members = stackalloc int[CompositeMove.MaxPieces], depth = stackalloc int[CompositeMove.MaxPieces];
        int count = 1;
        members[0] = pieceId;
        if (!Grid.IsAllowed(topo.GroupTable[state.GroupOf(pieceId)].Directions, dir)) return false;

        if ((topo.GroupTable[state.GroupOf(pieceId)].Roles & RoleFlags.Magnetic) != 0 && !GatherMagnetic(state, members, depth, ref count)) return false;

        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < count; i++)
            {
                int m = members[i], am = state.AnchorOf(m);
                var gm = topo.GroupTable[state.GroupOf(m)];
                if (!Grid.IsAllowed(gm.Directions, dir) || !Grid.StepStaysOnBoard(am, dir, topo.Columns, topo.CellCount)) return false;
                int nxt = am + delta;
                if (!Grid.Has(topo.ValidAnchorsFor(gm.GroupId, dir), nxt)) return false;
                TBitboard foot = gm.BaseMask.ShiftLeft(nxt);
                if (walls.Intersects(foot)) return false;

                foreach (var q in topo.PieceTable)
                {
                    if (Contains(members, count, q.PieceId)) continue;
                    var gq = topo.GroupTable[state.GroupOf(q.PieceId)];
                    if ((gq.ElevationMask & gm.ElevationMask) == 0) continue;
                    if (!gq.BaseMask.ShiftLeft(state.AnchorOf(q.PieceId)).Intersects(foot)) continue;

                    bool canPush = (gm.Roles & (RoleFlags.Pusher | RoleFlags.HollowCarrier)) != 0 || depth[i] > 0;
                    if (!canPush || (gq.Roles & RoleFlags.Pushable) == 0 || depth[i] + 1 > MaxPushDepth || count == CompositeMove.MaxPieces) return false;
                    members[count] = q.PieceId;
                    depth[count] = depth[i] + 1;
                    count++;
                    changed = true;
                }
            }
        } while (changed);

        move = new CompositeMove { Count = (byte)count, DirectionIndex = (sbyte)dir };
        for (int i = 0; i < count; i++)
        {
            int m = members[i], am = state.AnchorOf(m), gid = state.GroupOf(m);
            move.Deltas[i] = new PieceDelta((byte)m, (byte)gid, (byte)gid, (byte)am, (byte)(am + delta));
        }
        return true;
    }

    private static bool Contains(Span<int> members, int count, int value)
    {
        for (int i = 0; i < count; i++)
            if (members[i] == value) return true;
        return false;
    }

    /// <summary>Adds every magnetic piece orthogonally touching the cluster; false when the cluster outgrows a move.</summary>
    private bool GatherMagnetic(in PolyominoState<TBitboard> state, Span<int> members, Span<int> depth, ref int count)
    {
        var topo = Topology;
        int w = topo.Columns;
        TBitboard notCol0 = Grid.EntryMask<TBitboard>(1, w), notLast = Grid.EntryMask<TBitboard>(0, w);
        bool changed;
        do
        {
            changed = false;
            foreach (var q in topo.PieceTable)
            {
                if (Contains(members, count, q.PieceId)) continue;
                var gq = topo.GroupTable[state.GroupOf(q.PieceId)];
                if ((gq.Roles & RoleFlags.Magnetic) == 0) continue;
                TBitboard qFoot = gq.BaseMask.ShiftLeft(state.AnchorOf(q.PieceId));
                for (int i = 0; i < count; i++)
                {
                    var gm = topo.GroupTable[state.GroupOf(members[i])];
                    if ((gm.ElevationMask & gq.ElevationMask) == 0) continue;
                    TBitboard f = gm.BaseMask.ShiftLeft(state.AnchorOf(members[i]));
                    TBitboard touch = f.ShiftLeft(1).And(notCol0).Or(f.ShiftRight(1).And(notLast)).Or(f.ShiftLeft(w)).Or(f.ShiftRight(w));
                    if (!touch.Intersects(qFoot)) continue;
                    if (count == CompositeMove.MaxPieces) return false;
                    members[count] = q.PieceId;
                    depth[count] = 0;
                    count++;
                    changed = true;
                    break;
                }
            }
        } while (changed);
        return true;
    }
}
