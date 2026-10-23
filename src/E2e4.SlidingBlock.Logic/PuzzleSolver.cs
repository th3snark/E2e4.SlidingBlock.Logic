namespace E2e4.SlidingBlock.Logic;

public sealed record SolveResult<TMove>(IReadOnlyList<TMove> Moves, long NodesExpanded);

/// <summary>Generic BFS / A* / IDA* solvers over any <see cref="ISlidingBlockDomain{TState,TMove}"/>.</summary>
public sealed class PuzzleSolver
{
    public long MaxNodes { get; init; } = 5_000_000;

    /// <summary>Breadth-first search; optimal in number of moves. Returns null if unsolvable or the node cap is hit.</summary>
    public SolveResult<TMove>? SolveBfs<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain, TState start, CancellationToken ct = default)
        where TState : struct, IEquatable<TState>
    {
        var states = new List<TState> { start };
        var parent = new List<int> { -1 };
        var moveUsed = new List<TMove> { default! };
        var seen = new Dictionary<TState, int> { [start] = 0 };
        var buffer = new TMove[domain.MaxMoves];
        if (domain.IsGoal(start)) return new SolveResult<TMove>([], 0);

        for (int head = 0; head < states.Count; head++)
        {
            if ((head & 1023) == 0) ct.ThrowIfCancellationRequested();
            if (head > MaxNodes) return null;
            TState cur = states[head];
            int n = domain.GenerateMoves(cur, buffer);
            for (int i = 0; i < n; i++)
            {
                TState next = domain.ApplyMove(cur, buffer[i]);
                if (seen.ContainsKey(next)) continue;
                seen[next] = states.Count;
                states.Add(next);
                parent.Add(head);
                moveUsed.Add(buffer[i]);
                if (domain.IsGoal(next)) return new SolveResult<TMove>(Trace(states.Count - 1, parent, moveUsed), head + 1);
            }
        }
        return null;
    }

    /// <summary>A* with an admissible heuristic.</summary>
    public SolveResult<TMove>? SolveAStar<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain, TState start, Func<TState, int> heuristic, CancellationToken ct = default)
        where TState : struct, IEquatable<TState>
    {
        var states = new List<TState> { start };
        var parent = new List<int> { -1 };
        var moveUsed = new List<TMove> { default! };
        var g = new List<int> { 0 };
        var best = new Dictionary<TState, int> { [start] = 0 };
        var open = new PriorityQueue<int, (int f, int h)>();
        int h0 = heuristic(start);
        open.Enqueue(0, (h0, h0));
        var buffer = new TMove[domain.MaxMoves];
        long expanded = 0;

        while (open.TryDequeue(out int idx, out _))
        {
            if ((++expanded & 1023) == 0) ct.ThrowIfCancellationRequested();
            if (expanded > MaxNodes) return null;
            TState cur = states[idx];
            if (best.TryGetValue(cur, out int bg) && bg < g[idx]) continue;
            if (domain.IsGoal(cur)) return new SolveResult<TMove>(Trace(idx, parent, moveUsed), expanded);
            int n = domain.GenerateMoves(cur, buffer);
            for (int i = 0; i < n; i++)
            {
                TState next = domain.ApplyMove(cur, buffer[i]);
                int ng = g[idx] + 1;
                if (best.TryGetValue(next, out int old) && old <= ng) continue;
                best[next] = ng;
                states.Add(next);
                parent.Add(idx);
                moveUsed.Add(buffer[i]);
                g.Add(ng);
                int h = heuristic(next);
                open.Enqueue(states.Count - 1, (ng + h, h));
            }
        }
        return null;
    }

    /// <summary>Iterative-deepening A*; memory-light, suited to large state spaces with a strong heuristic.</summary>
    public SolveResult<TMove>? SolveIdaStar<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain, TState start, Func<TState, int> heuristic, CancellationToken ct = default)
        where TState : struct, IEquatable<TState>
    {
        var path = new List<TMove>();
        var buffers = new List<TMove[]>();
        long nodes = 0;
        int bound = heuristic(start);

        // Returns -1 when solved, otherwise the smallest f that exceeded the bound (int.MaxValue = dead end).
        int Search(TState state, TState parentState, bool hasParent, int g, int limit)
        {
            if ((++nodes & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (nodes > MaxNodes) throw new OperationCanceledException("Node limit reached.");
            int f = g + heuristic(state);
            if (f > limit) return f;
            if (domain.IsGoal(state)) return -1;
            if (buffers.Count <= g) buffers.Add(new TMove[domain.MaxMoves]);
            var buf = buffers[g];
            int n = domain.GenerateMoves(state, buf);
            int min = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                TState next = domain.ApplyMove(state, buf[i]);
                if (hasParent && next.Equals(parentState)) continue;
                path.Add(buf[i]);
                int t = Search(next, state, true, g + 1, limit);
                if (t == -1) return -1;
                path.RemoveAt(path.Count - 1);
                if (t < min) min = t;
            }
            return min;
        }

        try
        {
            while (true)
            {
                path.Clear();
                int t = Search(start, start, false, 0, bound);
                if (t == -1) return new SolveResult<TMove>(path.ToArray(), nodes);
                if (t == int.MaxValue) return null;
                bound = t;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static TMove[] Trace<TMove>(int index, List<int> parent, List<TMove> moves)
    {
        var result = new List<TMove>();
        for (int i = index; parent[i] >= 0; i = parent[i]) result.Add(moves[i]);
        result.Reverse();
        return result.ToArray();
    }
}
