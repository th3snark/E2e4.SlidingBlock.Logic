using System.Buffers;

namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// A position: where every case stands (by case id, so the view can keep each case's number) and where the storeman is. Two positions are the
/// same when the cases occupy the same cells and the storeman can walk to the same part of the floor, whichever case is which and wherever he
/// stands in it: his walking is free, so only the region he can reach matters. <see cref="Player"/> is where he actually is.
/// </summary>
public readonly struct PushState : IEquatable<PushState>
{
    private readonly ushort[] _cases;

    internal PushState(ushort[] cases, ushort player, ushort region, ulong caseHash, ulong hash)
    {
        _cases = cases;
        Player = player;
        Region = region;
        CaseHash = caseHash;
        Hash = hash;
    }

    /// <summary>How many cases the position holds; 0 for <c>default</c>.</summary>
    public int CaseCount => _cases?.Length ?? 0;

    /// <summary>The cell of a case.</summary>
    /// <exception cref="InvalidOperationException">This is <c>default</c>, not a position of a <see cref="PushDomain"/>.</exception>
    public int CellOf(int id) => (_cases ?? throw new InvalidOperationException("This is not a position of a PushDomain."))[id];

    /// <summary>The cell the storeman stands on.</summary>
    public ushort Player { get; }

    /// <summary>The lowest cell of the floor the storeman can walk to; it names the region.</summary>
    internal ushort Region { get; }

    internal ulong CaseHash { get; }

    /// <summary>A 64-bit hash of the cases' cells and the storeman's region.</summary>
    public ulong Hash { get; }

    /// <inheritdoc/>
    public bool Equals(PushState other)
    {
        if (_cases is null || other._cases is null) return _cases is null && other._cases is null;
        if (Hash != other.Hash || Region != other.Region || _cases.Length != other._cases.Length) return false;
        Span<ushort> a = stackalloc ushort[_cases.Length], b = stackalloc ushort[_cases.Length];
        _cases.CopyTo(a);
        other._cases.CopyTo(b);
        a.Sort();
        b.Sort();
        return a.SequenceEqual(b);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PushState s && Equals(s);

    /// <inheritdoc/>
    public override int GetHashCode() => (int)(Hash ^ (Hash >> 32));

    /// <summary>True if both positions have the same cases on the same cells and the storeman can walk to the same region.</summary>
    public static bool operator ==(PushState left, PushState right) => left.Equals(right);

    /// <summary>True if the positions differ.</summary>
    public static bool operator !=(PushState left, PushState right) => !left.Equals(right);
}

/// <summary>
/// Push case <paramref name="Case"/> to cell <paramref name="To"/>, in as many pushes as it takes: the storeman walks round to wherever he needs to
/// stand, and ends on <paramref name="PlayerAt"/>, the cell the case was last pushed from.
/// </summary>
/// <param name="Case">The case that is pushed.</param>
/// <param name="To">The cell it ends on.</param>
/// <param name="PlayerAt">The cell the storeman ends on.</param>
public readonly record struct PushMove(byte Case, ushort To, ushort PlayerAt);

/// <summary>
/// The rules of a Soko puzzle: the storeman walks anywhere that is free, pushes one case at a time into a free cell and never pulls. One move is
/// all the pushes of one case in a row, so the storeman's walking costs nothing. The polyomino domain holds at most 16 pieces on 128 cells,
/// which is too small for a warehouse, so this domain carries its own position (a heap array: unlike the bitboard domains, applying a move
/// allocates) and plugs into <see cref="PlaySession{TState,TMove}"/> and the solvers all the same. The puzzle is solved when every case stands on a place.
/// </summary>
public sealed class PushDomain : ISlidingBlockDomain<PushState, PushMove>
{
    private readonly bool[] _floor, _target;
    private readonly int[] _distance;   // pushes a lone case on the cell needs to reach a place, -1 if it never can
    private readonly int[] _next;   // cell * 4 + direction (left, right, up, down; d ^ 1 is the opposite way): the floor cell beside it, or -1
    private readonly ulong[] _caseKeys, _regionKeys;

    /// <summary>The symbols of the cases in numbering order: case 1 is '1', case 10 is 'A', and so on (no 'X': that marks a place for a case).</summary>
    public const string CaseSymbols = "123456789ABCDEFGHIJKLMNOPQRSTUVWYZ";

    /// <summary>The most cells a warehouse may have; positions are searched with per-cell scratch space on the stack.</summary>
    public const int MaxCells = 2048;

    /// <summary>
    /// Creates the rules from the layout as data. Cells are numbered row by row: <c>row * columns + column</c>. Every cell named in
    /// <paramref name="targets"/>, <paramref name="cases"/> and <paramref name="player"/> must be floor. Case <c>i</c> starts on <c>cases[i]</c>; there must be
    /// as many places as cases. The puzzle is solved when every case stands on a place.
    /// </summary>
    /// <exception cref="ArgumentException">The layout is empty, has more than <see cref="MaxCells"/> cells or 256 cases, puts something off the floor, or has a different number of places and cases.</exception>
    public PushDomain(int columns, int rows, IEnumerable<int> floor, IEnumerable<int> targets, IReadOnlyList<int> cases, int player, bool pruneDeadSquares = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(cases);
        if ((long)columns * rows > MaxCells) throw new ArgumentException($"The warehouse has at most {MaxCells} cells.");
        Columns = columns;
        Rows = rows;
        int cells = columns * rows;
        PruneDeadSquares = pruneDeadSquares;
        _floor = new bool[cells];
        _target = new bool[cells];
        foreach (int cell in floor) _floor[CheckedCell(cell, nameof(floor))] = true;
        int targetCount = 0;
        foreach (int cell in targets)
        {
            if (!_floor[CheckedCell(cell, nameof(targets))]) throw new ArgumentException($"Place {cell} is not floor.", nameof(targets));
            if (_target[cell]) throw new ArgumentException($"Place {cell} is listed twice.", nameof(targets));
            _target[cell] = true;
            targetCount++;
        }
        if (!_floor[CheckedCell(player, nameof(player))]) throw new ArgumentException("The storeman is not on the floor.", nameof(player));
        if (cases.Count == 0) throw new ArgumentException("There are no cases.", nameof(cases));
        if (cases.Count > byte.MaxValue + 1) throw new ArgumentException("At most 256 cases.", nameof(cases));
        if (cases.Count != targetCount) throw new ArgumentException("There must be as many places as cases.", nameof(cases));
        CaseCount = cases.Count;
        var cellOf = new ushort[CaseCount];
        var taken = new bool[cells];
        for (int id = 0; id < cellOf.Length; id++)
        {
            int cell = cases[id];
            if (!_floor[CheckedCell(cell, nameof(cases))]) throw new ArgumentException($"Case {id} is not on the floor.", nameof(cases));
            if (taken[cell]) throw new ArgumentException($"Two cases are on cell {cell}.", nameof(cases));
            taken[cell] = true;
            cellOf[id] = (ushort)cell;
        }
        if (taken[player]) throw new ArgumentException("The storeman stands on a case.", nameof(player));
        Targets = [.. Enumerable.Range(0, cells).Where(i => _target[i])];

        int CheckedCell(int cell, string name) =>
            cell >= 0 && cell < cells ? cell : throw new ArgumentException($"Cell {cell} is outside the warehouse.", name);

        _next = new int[cells * 4];
        ReadOnlySpan<(int Row, int Col)> steps = [(0, -1), (0, 1), (-1, 0), (1, 0)];
        for (int cell = 0; cell < cells; cell++)
            for (int d = 0; d < 4; d++)
            {
                int r = cell / Columns + steps[d].Row, c = cell % Columns + steps[d].Col;
                bool inside = r >= 0 && r < Rows && c >= 0 && c < Columns;
                _next[cell * 4 + d] = inside && _floor[r * Columns + c] ? r * Columns + c : -1;
            }

        var random = new Random(0x50C0BA);
        _caseKeys = new ulong[cells];
        _regionKeys = new ulong[cells];
        for (int i = 0; i < cells; i++)
        {
            _caseKeys[i] = (ulong)random.NextInt64();
            _regionKeys[i] = (ulong)random.NextInt64();
        }
        _distance = FindPushDistances();

        ulong caseHash = 0;
        foreach (ushort cell in cellOf) caseHash ^= _caseKeys[cell];
        InitialState = Make(cellOf, (ushort)player, caseHash);
    }

    /// <summary>
    /// Reads the warehouse from a map with one string per row: '#' is a wall, ' ' is outside the warehouse, '.' is floor, 'X' is a place for a case,
    /// '@' is the storeman and a symbol of <see cref="CaseSymbols"/> is a case (numbered 1, 2, 3 and so on without gaps). Rows may differ in length;
    /// the rest of a short row is outside. There must be as many places as cases.
    /// </summary>
    public PushDomain(IReadOnlyList<string> map, bool pruneDeadSquares = true)
        : this(ParseMap(map), pruneDeadSquares)
    {
    }

    private PushDomain(MapData layout, bool pruneDeadSquares)
        : this(layout.Columns, layout.Rows, layout.Floor, layout.Targets, layout.Cases, layout.Player, pruneDeadSquares)
    {
    }

    private readonly record struct MapData(int Columns, int Rows, List<int> Floor, List<int> Targets, List<int> Cases, int Player);

    private static MapData ParseMap(IReadOnlyList<string> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Count == 0) throw new ArgumentException("The map is empty.", nameof(map));
        int columns = map.Max(row => row.Length), rows = map.Count;
        if (columns == 0) throw new ArgumentException("The map is empty.", nameof(map));
        List<int> floor = [], targets = [];
        var cases = new SortedDictionary<int, int>();
        int player = -1;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < map[r].Length; c++)
            {
                char ch = map[r][c];
                int cell = r * columns + c;
                if (ch is '#' or ' ') continue;
                floor.Add(cell);
                if (ch == 'X') targets.Add(cell);
                else if (ch == '@') player = cell;
                else if (CaseSymbols.IndexOf(ch, StringComparison.Ordinal) is int id and >= 0) cases[id] = cell;
                else if (ch != '.') throw new ArgumentException($"Unknown map symbol '{ch}'.", nameof(map));
            }
        if (player < 0) throw new ArgumentException("The map has no storeman.", nameof(map));
        if (cases.Count == 0) throw new ArgumentException("The map has no cases.", nameof(map));
        if (cases.Keys.Where((id, i) => id != i).Any()) throw new ArgumentException("The cases must be numbered 1, 2, 3 and so on.", nameof(map));
        if (cases.Count != targets.Count) throw new ArgumentException("There must be as many places as cases.", nameof(map));
        return new MapData(columns, rows, floor, targets, [.. cases.Values], player);
    }

    /// <summary>Columns of the warehouse.</summary>
    public int Columns { get; }
    /// <summary>Rows of the warehouse.</summary>
    public int Rows { get; }
    /// <summary>How many cases there are.</summary>
    public int CaseCount { get; }
    /// <summary>Cells of the warehouse: columns times rows. A cell is <c>row * Columns + column</c>.</summary>
    public int CellCount => Columns * Rows;

    /// <summary>The cells marked for a case, lowest first.</summary>
    public IReadOnlyList<int> Targets { get; }

    /// <summary>When set, <see cref="GenerateMoves"/> leaves out pushes that leave a case where no push can ever take it to a place (for the solvers).</summary>
    public bool PruneDeadSquares { get; }

    /// <inheritdoc/>
    public PushState InitialState { get; }

    /// <inheritdoc/>
    public int MaxMoves => CaseCount * CellCount * 4;

    /// <summary>True if the cell is floor (not a wall and not outside).</summary>
    public bool IsFloor(int cell) => _floor[cell];
    /// <summary>True if the cell is a place for a case.</summary>
    public bool IsTarget(int cell) => _target[cell];

    /// <summary>True if a case on <paramref name="cell"/> could still be pushed to a place if nothing else were in the way.</summary>
    public bool IsAlive(int cell) => _distance[cell] >= 0;

    /// <inheritdoc/>
    public bool IsGoal(in PushState state)
    {
        for (int i = 0; i < state.CaseCount; i++)
            if (!_target[state.CellOf(i)]) return false;
        return true;
    }

    /// <summary>The number of cases not on a place: each has to move at least once, so this never overestimates the moves left.</summary>
    public int Heuristic(in PushState state)
    {
        int n = 0;
        for (int i = 0; i < state.CaseCount; i++)
            if (!_target[state.CellOf(i)]) n++;
        return n;
    }

    /// <summary>The pushes a lone case on <paramref name="cell"/> needs to reach a place if nothing else were in the way, or -1 if no push can.</summary>
    public int PushDistance(int cell) => _distance[cell];

    /// <inheritdoc/>
    public ulong GetHashCode64(in PushState state) => state.Hash;

    /// <inheritdoc/>
    public PushState ApplyMove(in PushState state, in PushMove move)
    {
        var cases = new ushort[state.CaseCount];
        for (int i = 0; i < cases.Length; i++) cases[i] = (ushort)state.CellOf(i);
        ulong caseHash = state.CaseHash ^ _caseKeys[cases[move.Case]] ^ _caseKeys[move.To];
        cases[move.Case] = move.To;
        return Make(cases, move.PlayerAt, caseHash);
    }

    /// <inheritdoc/>
    public int GenerateMoves(in PushState state, Span<PushMove> moveBuffer)
    {
        int n = 0;
        for (int id = 0; id < state.CaseCount; id++) n += Pushes(state, id, moveBuffer[n..], PruneDeadSquares);
        return n;
    }

    /// <summary>Every place the storeman can push case <paramref name="id"/> to in one move, with nothing left out.</summary>
    public IReadOnlyList<PushMove> PushesOf(in PushState state, int id)
    {
        var buffer = new PushMove[CellCount * 4];
        return [.. buffer.AsSpan(0, Pushes(state, id, buffer, false))];
    }

    private PushState Make(ushort[] cases, ushort player, ulong caseHash)
    {
        int cells = CellCount;
        Span<bool> blocked = stackalloc bool[cells], seen = stackalloc bool[cells];
        Span<ushort> stack = stackalloc ushort[cells];
        foreach (ushort c in cases) blocked[c] = true;
        int region = Flood(player, blocked, seen, stack);
        return new PushState(cases, player, (ushort)region, caseHash, caseHash ^ _regionKeys[region]);
    }

    /// <summary>Marks in <paramref name="seen"/> every cell reachable from <paramref name="from"/> without entering a blocked one; returns the lowest.</summary>
    private int Flood(int from, ReadOnlySpan<bool> blocked, Span<bool> seen, Span<ushort> stack)
    {
        seen.Clear();
        int top = 0, lowest = from;
        stack[top++] = (ushort)from;
        seen[from] = true;
        while (top > 0)
        {
            int cell = stack[--top];
            if (cell < lowest) lowest = cell;
            for (int d = 0; d < 4; d++)
            {
                int next = _next[cell * 4 + d];
                if (next < 0 || seen[next] || blocked[next]) continue;
                seen[next] = true;
                stack[top++] = (ushort)next;
            }
        }
        return lowest;
    }

    /// <summary>
    /// Writes the moves of one case. A search over where the case can be and which part of the floor the storeman can then reach: from each,
    /// the storeman steps behind the case (anywhere in that part will do), pushes it one cell, and the search goes on from there.
    /// </summary>
    private int Pushes(in PushState state, int id, Span<PushMove> output, bool pruneDead)
    {
        int cells = CellCount, start = state.CellOf(id), count = 0;
        Span<bool> blocked = stackalloc bool[cells], seen = stackalloc bool[cells], queued = stackalloc bool[cells * 4];
        Span<ushort> stack = stackalloc ushort[cells], queueBox = stackalloc ushort[cells * 4], queuePlayer = stackalloc ushort[cells * 4];
        for (int i = 0; i < state.CaseCount; i++) blocked[state.CellOf(i)] = true;
        blocked[start] = false;

        int words = (int)(((long)cells * cells + 63) / 64);
        ulong[] bits = ArrayPool<ulong>.Shared.Rent(words);
        Array.Clear(bits, 0, words);
        try
        {
            int head = 0, tail = 0, startRegion = -1;
            queueBox[tail] = (ushort)start;
            queuePlayer[tail++] = state.Player;
            while (head < tail)
            {
                int box = queueBox[head], player = queuePlayer[head++];
                blocked[box] = true;
                int region = Flood(player, blocked, seen, stack);
                if (startRegion < 0) startRegion = region;
                long key = (long)box * cells + region;
                if ((bits[key >> 6] & (1UL << (int)(key & 63))) != 0)
                {
                    blocked[box] = false;
                    continue;
                }
                bits[key >> 6] |= 1UL << (int)(key & 63);
                if (!(box == start && region == startRegion))
                    output[count++] = new PushMove((byte)id, (ushort)box, (ushort)player);
                for (int d = 0; d < 4; d++)
                {
                    int behind = _next[box * 4 + (d ^ 1)], ahead = _next[box * 4 + d];
                    if (behind < 0 || !seen[behind] || ahead < 0 || blocked[ahead] || pruneDead && _distance[ahead] < 0) continue;
                    int slot = ahead * 4 + d;
                    if (queued[slot]) continue;
                    queued[slot] = true;
                    queueBox[tail] = (ushort)ahead;
                    queuePlayer[tail++] = (ushort)box;
                }
                blocked[box] = false;
            }
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(bits);
        }
        return count;
    }

    /// <summary>Pushes each cell's case needs to reach a place, ignoring every other case: a search backwards from the places, pulling.</summary>
    private int[] FindPushDistances()
    {
        var distance = new int[CellCount];
        Array.Fill(distance, -1);
        var queue = new Queue<int>();
        foreach (int t in Targets)
        {
            distance[t] = 0;
            queue.Enqueue(t);
        }
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                // A case is pushed from `from` to `cell` in direction d when the storeman can stand on the cell beyond `from`.
                int from = _next[cell * 4 + (d ^ 1)];
                if (from < 0 || distance[from] >= 0 || _next[from * 4 + (d ^ 1)] < 0) continue;
                distance[from] = distance[cell] + 1;
                queue.Enqueue(from);
            }
        }
        return distance;
    }
}
