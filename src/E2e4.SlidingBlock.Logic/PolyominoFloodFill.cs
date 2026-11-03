namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// Flood fills over the anchors of one piece: every anchor it can reach by sliding a cell at a time, corner hooks included, and the path to
/// one of them. <see cref="PolyominoDomain{TBitboard}"/> makes its flood-fill moves with the same fill; these are for a view that shows or
/// animates them. Give the type argument when every mask is a <see cref="ulong"/> literal: <c>FindReachableAnchors&lt;Bitboard64&gt;(...)</c>.
/// </summary>
public static class PolyominoFloodFill
{
    private static void CheckStart<TBitboard>(int startAnchor, int boardWidth)
        where TBitboard : unmanaged, IBitboard<TBitboard>
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(boardWidth);
        if ((uint)startAnchor >= (uint)Math.Min(Grid.BitWidth<TBitboard>(), 128))
            throw new ArgumentOutOfRangeException(nameof(startAnchor), startAnchor, "The start anchor must be a cell of the board.");
    }

    private static TBitboard FreeAnchors<TBitboard>(int startAnchor, TBitboard baseMask, TBitboard validAnchorsMask, TBitboard otherOccupancy)
        where TBitboard : unmanaged, IBitboard<TBitboard>
    {
        // An anchor is blocked when any cell of the shape lands on an occupied cell: one shift of the occupancy per shape cell.
        TBitboard blocked = Bit<TBitboard>.Zero;
        foreach (int cell in Grid.Bits(baseMask)) blocked = blocked.Or(otherOccupancy.ShiftRight(cell));
        return validAnchorsMask.AndNot(blocked).Or(Bit<TBitboard>.One.ShiftLeft(startAnchor));
    }

    private static int Prepare<TBitboard>(int width, AllowedDirections allowed, GridGeometry geometry, Span<int> dirs, Span<int> deltas, Span<TBitboard> entry)
        where TBitboard : unmanaged, IBitboard<TBitboard>
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

    /// <summary>The flood fill of the public <c>FindReachableAnchors</c> over direction tables the caller already has (indexed by direction).</summary>
    internal static TBitboard Flood<TBitboard>(int startAnchor, TBitboard baseMask, TBitboard validAnchorsMask, TBitboard otherOccupancy, AllowedDirections directions,
        ReadOnlySpan<int> deltas, ReadOnlySpan<TBitboard> entry, ReadOnlySpan<TBitboard> validByDir)
        where TBitboard : unmanaged, IBitboard<TBitboard>
    {
        TBitboard free = FreeAnchors(startAnchor, baseMask, validAnchorsMask, otherOccupancy);
        TBitboard start = Bit<TBitboard>.One.ShiftLeft(startAnchor);
        TBitboard visitedOrInvalid = free.Not().Or(start);
        TBitboard frontier = start, reachable = Bit<TBitboard>.Zero;
        while (!frontier.IsEmpty)
        {
            TBitboard next = Bit<TBitboard>.Zero;
            for (int d = 0; d < deltas.Length; d++)
            {
                if (!Grid.IsAllowed(directions, d)) continue;
                TBitboard step = Grid.Shift(frontier, deltas[d]).And(entry[d]);
                if (!validByDir.IsEmpty) step = step.And(validByDir[d]);
                next = next.Or(step);
            }
            next = next.AndNot(visitedOrInvalid);
            visitedOrInvalid = visitedOrInvalid.Or(next);
            reachable = reachable.Or(next);
            frontier = next;
        }
        return reachable;
    }

    private static TBitboard StepMask<TBitboard>(TBitboard frontier, int slot, ReadOnlySpan<int> dirs, ReadOnlySpan<int> deltas, ReadOnlySpan<TBitboard> entry,
        ReadOnlySpan<TBitboard> validByDir)
        where TBitboard : unmanaged, IBitboard<TBitboard>
    {
        TBitboard step = Grid.Shift(frontier, deltas[slot]).And(entry[slot]);
        if (!validByDir.IsEmpty) step = step.And(validByDir[dirs[slot]]);
        return step;
    }

    /// <summary>
    /// Register-only BFS flood fill over anchors. Returns every anchor (excluding the start) reachable by
    /// sliding one square at a time, including multi-step corner "hook" paths. Entry masks stop shifts
    /// from wrapping across rows. <paramref name="validByDir"/> (one mask per direction) restricts which anchors a step may enter, e.g. for diodes and partitions.
    /// </summary>
    public static TBitboard FindReachableAnchors<TBitboard>(int startAnchor, TBitboard baseMask, TBitboard validAnchorsMask, TBitboard otherOccupancy, int boardWidth,
        AllowedDirections directions = AllowedDirections.Both, GridGeometry geometry = GridGeometry.Orthogonal4Way, ReadOnlySpan<TBitboard> validByDir = default)
        where TBitboard : unmanaged, IBitboard<TBitboard>
    {
        CheckStart<TBitboard>(startAnchor, boardWidth);
        int count = Grid.DirectionCount(geometry);
        Span<int> deltas = stackalloc int[Grid.MaxDirections];
        Span<TBitboard> entry = stackalloc TBitboard[Grid.MaxDirections];
        for (int d = 0; d < count; d++)
        {
            deltas[d] = Grid.Delta(boardWidth, d);
            entry[d] = Grid.EntryMask<TBitboard>(d, boardWidth);
        }
        return Flood(startAnchor, baseMask, validAnchorsMask, otherOccupancy, directions, deltas.Slice(0, count), entry.Slice(0, count), validByDir);
    }

    /// <summary>
    /// Flood fill that also records the shortest-path tree. <paramref name="parentMap"/> (length at least the cell count) is filled with the predecessor anchor of every reachable anchor (-1 elsewhere, the start points at itself).
    /// Destinations whose shortest path keeps one axis are returned in <paramref name="straightMovesMask"/>, those that turn a corner in
    /// <paramref name="hookMovesMask"/>. The result is the union of the two.
    /// </summary>
    public static TBitboard FindReachableAnchors<TBitboard>(int startAnchor, TBitboard baseMask, TBitboard validAnchorsMask, TBitboard otherOccupancy, int boardWidth,
        Span<sbyte> parentMap, out TBitboard straightMovesMask, out TBitboard hookMovesMask, AllowedDirections directions = AllowedDirections.Both,
        GridGeometry geometry = GridGeometry.Orthogonal4Way, ReadOnlySpan<TBitboard> validByDir = default)
        where TBitboard : unmanaged, IBitboard<TBitboard>
    {
        CheckStart<TBitboard>(startAnchor, boardWidth);
        if (parentMap.Length <= startAnchor) throw new ArgumentException("The parent map must hold every cell of the board.", nameof(parentMap));
        TBitboard free = FreeAnchors(startAnchor, baseMask, validAnchorsMask, otherOccupancy);
        Span<int> dirs = stackalloc int[Grid.MaxDirections], deltas = stackalloc int[Grid.MaxDirections];
        Span<TBitboard> entry = stackalloc TBitboard[Grid.MaxDirections];
        int n = Prepare(boardWidth, directions, geometry, dirs, deltas, entry);
        int bits = Grid.BitWidth<TBitboard>();

        parentMap.Fill(-1);
        parentMap[startAnchor] = (sbyte)startAnchor;
        TBitboard startBit = Bit<TBitboard>.One.ShiftLeft(startAnchor);
        TBitboard visitedOrInvalid = free.Not().Or(startBit);
        TBitboard frontier = startBit, reachable = Bit<TBitboard>.Zero;
        while (!frontier.IsEmpty)
        {
            TBitboard next = Bit<TBitboard>.Zero;
            for (int i = 0; i < n; i++) next = next.Or(StepMask(frontier, i, dirs, deltas, entry, validByDir));
            next = next.AndNot(visitedOrInvalid);
            visitedOrInvalid = visitedOrInvalid.Or(next);
            reachable = reachable.Or(next);
            for (TBitboard m = next; !m.IsEmpty; m = m.ClearLowest())
            {
                int a = Grid.Lsb(m);
                TBitboard abit = Bit<TBitboard>.One.ShiftLeft(a);
                int parent = int.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    int src = a - deltas[i];
                    if ((uint)src >= (uint)bits || !frontier.HasBit(src)) continue;
                    if (!StepMask(Bit<TBitboard>.One.ShiftLeft(src), i, dirs, deltas, entry, validByDir).Intersects(abit)) continue;
                    if (src < parent) parent = src;
                }
                parentMap[a] = (sbyte)parent;
            }
            frontier = next;
        }

        straightMovesMask = Bit<TBitboard>.Zero;
        hookMovesMask = Bit<TBitboard>.Zero;
        for (TBitboard r = reachable; !r.IsEmpty; r = r.ClearLowest())
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
            if (hook) hookMovesMask = hookMovesMask.Or(Bit<TBitboard>.One.ShiftLeft(dest)); else straightMovesMask = straightMovesMask.Or(Bit<TBitboard>.One.ShiftLeft(dest));
        }
        return reachable;
    }

    private static int AxisOfStep(int diff, int width, GridGeometry geometry)
    {
        for (int d = 0; d < Grid.DirectionCount(geometry); d++)
            if (Grid.Delta(width, d) == diff) return Grid.Axis(d);
        throw new InvalidOperationException($"A step of {diff} cells is not a move in any direction of the board.");
    }

    /// <summary>Shortest step-by-step anchor trajectory (start..target inclusive) or empty if unreachable.</summary>
    public static IReadOnlyList<int> ReconstructHookPath<TBitboard>(int startAnchor, int targetAnchor, TBitboard baseMask, TBitboard validAnchorsMask,
        TBitboard otherOccupancy, int boardWidth, AllowedDirections directions = AllowedDirections.Both,
        GridGeometry geometry = GridGeometry.Orthogonal4Way, ReadOnlySpan<TBitboard> validByDir = default)
        where TBitboard : unmanaged, IBitboard<TBitboard>
    {
        CheckStart<TBitboard>(startAnchor, boardWidth);
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
                if (parent[n] != -1 || !free.HasBit(n)) continue;
                if (!validByDir.IsEmpty && !validByDir[d].HasBit(n)) continue;
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
