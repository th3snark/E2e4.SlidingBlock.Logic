using System.Globalization;
using System.Numerics;
using System.Text;
using System.Windows.Media;
using E2e4.SlidingBlock.Logic;

namespace E2e4.SlidingBlock.Logic.Labs.Wpf.Lab1;

public sealed record PieceView(int Id, string Label, Color Color, IReadOnlyList<(int R, int C)> Cells);
public sealed record MoveView(int Index, int PieceId, int DRow, int DCol);
public sealed record RegisterSpec(int GroupBits, int GroupCount, int GroupsPerRow, string Caption);

/// <summary>A playable puzzle plus everything the Lab 1 inspector needs to display about it.</summary>
public abstract class PuzzleSession : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Representation { get; }
    public abstract int Rows { get; }
    public abstract int Cols { get; }
    public abstract RegisterSpec RegisterLayout { get; }
    public abstract string OccupancyCaption { get; }
    public abstract IReadOnlyList<PieceView> Pieces { get; }
    public abstract IReadOnlyList<MoveView> LegalMoves { get; }
    public abstract ulong Register { get; }
    public abstract ulong Occupancy { get; }
    public abstract GroupInfo GroupInfoFor(int group);

    public int MoveCount { get; protected set => Set(ref field, value); }
    public string Trace { get; protected set => Set(ref field, value); } = "No move yet. Click a block or an arrow.";
    public bool IsSolved { get; protected set => Set(ref field, value); }
    public ulong ChangedBits { get; protected set => Set(ref field, value); }
    public string RegisterHex => $"0x{Register:X16}";
    public string OccupancyHex => $"0x{Occupancy:X}";

    /// <summary>Raised after the state changed; argument is true when the change came from a move (animate).</summary>
    public event Action<bool>? Changed;

    protected void NotifyChanged(bool animate)
    {
        Raise(nameof(RegisterHex));
        Raise(nameof(OccupancyHex));
        Changed?.Invoke(animate);
    }

    public abstract void Apply(int moveIndex);
    public abstract void Reset();
    public abstract Task<bool> HintStepAsync(CancellationToken ct);

    public void RandomMove()
    {
        var moves = LegalMoves;
        if (moves.Count > 0) Apply(moves[Random.Shared.Next(moves.Count)].Index);
    }

    public static Color Hue(double hue, double sat = 0.55, double val = 0.85)
    {
        double c = val * sat, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = val - c;
        (double r, double g, double b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}

/// <summary>Shared plumbing: state, legal moves, undo-free history and cached solver hints.</summary>
public abstract class PuzzleSession<TState, TMove> : PuzzleSession
    where TState : struct, IEquatable<TState>
    where TMove : struct, IEquatable<TMove>
{
    private readonly ISlidingBlockDomain<TState, TMove> _domain;
    private readonly TMove[] _buffer;
    private int _legalCount;
    private readonly Queue<TMove> _hint = new();
    private IReadOnlyList<MoveView> _legalViews = [];
    private IReadOnlyList<PieceView> _pieces = [];

    protected PuzzleSession(ISlidingBlockDomain<TState, TMove> domain)
    {
        _domain = domain;
        _buffer = new TMove[domain.MaxMoves];
        State = domain.InitialState;
    }

    protected TState State { get; private set; }
    protected ISlidingBlockDomain<TState, TMove> Domain => _domain;

    protected abstract IReadOnlyList<PieceView> BuildPieces(TState state);
    protected abstract MoveView Describe(TState state, TMove move, int index);
    protected abstract ulong RegisterOf(TState state);
    protected abstract ulong OccupancyOf(TState state);
    protected abstract string BuildTrace(TState before, TMove move, TState after);
    protected virtual SolveResult<TMove>? Solve(TState start, CancellationToken ct) => new PuzzleSolver().SolveBfs(_domain, start, ct);

    public override IReadOnlyList<PieceView> Pieces => _pieces;
    public override IReadOnlyList<MoveView> LegalMoves => _legalViews;
    public override ulong Register => RegisterOf(State);
    public override ulong Occupancy => OccupancyOf(State);

    protected void Refresh(bool animate, ulong changed)
    {
        _pieces = BuildPieces(State);
        _legalCount = _domain.GenerateMoves(State, _buffer);
        var views = new MoveView[_legalCount];
        for (int i = 0; i < _legalCount; i++) views[i] = Describe(State, _buffer[i], i);
        _legalViews = views;
        ChangedBits = changed;
        IsSolved = _domain.IsGoal(State);
        NotifyChanged(animate);
    }

    public override void Apply(int moveIndex)
    {
        TState before = State;
        TMove move = _buffer[moveIndex];
        State = _domain.ApplyMove(before, move);
        MoveCount++;
        if (_hint.Count > 0 && _hint.Peek().Equals(move)) _hint.Dequeue(); else _hint.Clear();
        Trace = BuildTrace(before, move, State);
        Refresh(true, RegisterOf(before) ^ RegisterOf(State));
    }

    public override void Reset()
    {
        State = _domain.InitialState;
        MoveCount = 0;
        _hint.Clear();
        Trace = "State reset. Click a block or an arrow.";
        Refresh(false, 0);
    }

    protected void ResetTo(TState state)
    {
        State = state;
        MoveCount = 0;
        _hint.Clear();
        Refresh(false, 0);
    }

    public override async Task<bool> HintStepAsync(CancellationToken ct)
    {
        if (IsSolved) return false;
        if (_hint.Count == 0)
        {
            var start = State;
            var result = await Task.Run(() => Solve(start, ct), ct);
            if (result is null || result.Moves.Count == 0) return false;
            foreach (var m in result.Moves) _hint.Enqueue(m);
        }
        TMove next = _hint.Peek();
        for (int i = 0; i < _legalCount; i++)
            if (_buffer[i].Equals(next)) { Apply(i); return true; }
        _hint.Clear();
        return false;
    }

    protected static string Row(string name, string value) => $"{name,-26}{value}\n";
}

// ---------------------------------------------------------------------------------------------------------------
public sealed class NPuzzleSession : PuzzleSession<NPuzzleState, NPuzzleMove>
{
    public NPuzzleSession() : base(new NPuzzleDomain(new NPuzzleState(NPuzzleState.Solved)))
    {
        ResetTo(NPuzzleDomain.Scramble(30, new Random(15)));
        scrambled = State;
    }

    private readonly NPuzzleState scrambled;

    public override string Title => "15-Puzzle";
    public override string Representation => "16 squares x 4 bits = one ulong. Nibble i holds the tile on square i (0 = blank). Sliding a tile is a branchless XOR swap.";
    public override int Rows => 4;
    public override int Cols => 4;
    public override RegisterSpec RegisterLayout => new(4, 16, 8, "nibble per square");
    public override string OccupancyCaption => "non-blank squares (16-bit)";

    public override void Reset() => ResetTo(scrambled);

    public override GroupInfo GroupInfoFor(int group)
    {
        int tile = State.TileAt(group);
        return new GroupInfo(tile == 0 ? Color.FromRgb(0x47, 0x55, 0x69) : Hue(tile * 24), $"s{group}:{tile}");
    }

    protected override IReadOnlyList<PieceView> BuildPieces(NPuzzleState s)
    {
        var list = new List<PieceView>();
        for (int sq = 0; sq < 16; sq++)
        {
            int t = s.TileAt(sq);
            if (t == 0) continue;
            list.Add(new PieceView(t, t.ToString(CultureInfo.InvariantCulture), Hue(t * 24, t == sq ? 0.7 : 0.38, t == sq ? 0.9 : 0.62), [(sq >> 2, sq & 3)]));
        }
        return list;
    }

    protected override MoveView Describe(NPuzzleState s, NPuzzleMove m, int index) =>
        new(index, s.TileAt(m.From), (m.To >> 2) - (m.From >> 2), (m.To & 3) - (m.From & 3));

    protected override ulong RegisterOf(NPuzzleState s) => s.PackedNibbles;

    protected override ulong OccupancyOf(NPuzzleState s)
    {
        ulong occ = 0;
        for (int sq = 0; sq < 16; sq++) if (s.TileAt(sq) != 0) occ |= 1UL << sq;
        return occ;
    }

    protected override string BuildTrace(NPuzzleState before, NPuzzleMove m, NPuzzleState after)
    {
        ulong tile = (before.PackedNibbles >> (m.From << 2)) & 0xF;
        ulong mask = before.SlideMask(m.From);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"tile {tile} on square {m.From} slides into blank on square {m.To}\n\n");
        sb.Append(Row("v", $"0x{before.PackedNibbles:X16}"));
        sb.Append(Row($"tile = (v >> 4*{m.From}) & 0xF", $"0x{tile:X}"));
        sb.Append(Row($"mask = tile<<4*{m.To} ^ tile<<4*{m.From}", $"0x{mask:X16}"));
        sb.Append(Row("v' = v ^ mask", $"0x{after.PackedNibbles:X16}"));
        ulong hz = (after.PackedNibbles - 0x1111111111111111UL) & ~after.PackedNibbles & 0x8888888888888888UL;
        sb.Append(Row("haszero(v')", $"0x{hz:X16}"));
        sb.Append(Row("blank = tzcnt(haszero) >> 2", $"{BitOperations.TrailingZeroCount(hz) >> 2}"));
        sb.Append(Row("Manhattan(v')", $"{after.ManhattanDistance}"));
        sb.Append(Row("inverse map (tile->square)", $"0x{after.InverseMap:X16}"));
        return sb.ToString();
    }

    protected override SolveResult<NPuzzleMove>? Solve(NPuzzleState start, CancellationToken ct) =>
        new PuzzleSolver { MaxNodes = 60_000_000 }.SolveIdaStar(Domain, start, NPuzzleDomain.Manhattan, ct);
}

// ---------------------------------------------------------------------------------------------------------------
public sealed class KlotskiSession : PuzzleSession<KlotskiState, KlotskiMove>
{
    private readonly KlotskiDomain _k;

    private KlotskiSession(KlotskiDomain d) : base(d)
    {
        _k = d;
        Reset();
    }

    public KlotskiSession() : this(KlotskiDomain.CreateClassic()) { }

    public override string Title => "Klotski";
    public override string Representation => "4x5 board = 20-bit occupancy. A 60-bit canonical shape key (3 bits per cell, class 0..4) identifies a position independent of which identical piece is where.";
    public override int Rows => 5;
    public override int Cols => 4;
    public override RegisterSpec RegisterLayout => new(3, 20, 10, "3-bit shape code per cell");
    public override string OccupancyCaption => "occupied cells (20-bit)";

    public override GroupInfo GroupInfoFor(int group)
    {
        int cls = (int)((State.CanonicalShapeKey >> (3 * group)) & 7);
        Color c = cls switch
        {
            0 => Color.FromRgb(0x47, 0x55, 0x69),
            _ => DrawHelpers.Hex(_k.Topology.Groups[cls - 1].ColorHex),
        };
        return new GroupInfo(c, $"c{group}");
    }

    protected override IReadOnlyList<PieceView> BuildPieces(KlotskiState s)
    {
        var t = _k.Topology;
        var list = new List<PieceView>();
        foreach (var p in t.Pieces)
        {
            var g = t.Groups[p.GroupId];
            int a = s.AnchorOf(p.PieceId);
            var cells = g.RelativeCells.Select(x => (a / t.Width + x.dRow, a % t.Width + x.dCol)).ToArray();
            list.Add(new PieceView(p.PieceId, g.Name.StartsWith("Master", StringComparison.Ordinal) ? "2x2" : "", DrawHelpers.Hex(g.ColorHex), cells));
        }
        return list;
    }

    protected override MoveView Describe(KlotskiState s, KlotskiMove m, int index)
    {
        int w = _k.Topology.Width;
        return new MoveView(index, m.Piece, m.To / w - m.From / w, m.To % w - m.From % w);
    }

    protected override ulong RegisterOf(KlotskiState s) => s.CanonicalShapeKey;
    protected override ulong OccupancyOf(KlotskiState s) => s.OccupancyMask;

    protected override string BuildTrace(KlotskiState before, KlotskiMove m, KlotskiState after)
    {
        var t = _k.Topology;
        var piece = t.Pieces[m.Piece];
        var g = t.Groups[piece.GroupId];
        ulong pieceMask = g.BaseMask << m.From, other = before.OccupancyMask ^ pieceMask, target = g.BaseMask << m.To;
        ulong delta = (KlotskiState.SpreadCells(pieceMask) ^ KlotskiState.SpreadCells(target)) * (ulong)(g.GroupId + 1);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"piece '{piece.Label}' ({g.Name}, shape class {g.GroupId + 1}) anchor {m.From} -> {m.To}\n\n");
        sb.Append(Row("baseMask[g]", $"0x{g.BaseMask:X}"));
        sb.Append(Row($"pieceMask = base << {m.From}", $"0x{pieceMask:X5}"));
        sb.Append(Row("other = occ ^ pieceMask", $"0x{other:X5}"));
        sb.Append(Row($"target = base << {m.To}", $"0x{target:X5}"));
        sb.Append(Row($"(valid[g] >> {m.To}) & 1", $"{(g.ValidAnchorsMask >> m.To) & 1}"));
        sb.Append(Row("(other & target) == 0", $"0x{other & target:X} -> legal"));
        sb.Append(Row("occ' = occ ^ piece ^ target", $"0x{after.OccupancyMask:X5}"));
        sb.Append(Row($"delta = (spread(old)^spread(new))*{g.GroupId + 1}", $"0x{delta:X15}"));
        sb.Append(Row("key' = key ^ delta", $"0x{after.CanonicalShapeKey:X15}"));
        return sb.ToString();
    }
}

// ---------------------------------------------------------------------------------------------------------------
public sealed class RushHourSession : PuzzleSession<RushHourState, RushHourMove>
{
    private readonly RushHourDomain _r;

    private RushHourSession(RushHourDomain d) : base(d)
    {
        _r = d;
        Reset();
    }

    public RushHourSession() : this(RushHourDomain.CreateDefault()) { }

    public override string Title => "Rush Hour";
    public override string Representation => "Each vehicle has one degree of freedom: a 3-bit offset along its lane. Occupancy is rebuilt from VehicleMasks[v, offset]; legality tests one FrontNeighborBit.";
    public override int Rows => 6;
    public override int Cols => 6;
    public override RegisterSpec RegisterLayout => new(3, _r.Topology.VehicleCount, 8, "3-bit offset per vehicle");
    public override string OccupancyCaption => "occupied cells (36-bit)";

    private Color VehicleColor(int v) => v == 0 ? Color.FromRgb(0xEF, 0x44, 0x44) : PuzzleSession.Hue(v * 47 + 20, 0.5, 0.85);

    public override GroupInfo GroupInfoFor(int group) => new(VehicleColor(group), _r.Topology.Vehicles[group].Label.ToString());

    protected override IReadOnlyList<PieceView> BuildPieces(RushHourState s)
    {
        var list = new List<PieceView>();
        foreach (var v in _r.Topology.Vehicles)
        {
            int off = s.OffsetOf(v.Index);
            var cells = Enumerable.Range(0, v.Length).Select(k => v.IsHorizontal ? (v.Lane, off + k) : (off + k, v.Lane)).ToArray();
            list.Add(new PieceView(v.Index, v.Label.ToString(), VehicleColor(v.Index), cells));
        }
        return list;
    }

    protected override MoveView Describe(RushHourState s, RushHourMove m, int index)
    {
        bool h = _r.Topology.Vehicles[m.Vehicle].IsHorizontal;
        return new MoveView(index, m.Vehicle, h ? 0 : m.Delta, h ? m.Delta : 0);
    }

    protected override ulong RegisterOf(RushHourState s) => s.PackedOffsets;
    protected override ulong OccupancyOf(RushHourState s) => s.Occupancy(_r.Topology);

    protected override string BuildTrace(RushHourState before, RushHourMove m, RushHourState after)
    {
        var topo = _r.Topology;
        var veh = topo.Vehicles[m.Vehicle];
        int off = before.OffsetOf(m.Vehicle);
        ulong occ = before.Occupancy(topo);
        ulong nb = m.Delta > 0 ? topo.FrontNeighborBit[m.Vehicle, off] : topo.BackNeighborBit[m.Vehicle, off];
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"vehicle {veh.Label} ({(veh.IsHorizontal ? "row" : "col")} {veh.Lane}, len {veh.Length}) offset {off} -> {off + m.Delta}\n\n");
        sb.Append(Row("occ = OR VehicleMasks[v,off[v]]", $"0x{occ:X9}"));
        sb.Append(Row($"VehicleMasks[{veh.Label},{off}]", $"0x{topo.VehicleMasks[m.Vehicle, off]:X9}"));
        sb.Append(Row(m.Delta > 0 ? "FrontNeighborBit" : "BackNeighborBit", $"0x{nb:X9}"));
        sb.Append(Row("(occ & neighbor) == 0", $"0x{occ & nb:X} -> legal"));
        sb.Append(Row($"packed {(m.Delta > 0 ? "+" : "-")}= 1 << 3*{m.Vehicle}", $"0x{(1UL << (3 * m.Vehicle)):X}"));
        sb.Append(Row("packed'", $"0x{after.PackedOffsets:X16}"));
        sb.Append(Row("occ'", $"0x{after.Occupancy(topo):X9}"));
        return sb.ToString();
    }
}
