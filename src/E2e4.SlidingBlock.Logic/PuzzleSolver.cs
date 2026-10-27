using System.Runtime.InteropServices;

namespace E2e4.SlidingBlock.Logic;

/// <summary>A solution: the moves from the start position to a goal and the number of nodes the search expanded.</summary>
/// <param name="Moves">The moves in order; empty when the start is already a goal.</param>
/// <param name="NodesExpanded">How many nodes the search expanded to find them.</param>
public sealed record SolveResult<TMove>(IReadOnlyList<TMove> Moves, long NodesExpanded)
{
    /// <summary>The total cost of <see cref="Moves"/> for the weighted solvers; 0 for the others, which count moves.</summary>
    public long Cost { get; init; }
}

/// <summary>Why a solver returned no solution. Cancellation is not a status: it throws <see cref="OperationCanceledException"/>.</summary>
public enum SolveStatus
{
    /// <summary>A solution was found.</summary>
    Solved,

    /// <summary>The search ran out of positions: no goal can be reached from the start.</summary>
    Unsolvable,

    /// <summary>The search stopped at <see cref="PuzzleSolver.MaxNodes"/> before it found a goal or ran out of positions.</summary>
    NodeLimit,
}

/// <summary>
/// Generic BFS / A* / IDA* solvers over any <see cref="ISlidingBlockDomain{TState,TMove}"/>. The solver holds no state between calls, so one instance
/// can serve several threads as long as the domain does. Each method returns null when there is no solution; the overloads with an
/// <c>out</c> <see cref="SolveStatus"/> say whether the puzzle is unsolvable or the node limit was hit. Cancelling through the token throws.
/// </summary>
public sealed class PuzzleSolver
{
    private long _maxNodes = 5_000_000;

    /// <summary>The most nodes a search may expand before it gives up with <see cref="SolveStatus.NodeLimit"/>. Must be positive.</summary>
    public long MaxNodes
    {
        get => _maxNodes;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxNodes = value;
        }
    }

    /// <summary>Breadth-first search; optimal in number of moves. Returns null if unsolvable or the node cap is hit.</summary>
    public SolveResult<TMove>? SolveBfs<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain, TState start, CancellationToken ct = default)
        where TState : struct, IEquatable<TState> =>
        SolveBfs(domain, start, out _, ct);

    /// <summary>Breadth-first search as <see cref="SolveBfs{TState,TMove}(ISlidingBlockDomain{TState,TMove},TState,CancellationToken)"/>, reporting why it found nothing.</summary>
    public SolveResult<TMove>? SolveBfs<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain, TState start, out SolveStatus status, CancellationToken ct = default)
        where TState : struct, IEquatable<TState>
    {
        ArgumentNullException.ThrowIfNull(domain);
        status = SolveStatus.Solved;
        var graph = new SearchGraph<TState, TMove>(start, domain.GetHashCode64(start));
        var buffer = new TMove[domain.MaxMoves];
        if (domain.IsGoal(start)) return new SolveResult<TMove>([], 0);

        for (int head = 0; head < graph.Count; head++)
        {
            if ((head & 1023) == 0) ct.ThrowIfCancellationRequested();
            if (head >= MaxNodes) { status = SolveStatus.NodeLimit; return null; }
            TState cur = graph.StateAt(head);
            int n = domain.GenerateMoves(cur, buffer);
            for (int i = 0; i < n; i++)
            {
                TState next = domain.ApplyMove(cur, buffer[i]);
                ulong hash = domain.GetHashCode64(next);
                if (graph.Find(next, hash, out _)) continue;
                int added = graph.Add(next, hash, head, buffer[i]);
                if (domain.IsGoal(next)) return new SolveResult<TMove>(graph.Trace(added), head + 1);
            }
        }
        status = SolveStatus.Unsolvable;
        return null;
    }

    /// <summary>A* with an admissible heuristic.</summary>
    public SolveResult<TMove>? SolveAStar<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain, TState start, Func<TState, int> heuristic, CancellationToken ct = default)
        where TState : struct, IEquatable<TState> =>
        SolveAStar(domain, start, heuristic, out _, ct);

    /// <summary>A* as <see cref="SolveAStar{TState,TMove}(ISlidingBlockDomain{TState,TMove},TState,Func{TState,int},CancellationToken)"/>, reporting why it found nothing.</summary>
    public SolveResult<TMove>? SolveAStar<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain, TState start, Func<TState, int> heuristic, out SolveStatus status, CancellationToken ct = default)
        where TState : struct, IEquatable<TState>
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(heuristic);
        status = SolveStatus.Solved;
        var graph = new SearchGraph<TState, TMove>(start, domain.GetHashCode64(start));
        var g = new List<int> { 0 };
        // Ordered by f, then by h, packed so one integer comparison does both.
        var open = new PriorityQueue<int, long>();
        int h0 = heuristic(start);
        open.Enqueue(0, Priority(h0, h0));
        var buffer = new TMove[domain.MaxMoves];
        long expanded = 0;

        while (open.TryDequeue(out int idx, out _))
        {
            if ((++expanded & 1023) == 0) ct.ThrowIfCancellationRequested();
            if (expanded > MaxNodes) { status = SolveStatus.NodeLimit; return null; }
            TState cur = graph.StateAt(idx);
            if (graph.Find(cur, domain.GetHashCode64(cur), out int best) && g[best] < g[idx]) continue;
            if (domain.IsGoal(cur)) return new SolveResult<TMove>(graph.Trace(idx), expanded);
            int n = domain.GenerateMoves(cur, buffer);
            for (int i = 0; i < n; i++)
            {
                TState next = domain.ApplyMove(cur, buffer[i]);
                ulong hash = domain.GetHashCode64(next);
                int ng = g[idx] + 1;
                if (graph.Find(next, hash, out int old) && g[old] <= ng) continue;
                int added = graph.Add(next, hash, idx, buffer[i]);
                g.Add(ng);
                int h = heuristic(next);
                open.Enqueue(added, Priority(ng + h, h));
            }
        }
        status = SolveStatus.Unsolvable;
        return null;
    }

    private static long Priority(int f, int h) => ((long)f << 32) | (uint)h;

    /// <summary>Dijkstra: the cheapest solution under <see cref="IWeightedDomain{TState,TMove}.MoveCost"/>. Returns null if unsolvable or the node cap is hit.</summary>
    public SolveResult<TMove>? SolveDijkstra<TState, TMove>(IWeightedDomain<TState, TMove> domain, TState start, CancellationToken ct = default)
        where TState : struct, IEquatable<TState> =>
        SolveWeightedAStar(domain, start, static _ => 0, out _, ct);

    /// <summary>Dijkstra as <see cref="SolveDijkstra{TState,TMove}(IWeightedDomain{TState,TMove},TState,CancellationToken)"/>, reporting why it found nothing.</summary>
    public SolveResult<TMove>? SolveDijkstra<TState, TMove>(IWeightedDomain<TState, TMove> domain, TState start, out SolveStatus status, CancellationToken ct = default)
        where TState : struct, IEquatable<TState> =>
        SolveWeightedAStar(domain, start, static _ => 0, out status, ct);

    /// <summary>A* over move costs; the heuristic must never overestimate the remaining cost. Returns null if unsolvable or the node cap is hit.</summary>
    public SolveResult<TMove>? SolveWeightedAStar<TState, TMove>(IWeightedDomain<TState, TMove> domain, TState start, Func<TState, long> heuristic, CancellationToken ct = default)
        where TState : struct, IEquatable<TState> =>
        SolveWeightedAStar(domain, start, heuristic, out _, ct);

    /// <summary>Weighted A* as <see cref="SolveWeightedAStar{TState,TMove}(IWeightedDomain{TState,TMove},TState,Func{TState,long},CancellationToken)"/>, reporting why it found nothing.</summary>
    /// <exception cref="InvalidOperationException">The domain returned a negative move cost.</exception>
    public SolveResult<TMove>? SolveWeightedAStar<TState, TMove>(IWeightedDomain<TState, TMove> domain, TState start, Func<TState, long> heuristic, out SolveStatus status, CancellationToken ct = default)
        where TState : struct, IEquatable<TState>
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(heuristic);
        status = SolveStatus.Solved;
        var graph = new SearchGraph<TState, TMove>(start, domain.GetHashCode64(start));
        var g = new List<long> { 0 };
        // Ordered by f, then by h, so ties go to the node nearer the goal.
        var open = new PriorityQueue<int, (long F, long H)>();
        long h0 = heuristic(start);
        open.Enqueue(0, (h0, h0));
        var buffer = new TMove[domain.MaxMoves];
        long expanded = 0;

        while (open.TryDequeue(out int idx, out _))
        {
            if ((++expanded & 1023) == 0) ct.ThrowIfCancellationRequested();
            if (expanded > MaxNodes) { status = SolveStatus.NodeLimit; return null; }
            TState cur = graph.StateAt(idx);
            if (graph.Find(cur, domain.GetHashCode64(cur), out int best) && g[best] < g[idx]) continue;
            if (domain.IsGoal(cur)) return new SolveResult<TMove>(graph.Trace(idx), expanded) { Cost = g[idx] };
            int n = domain.GenerateMoves(cur, buffer);
            for (int i = 0; i < n; i++)
            {
                long cost = domain.MoveCost(cur, buffer[i]);
                if (cost < 0) throw new InvalidOperationException("A move cost must not be negative.");
                TState next = domain.ApplyMove(cur, buffer[i]);
                ulong hash = domain.GetHashCode64(next);
                long ng = g[idx] + cost;
                if (graph.Find(next, hash, out int old) && g[old] <= ng) continue;
                int added = graph.Add(next, hash, idx, buffer[i]);
                g.Add(ng);
                long h = heuristic(next);
                open.Enqueue(added, (ng + h, h));
            }
        }
        status = SolveStatus.Unsolvable;
        return null;
    }

    /// <summary>Iterative-deepening A*; memory-light, suited to large state spaces with a strong heuristic.</summary>
    public SolveResult<TMove>? SolveIdaStar<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain, TState start, Func<TState, int> heuristic, CancellationToken ct = default)
        where TState : struct, IEquatable<TState> =>
        SolveIdaStar(domain, start, heuristic, out _, ct);

    /// <summary>IDA* as <see cref="SolveIdaStar{TState,TMove}(ISlidingBlockDomain{TState,TMove},TState,Func{TState,int},CancellationToken)"/>, reporting why it found nothing.</summary>
    public SolveResult<TMove>? SolveIdaStar<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain, TState start, Func<TState, int> heuristic, out SolveStatus status, CancellationToken ct = default)
        where TState : struct, IEquatable<TState>
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(heuristic);
        status = SolveStatus.Solved;
        var search = new IdaSearch<TState, TMove>(domain, heuristic, MaxNodes, ct);
        int bound = heuristic(start);
        while (true)
        {
            search.Path.Clear();
            int t = search.Search(start, start, false, 0, bound);
            if (t == IdaSearch<TState, TMove>.Solved) return new SolveResult<TMove>(search.Path.ToArray(), search.Nodes);
            if (t == IdaSearch<TState, TMove>.Aborted) { status = SolveStatus.NodeLimit; return null; }
            if (t == int.MaxValue) { status = SolveStatus.Unsolvable; return null; }
            bound = t;
        }
    }

    /// <summary>One IDA* run: the moves of the current path, a move buffer per depth and the node count.</summary>
    private sealed class IdaSearch<TState, TMove> where TState : struct, IEquatable<TState>
    {
        public const int Solved = -1, Aborted = -2;

        private readonly ISlidingBlockDomain<TState, TMove> _domain;
        private readonly Func<TState, int> _heuristic;
        private readonly long _maxNodes;
        private readonly CancellationToken _ct;
        private readonly List<TMove[]> _buffers = [];

        public IdaSearch(ISlidingBlockDomain<TState, TMove> domain, Func<TState, int> heuristic, long maxNodes, CancellationToken ct)
        {
            _domain = domain;
            _heuristic = heuristic;
            _maxNodes = maxNodes;
            _ct = ct;
        }

        public List<TMove> Path { get; } = [];
        public long Nodes { get; private set; }

        /// <returns><see cref="Solved"/>, <see cref="Aborted"/> at the node cap, otherwise the smallest f that exceeded the bound (int.MaxValue = dead end).</returns>
        public int Search(TState state, TState parentState, bool hasParent, int g, int limit)
        {
            if ((++Nodes & 4095) == 0) _ct.ThrowIfCancellationRequested();
            if (Nodes > _maxNodes) return Aborted;
            int f = g + _heuristic(state);
            if (f > limit) return f;
            if (_domain.IsGoal(state)) return Solved;
            if (_buffers.Count <= g) _buffers.Add(new TMove[_domain.MaxMoves]);
            var buf = _buffers[g];
            int n = _domain.GenerateMoves(state, buf);
            int min = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                TState next = _domain.ApplyMove(state, buf[i]);
                if (hasParent && next.Equals(parentState)) continue;
                Path.Add(buf[i]);
                int t = Search(next, state, true, g + 1, limit);
                if (t is Solved or Aborted) return t;
                Path.RemoveAt(Path.Count - 1);
                if (t < min) min = t;
            }
            return min;
        }
    }

    /// <summary>
    /// The positions a search has met, each stored once, with the move that first reached it and a hash table from position to its latest node.
    /// </summary>
    private sealed class SearchGraph<TState, TMove> where TState : struct, IEquatable<TState>
    {
        private readonly List<TState> _states = [];
        private readonly List<int> _parent = [];
        private readonly List<TMove> _move = [];
        private int[] _slots = new int[1024];   // node index + 1; 0 = empty
        private ulong[] _hashes = new ulong[1024];
        private int _filled;

        public SearchGraph(TState start, ulong hash)
        {
            Add(start, hash, -1, default!);
        }

        public int Count => _states.Count;

        public TState StateAt(int node) => _states[node];

        /// <summary>Looks the position up; <paramref name="node"/> is its latest node when found.</summary>
        public bool Find(in TState state, ulong hash, out int node)
        {
            int mask = _slots.Length - 1;
            var states = CollectionsMarshal.AsSpan(_states);
            for (int i = (int)(hash >> 32) & mask; ; i = (i + 1) & mask)
            {
                int v = _slots[i];
                if (v == 0) { node = -1; return false; }
                if (_hashes[i] == hash && states[v - 1].Equals(state)) { node = v - 1; return true; }
            }
        }

        /// <summary>Adds a node for the position and makes it the latest one for its table entry.</summary>
        public int Add(in TState state, ulong hash, int parent, TMove move)
        {
            int node = _states.Count;
            _states.Add(state);
            _parent.Add(parent);
            _move.Add(move);
            if ((_filled + 1) * 2 > _slots.Length) Grow();
            int mask = _slots.Length - 1;
            var states = CollectionsMarshal.AsSpan(_states);
            int i = (int)(hash >> 32) & mask;
            for (; _slots[i] != 0; i = (i + 1) & mask)
            {
                if (_hashes[i] == hash && states[_slots[i] - 1].Equals(state))
                {
                    _slots[i] = node + 1;   // a better route to a known position replaces its node
                    return node;
                }
            }
            _slots[i] = node + 1;
            _hashes[i] = hash;
            _filled++;
            return node;
        }

        private void Grow()
        {
            var slots = new int[_slots.Length * 2];
            var hashes = new ulong[slots.Length];
            int mask = slots.Length - 1;
            for (int s = 0; s < _slots.Length; s++)
            {
                if (_slots[s] == 0) continue;
                int i = (int)(_hashes[s] >> 32) & mask;
                while (slots[i] != 0) i = (i + 1) & mask;
                slots[i] = _slots[s];
                hashes[i] = _hashes[s];
            }
            _slots = slots;
            _hashes = hashes;
        }

        public TMove[] Trace(int node)
        {
            int length = 0;
            for (int i = node; _parent[i] >= 0; i = _parent[i]) length++;
            var result = new TMove[length];
            for (int i = node; _parent[i] >= 0; i = _parent[i]) result[--length] = _move[i];
            return result;
        }
    }
}
