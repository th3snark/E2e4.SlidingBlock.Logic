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

    /// <summary>Board cells (bitboard) that register group <paramref name="group"/> describes.</summary>
    public abstract ulong CellsOfGroup(int group);
    public virtual string PieceMaskName(int pieceId) => "pieceMask";

    /// <summary>Optional companion register (the 15-puzzle's inverse map).</summary>
    public virtual string? SecondaryCaption => null;
    public virtual ulong SecondaryRegister => 0;
    public virtual RegisterSpec SecondaryLayout => new(4, 16, 8, "");
    public virtual GroupInfo SecondaryGroupInfoFor(int group) => default;

    public ulong PreviousRegister { get; protected set; }
    public abstract bool CanUndo { get; }
    public abstract void Undo();
    public abstract void Scramble();
    /// <summary>Formula trace for legal move <paramref name="moveIndex"/> without playing it.</summary>
    public abstract string Preview(int moveIndex);

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

    public void RandomMove()
    {
        var moves = LegalMoves;
        if (moves.Count > 0) Apply(moves[Random.Shared.Next(moves.Count)].Index);
    }
}

/// <summary>Shared plumbing: state, legal moves and undo history.</summary>
public abstract class PuzzleSession<TState, TMove> : PuzzleSession
    where TState : struct, IEquatable<TState>
    where TMove : struct
{
    private readonly ISlidingBlockDomain<TState, TMove> _domain;
    private readonly PlaySession<TState, TMove> _play;
    private readonly TMove[] _buffer;
    private int _legalCount;
    private IReadOnlyList<MoveView> _legalViews = [];
    private IReadOnlyList<PieceView> _pieces = [];

    protected PuzzleSession(ISlidingBlockDomain<TState, TMove> domain)
    {
        _domain = domain;
        _play = new PlaySession<TState, TMove>(domain, domain.InitialState);
        _buffer = new TMove[domain.MaxMoves];
    }

    protected TState State => _play.State;
    protected ISlidingBlockDomain<TState, TMove> Domain => _domain;

    protected abstract IReadOnlyList<PieceView> BuildPieces(TState state);
    protected abstract MoveView Describe(TState state, TMove move, int index);
    protected abstract ulong RegisterOf(TState state);
    protected abstract ulong OccupancyOf(TState state);
    protected abstract string BuildTrace(TState before, TMove move, TState after);

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
        PreviousRegister = RegisterOf(State) ^ changed;
        IsSolved = _domain.IsGoal(State);
        NotifyChanged(animate);
    }

    public override void Apply(int moveIndex)
    {
        TState before = State;
        TMove move = _buffer[moveIndex];
        _play.Apply(move);
        MoveCount = _play.MoveCount;
        Trace = BuildTrace(before, move, State);
        Refresh(true, RegisterOf(before) ^ RegisterOf(State));
    }

    public override bool CanUndo => _play.CanUndo;

    public override void Undo()
    {
        TState undone = State;
        if (!_play.Undo()) return;
        MoveCount = _play.MoveCount;
        Trace = $"Undo: register restored from 0x{RegisterOf(undone):X16} to 0x{RegisterOf(State):X16}\nxor delta = 0x{RegisterOf(undone) ^ RegisterOf(State):X16}";
        Refresh(true, RegisterOf(undone) ^ RegisterOf(State));
    }

    public override string Preview(int moveIndex)
    {
        TMove move = _buffer[moveIndex];
        return BuildTrace(State, move, _domain.ApplyMove(State, move));
    }

    /// <summary>Random walk from the initial state: always reachable, hence solvable.</summary>
    public override void Scramble()
    {
        Trace = "Scrambled by an 80-move random walk from the initial state (always solvable).";
        ResetTo(PuzzleWalk.RandomWalk(_domain, _domain.InitialState, 80, Random.Shared));
    }

    public override void Reset()
    {
        Trace = "State reset. Click a block or an arrow.";
        ResetTo(_domain.InitialState);
    }

    protected void ResetTo(TState state)
    {
        _play.Begin(state);
        MoveCount = 0;
        Refresh(false, 0);
    }

    protected static string Row(string name, string value) => $"{name.PadRight(Math.Max(26, name.Length + 2))}{value}\n";
}

// ---------------------------------------------------------------------------------------------------------------
public sealed class NPuzzleSession : PuzzleSession<NPuzzle4x4State, NPuzzle4x4Move>
{
    private readonly NPuzzle4x4Domain _n;
    private NPuzzle4x4State _scrambled;

    private NPuzzleSession(NPuzzle4x4Domain d) : base(d)
    {
        _n = d;
        _scrambled = PuzzleWalk.RandomWalk(d, d.Goal, 30, new Random(15));
        ResetTo(_scrambled);
    }

    public NPuzzleSession() : this(LabPuzzles.CreatePuzzle15()) { }

    public override string Title => "15-Puzzle";
    public override string Representation => "16 squares x 4 bits = one ulong. Nibble i holds the tile on square i (0 = blank). Sliding a tile is a branchless XOR swap.";
    public override int Rows => 4;
    public override int Cols => 4;
    public override RegisterSpec RegisterLayout => new(4, 16, 8, "nibble per square");
    public override string OccupancyCaption => "non-blank squares (16-bit)";

    public override void Reset() => ResetTo(_scrambled);

    public override void Scramble()
    {
        _scrambled = PuzzleWalk.RandomWalk(_n, _n.Goal, 60, Random.Shared);
        Trace = "New solvable scramble (60-move random walk from the solved board).";
        ResetTo(_scrambled);
    }

    public override ulong CellsOfGroup(int group) => 1UL << group;
    public override string PieceMaskName(int pieceId) => $"tile {pieceId} mask";

    public override GroupInfo GroupInfoFor(int group)
    {
        int tile = State.TileAt(group);
        string name = tile == 0 ? "blank" : $"tile {tile}";
        string bits = Convert.ToString(tile, 2).PadLeft(4, '0');
        return new GroupInfo(tile == 0 ? Palette.Slate : DrawHelpers.Hue(tile * 24), $"cell {group}", tile == 0 ? "blank" : $"{tile} = {tile:X}",
            $"Nibble {group} (bits {group * 4}..{group * 4 + 3})\ncell {group} ({group >> 2},{group & 3})\n0b{bits} = 0x{tile:X} -> {name}");
    }

    public override string? SecondaryCaption =>
        $"InversePackedNibbles (nibble t = cell of tile t)   BlankCell = inverse & 0xF = {(int)(State.InverseMap & 0xF)}";
    public override ulong SecondaryRegister => State.InverseMap;
    public override GroupInfo SecondaryGroupInfoFor(int group)
    {
        int tile = group;
        int cell = (int)((State.InverseMap >> (tile * 4)) & 0xF);
        return new GroupInfo(tile == 0 ? Palette.Slate : DrawHelpers.Hue(tile * 24), tile == 0 ? "blank" : $"tile {tile}", $"cell {cell}");
    }

    protected override IReadOnlyList<PieceView> BuildPieces(NPuzzle4x4State s)
    {
        var list = new List<PieceView>();
        for (int sq = 0; sq < 16; sq++)
        {
            int t = s.TileAt(sq);
            if (t == 0) continue;
            list.Add(new PieceView(t, t.ToString(CultureInfo.InvariantCulture), DrawHelpers.Hue(t * 24, t == sq ? 0.7 : 0.38, t == sq ? 0.9 : 0.62), [(sq >> 2, sq & 3)]));
        }
        return list;
    }

    protected override MoveView Describe(NPuzzle4x4State s, NPuzzle4x4Move m, int index) =>
        new(index, s.TileAt(m.From), (m.To >> 2) - (m.From >> 2), (m.To & 3) - (m.From & 3));

    protected override ulong RegisterOf(NPuzzle4x4State s) => s.PackedNibbles;

    protected override ulong OccupancyOf(NPuzzle4x4State s)
    {
        ulong occ = 0;
        for (int sq = 0; sq < 16; sq++) if (s.TileAt(sq) != 0) occ |= 1UL << sq;
        return occ;
    }

    protected override string BuildTrace(NPuzzle4x4State before, NPuzzle4x4Move m, NPuzzle4x4State after)
    {
        ulong tile = (before.PackedNibbles >> (m.From << 2)) & 0xF;
        ulong mask = before.SlideMask(m.From);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"tile {tile} on square {m.From} slides into blank on square {m.To}\n\n");
        sb.Append(Row("srcCell, blankCell", $"{m.From}, {m.To}   (nibble shifts {4 * m.From}, {4 * m.To})"));
        sb.Append(Row("v", $"0x{before.PackedNibbles:X16}"));
        sb.Append(Row($"tile = (v >> 4*{m.From}) & 0xF", $"0x{tile:X}"));
        sb.Append(Row($"mask = tile<<4*{m.To} ^ tile<<4*{m.From}", $"0x{mask:X16}"));
        sb.Append(Row("v' = v ^ mask", $"0x{after.PackedNibbles:X16}"));
        sb.Append(Row("occupancy' (non-blank)", $"0x{OccupancyOf(after):X4}"));
        ulong hz = (after.PackedNibbles - 0x1111111111111111UL) & ~after.PackedNibbles & 0x8888888888888888UL;
        sb.Append(Row("haszero(v')", $"0x{hz:X16}"));
        sb.Append(Row("blank = tzcnt(haszero) >> 2", $"{BitOperations.TrailingZeroCount(hz) >> 2}"));
        sb.Append(Row("Manhattan(v')", $"{_n.ManhattanDistance(after)}"));
        sb.Append(Row("inverse map (tile->square)", $"0x{after.InverseMap:X16}"));
        return sb.ToString();
    }
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

    public KlotskiSession() : this(LabPuzzles.CreateKlotski()) { }

    public override string Title => "Klotski";
    public override string Representation => "4x5 board = 20-bit occupancy. A 60-bit canonical shape key (3 bits per cell, class 0..4) identifies a position independent of which identical piece is where.";
    public override int Rows => 5;
    public override int Cols => 4;
    public override RegisterSpec RegisterLayout => new(3, 20, 10, "3-bit shape code per cell");
    public override string OccupancyCaption => "occupied cells (20-bit)";

    private static readonly string[] ShapeNames = ["Empty", "1x1", "1x2V", "2x1H", "2x2"];

    public override ulong CellsOfGroup(int group) => 1UL << group;
    public override string PieceMaskName(int pieceId) => $"BaseMask << {State.AnchorOf(pieceId)}";

    public override GroupInfo GroupInfoFor(int group)
    {
        int cls = (int)((State.CanonicalShapeKey >> (3 * group)) & 7);
        Color c = cls switch
        {
            0 => Palette.Slate,
            _ => DrawHelpers.Hex(LabPuzzles.KlotskiColors[_k.Topology.Groups[cls - 1].Name]),
        };
        string name = ShapeNames[cls];
        return new GroupInfo(c, $"{group}:{(cls == 0 ? "--" : name)}", "",
            $"Cell {group} ({group / 4},{group % 4}) bits {group * 3}..{group * 3 + 2}\n0b{Convert.ToString(cls, 2).PadLeft(3, '0')} -> {name}");
    }

    protected override IReadOnlyList<PieceView> BuildPieces(KlotskiState s)
    {
        var t = _k.Topology;
        var list = new List<PieceView>();
        foreach (var p in t.Pieces)
        {
            var g = t.Groups[p.GroupId];
            int a = s.AnchorOf(p.PieceId);
            var cells = g.RelativeCells.Select(x => (a / t.Columns + x.dRow, a % t.Columns + x.dCol)).ToArray();
            list.Add(new PieceView(p.PieceId, p.PieceId == _k.MasterPieceId ? "2x2" : "", DrawHelpers.Hex(LabPuzzles.KlotskiColors[g.Name]), cells));
        }
        return list;
    }

    protected override MoveView Describe(KlotskiState s, KlotskiMove m, int index)
    {
        int w = _k.Topology.Columns;
        return new MoveView(index, m.Piece, m.To / w - m.From / w, m.To % w - m.From % w);
    }

    protected override ulong RegisterOf(KlotskiState s) => s.CanonicalShapeKey;
    protected override ulong OccupancyOf(KlotskiState s) => s.OccupancyMask;

    protected override string BuildTrace(KlotskiState before, KlotskiMove m, KlotskiState after)
    {
        var t = _k.Topology;
        var piece = t.Pieces[m.Piece];
        var g = t.Groups[piece.GroupId];
        Bitboard64 pieceMask = g.BaseMask << m.From, other = before.OccupancyMask ^ pieceMask, target = g.BaseMask << m.To;
        ulong delta = (KlotskiState.SpreadCells(pieceMask.Value) ^ KlotskiState.SpreadCells(target.Value)) * (ulong)(g.GroupId + 1);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"piece '{piece.Label}' ({g.Name}, shape class {g.GroupId + 1}) anchor {m.From} -> {m.To}\n\n");
        int dr = m.To / t.Columns - m.From / t.Columns, dc = m.To % t.Columns - m.From % t.Columns;
        int width = t.Columns, column = m.From % width;
        (string dir, string edgeTest) = (dr, dc) switch
        {
            (0, -1) => ("left", $"anchor column > 0  ({column} > 0)"),
            (0, 1) => ("right", $"anchor column < width - 1  ({column} < {width - 1})"),
            (-1, 0) => ("up", "no column test; valid[g] keeps the shape on the board"),
            _ => ("down", "no column test; valid[g] keeps the shape on the board"),
        };
        sb.Append(Row("baseMask[g]", $"0x{g.BaseMask:X}"));
        sb.Append(Row($"pieceMask = base << {m.From}", $"0x{pieceMask:X5}"));
        sb.Append(Row("OccupancyMask", $"0x{before.OccupancyMask:X5}"));
        sb.Append(Row("otherPieces = occ ^ pieceMask", $"0x{other:X5}"));
        sb.Append(Row($"move {dir}: edge test", edgeTest));
        sb.Append(Row($"(valid[g] >> {m.To}) & 1", $"{(g.ValidAnchorsMask >> m.To) & 1}  (1 -> the shape fits at anchor {m.To})"));
        sb.Append(Row($"target = base << {m.To}", $"0x{target:X5}"));
        sb.Append(Row("(other & target) == 0", $"0x{other & target:X} -> legal"));
        sb.Append(Row("occ' = occ ^ piece ^ target", $"0x{after.OccupancyMask:X5}"));
        sb.Append(Row($"delta = (spread(old)^spread(new))*{g.GroupId + 1}", $"0x{delta:X15}"));
        sb.Append(Row("key' = key ^ delta", $"0x{after.CanonicalShapeKey:X15}"));
        return sb.ToString();
    }
}

// ---------------------------------------------------------------------------------------------------------------
public sealed class RushHourSession : PuzzleSession<LaneState16, LaneMove>
{
    private readonly LaneSlideDomain<LaneState16, Bitboard64> _r;

    private RushHourSession(LaneSlideDomain<LaneState16, Bitboard64> d) : base(d)
    {
        _r = d;
        Reset();
    }

    public RushHourSession() : this(LabPuzzles.CreateRushHour()) { }

    private static char Label(int vehicle) => LabPuzzles.RushHourSymbols[vehicle];

    public override string Title => "Rush Hour";
    public override string Representation => "Each vehicle has one degree of freedom: a 4-bit offset along its lane (16 vehicles fit in one 64-bit register; the engine's lane-sliding domain also holds up to 64, in four words, for the garage puzzles). Occupancy is rebuilt from MaskAt(v, offset); legality tests the cells the vehicle would move into.";
    public override int Rows => 6;
    public override int Cols => 6;
    public override RegisterSpec RegisterLayout => new(4, _r.Vehicles.Count, 8, "4-bit offset per vehicle");
    public override string OccupancyCaption => "occupied cells (36-bit)";

    private Color VehicleColor(int v) => v == _r.GoalVehicle ? Color.FromRgb(0xEF, 0x44, 0x44) : DrawHelpers.Hue(v * 47 + 20, 0.5, 0.85);

    public override ulong CellsOfGroup(int group) => (ulong)_r.MaskAt(group, State.OffsetOf(group));
    public override string PieceMaskName(int pieceId) => $"MaskAt({pieceId},{State.OffsetOf(pieceId)})";

    public override GroupInfo GroupInfoFor(int group)
    {
        var v = _r.Vehicles[group];
        int off = State.OffsetOf(group);
        string axis = v.IsHorizontal ? "row" : "col";
        return new GroupInfo(VehicleColor(group), $"v{group}:{Label(group)}", $"{(v.IsHorizontal ? 'r' : 'c')}{v.Lane} off {off}",
            $"Vehicle {group} ({Label(group)}), {axis} {v.Lane}, length {v.Length}\nbits {group * 4}..{group * 4 + 3}\n0b{Convert.ToString(off, 2).PadLeft(4, '0')} -> offset {off}");
    }

    protected override IReadOnlyList<PieceView> BuildPieces(LaneState16 s)
    {
        var list = new List<PieceView>();
        foreach (var v in _r.Vehicles)
        {
            int off = s.OffsetOf(v.Index);
            var cells = Enumerable.Range(0, v.Length).Select(k => v.IsHorizontal ? (v.Lane, off + k) : (off + k, v.Lane)).ToArray();
            list.Add(new PieceView(v.Index, Label(v.Index).ToString(), VehicleColor(v.Index), cells));
        }
        return list;
    }

    protected override MoveView Describe(LaneState16 s, LaneMove m, int index)
    {
        bool h = _r.Vehicles[m.Vehicle].IsHorizontal;
        int delta = m.Offset - s.OffsetOf(m.Vehicle);
        return new MoveView(index, m.Vehicle, h ? 0 : delta, h ? delta : 0);
    }

    protected override ulong RegisterOf(LaneState16 s) => s.Word(0);
    protected override ulong OccupancyOf(LaneState16 s) => (ulong)_r.Occupancy(s);

    protected override string BuildTrace(LaneState16 before, LaneMove m, LaneState16 after)
    {
        var veh = _r.Vehicles[m.Vehicle];
        int off = before.OffsetOf(m.Vehicle);
        ulong occ = OccupancyOf(before), own = (ulong)_r.MaskAt(m.Vehicle, off), target = (ulong)_r.MaskAt(m.Vehicle, m.Offset);
        ulong packed = RegisterOf(before), clear = packed & ~(0xFUL << (4 * m.Vehicle));
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"vehicle {Label(m.Vehicle)} ({(veh.IsHorizontal ? "row" : "col")} {veh.Lane}, len {veh.Length}) offset {off} -> {m.Offset}\n\n");
        sb.Append(Row($"offset = (packed >> 4*{m.Vehicle}) & 0b1111", $"{off}   (packed 0x{packed:X16})"));
        sb.Append(Row("occ = OR MaskAt(v, off[v])", $"0x{occ:X9}"));
        sb.Append(Row($"MaskAt({Label(m.Vehicle)},{off})", $"0x{own:X9}"));
        sb.Append(Row($"MaskAt({Label(m.Vehicle)},{m.Offset})", $"0x{target:X9}"));
        sb.Append(Row("(occ & ~own & target) == 0", $"0x{occ & ~own & target:X} -> legal"));
        sb.Append(Row($"clear = packed & ~(15 << 4*{m.Vehicle})", $"0x{clear:X16}"));
        sb.Append(Row($"packed' = clear | ({m.Offset} << 4*{m.Vehicle})", $"0x{clear | ((ulong)m.Offset << (4 * m.Vehicle)):X16}"));
        sb.Append(Row("packed'", $"0x{RegisterOf(after):X16}"));
        sb.Append(Row("occ'", $"0x{OccupancyOf(after):X9}"));
        return sb.ToString();
    }
}
