using System.Numerics;
using System.Runtime.CompilerServices;

namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// A vehicle of a lane-sliding puzzle: a block of cells that slides only along its length. <paramref name="Length"/> is its extent along the
/// lane and <paramref name="Breadth"/> across it (1 for a car, 2 for a truck); <paramref name="Lane"/> is the row (horizontal) or column
/// (vertical) of its first cell and <paramref name="InitialOffset"/> the column or row of its first cell at the start.
/// </summary>
public sealed record LaneVehicle(int Index, bool IsHorizontal, int Length, int Breadth, int Lane, int InitialOffset)
{
    /// <summary>
    /// The symbols of a diagram, one per vehicle, ready for <see cref="Parse"/>: every character except <paramref name="empty"/>, in character
    /// order except that the ones in <paramref name="leadingLabels"/> come first, in that order (so the red car can be vehicle 0).
    /// </summary>
    public static string SymbolsOf(IReadOnlyList<string> rows, char empty = '.', string leadingLabels = "") =>
        string.Concat(rows.SelectMany(r => r).Where(c => c != empty).Distinct()
            .OrderBy(c => { int i = leadingLabels.IndexOf(c, StringComparison.Ordinal); return i < 0 ? int.MaxValue : i; }).ThenBy(c => c));

    /// <summary>
    /// Reads the vehicles off a diagram with one string per row. Vehicle <c>i</c> is the rectangle of cells carrying <c>symbols[i]</c>; it slides
    /// along its longer side (a square one along its columns). Every other character is empty floor.
    /// </summary>
    public static IReadOnlyList<LaneVehicle> Parse(IReadOnlyList<string> rows, string symbols)
    {
        int rowCount = rows.Count, columns = rows.Count == 0 ? 0 : rows[0].Length;
        if (rows.Any(r => r.Length != columns)) throw new ArgumentException("The rows of the diagram differ in length.", nameof(rows));
        var vehicles = new List<LaneVehicle>();
        for (int v = 0; v < symbols.Length; v++)
        {
            var cells = Enumerable.Range(0, rowCount * columns).Where(c => rows[c / columns][c % columns] == symbols[v]).ToList();
            if (cells.Count == 0) throw new ArgumentException($"Vehicle '{symbols[v]}' is not on the diagram.", nameof(symbols));
            int minRow = cells.Min(c => c / columns), maxRow = cells.Max(c => c / columns);
            int minCol = cells.Min(c => c % columns), maxCol = cells.Max(c => c % columns);
            int width = maxCol - minCol + 1, height = maxRow - minRow + 1;
            if (width * height != cells.Count) throw new ArgumentException($"Vehicle '{symbols[v]}' is not a rectangle.", nameof(rows));
            bool horizontal = width > height;
            vehicles.Add(new LaneVehicle(v, horizontal, horizontal ? width : height, horizontal ? height : width,
                horizontal ? minRow : minCol, horizontal ? minCol : minRow));
        }
        return vehicles;
    }
}

/// <summary>
/// A position of a lane-sliding puzzle, packed: where every vehicle stands along its lane, four bits each (lanes are at most 16 cells long).
/// The domain is generic over it so that a small puzzle pays only for the vehicles it has.
/// </summary>
public interface ILaneState<TState> : IEquatable<TState> where TState : struct, ILaneState<TState>
{
    /// <summary>How many vehicles the position holds: 16 to a 64-bit word.</summary>
    static abstract int Capacity { get; }

    /// <summary>The position of a vehicle along its lane: the column (horizontal) or row (vertical) of its first cell.</summary>
    int OffsetOf(int vehicle);

    TState WithOffset(int vehicle, int offset);

    /// <summary>One 64-bit word of the packed offsets: word 0 holds vehicles 0 to 15, word 1 vehicles 16 to 31, and so on.</summary>
    ulong Word(int index);

    ulong Hash();
}

/// <summary>A position of up to 16 vehicles, in one 64-bit word (Rush Hour).</summary>
public readonly struct LaneState16 : ILaneState<LaneState16>
{
    private readonly ulong _word;

    private LaneState16(ulong word) => _word = word;

    public static int Capacity => 16;

    public ulong Word(int index) => _word;
    public int OffsetOf(int vehicle) => (int)((_word >> (4 * vehicle)) & 0xF);
    public LaneState16 WithOffset(int vehicle, int offset) => new((_word & ~(0xFUL << (4 * vehicle))) | ((ulong)offset << (4 * vehicle)));
    public ulong Hash() => Hashing.Mix(_word);

    public bool Equals(LaneState16 other) => _word == other._word;
    public override bool Equals(object? obj) => obj is LaneState16 s && Equals(s);
    public override int GetHashCode() => _word.GetHashCode();
    public static bool operator ==(LaneState16 left, LaneState16 right) => left.Equals(right);
    public static bool operator !=(LaneState16 left, LaneState16 right) => !left.Equals(right);
}

/// <summary>A position of up to 64 vehicles, in four 64-bit words (the garages).</summary>
public readonly struct LaneState64 : ILaneState<LaneState64>
{
    private readonly ulong _w0, _w1, _w2, _w3;

    private LaneState64(ulong w0, ulong w1, ulong w2, ulong w3)
    {
        _w0 = w0;
        _w1 = w1;
        _w2 = w2;
        _w3 = w3;
    }

    public static int Capacity => 64;

    public ulong Word(int index) => Unsafe.Add(ref Unsafe.AsRef(in _w0), index & 3);   // the four words are laid out in order
    public int OffsetOf(int vehicle) => (int)((Word(vehicle >> 4) >> (4 * (vehicle & 15))) & 0xF);

    public LaneState64 WithOffset(int vehicle, int offset)
    {
        int shift = 4 * (vehicle & 15);
        ulong Replace(ulong word) => (word & ~(0xFUL << shift)) | ((ulong)offset << shift);
        return (vehicle >> 4) switch
        {
            0 => new LaneState64(Replace(_w0), _w1, _w2, _w3),
            1 => new LaneState64(_w0, Replace(_w1), _w2, _w3),
            2 => new LaneState64(_w0, _w1, Replace(_w2), _w3),
            _ => new LaneState64(_w0, _w1, _w2, Replace(_w3)),
        };
    }

    public ulong Hash() => Hashing.Mix(_w0, _w1, _w2, _w3);

    public bool Equals(LaneState64 other) => _w0 == other._w0 && _w1 == other._w1 && _w2 == other._w2 && _w3 == other._w3;
    public override bool Equals(object? obj) => obj is LaneState64 s && Equals(s);
    public override int GetHashCode() => Hash().GetHashCode();
    public static bool operator ==(LaneState64 left, LaneState64 right) => left.Equals(right);
    public static bool operator !=(LaneState64 left, LaneState64 right) => !left.Equals(right);
}

/// <summary>Slide a vehicle along its lane so that its first cell is at <paramref name="Offset"/> (a column or a row).</summary>
public readonly record struct LaneMove(byte Vehicle, byte Offset);

/// <summary>
/// The rules of vehicles that slide forwards or backwards along their length, never sideways or round a corner: Rush Hour, garages, shunting
/// yards. A move takes one vehicle through free cells, up to <c>maxStep</c> cells; <c>1</c> is Rush Hour's one cell at a time and the default
/// (any distance is one move) is the way Hordern counts the garage puzzles. The position is the packed lane offsets (like the engine's old
/// Rush Hour domain, but four bits each) in <typeparamref name="TState"/>: <see cref="LaneState16"/> for up to 16 vehicles, <see cref="LaneState64"/>
/// for up to 64, far past the polyomino domain's 16 pieces. <typeparamref name="TBitboard"/> is the board's cell register, as for the polyomino domain: <see cref="ulong"/> for boards of up to 64
/// cells (Rush Hour), <see cref="UInt128"/> for up to 128 (the garages). The puzzle is solved when the goal vehicle reaches the goal offset.
/// </summary>
public sealed class LaneSlideDomain<TState, TBitboard> : ISlidingBlockDomain<TState, LaneMove>
    where TState : struct, ILaneState<TState>
    where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>
{
    private const int Stride = 16;   // offsets per vehicle in the mask table: lanes are at most 16 cells
    // The cells covered at an offset, and the cells that sliding one cell forward / backward from an offset newly covers (every cell, so never free, at the end of the lane).
    private readonly TBitboard[] _masks, _forward, _backward;
    private readonly int _count;
    private readonly int[] _maxOffset;

    public LaneSlideDomain(IReadOnlyList<LaneVehicle> vehicles, int columns, int rows, int goalVehicle, int goalOffset, int maxStep = int.MaxValue)
    {
        if (vehicles.Count == 0) throw new ArgumentException("There must be at least one vehicle.", nameof(vehicles));
        if (vehicles.Count > TState.Capacity) throw new ArgumentException($"At most {TState.Capacity} vehicles.", nameof(vehicles));
        if (columns * rows > Grid.BitWidth<TBitboard>()) throw new ArgumentException($"The board has at most {Grid.BitWidth<TBitboard>()} cells.");
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStep, 1);
        if (goalVehicle < 0 || goalVehicle >= vehicles.Count) throw new ArgumentOutOfRangeException(nameof(goalVehicle));
        Vehicles = vehicles;
        Columns = columns;
        Rows = rows;
        GoalVehicle = goalVehicle;
        GoalOffset = goalOffset;
        MaxStep = maxStep;
        _maxOffset = [.. vehicles.Select(v => (v.IsHorizontal ? columns : rows) - v.Length)];
        if (_maxOffset.Any(m => m < 0 || m > 15)) throw new ArgumentException("A vehicle does not fit its lane, or the lane is longer than 16 cells.", nameof(vehicles));
        if (goalOffset < 0 || goalOffset > _maxOffset[goalVehicle]) throw new ArgumentOutOfRangeException(nameof(goalOffset));
        _count = vehicles.Count;
        _masks = new TBitboard[_count * Stride];
        _forward = new TBitboard[_count * Stride];
        _backward = new TBitboard[_count * Stride];
        foreach (var v in vehicles)
            for (int o = 0; o <= _maxOffset[v.Index]; o++) _masks[v.Index * Stride + o] = MaskOf(v, o);
        foreach (var v in vehicles)
            for (int o = 0; o <= _maxOffset[v.Index]; o++)
            {
                int i = v.Index * Stride + o;
                _forward[i] = o < _maxOffset[v.Index] ? _masks[i + 1] & ~_masks[i] : ~TBitboard.Zero;
                _backward[i] = o > 0 ? _masks[i - 1] & ~_masks[i] : ~TBitboard.Zero;
            }
        var state = default(TState);
        foreach (var v in vehicles) state = state.WithOffset(v.Index, v.InitialOffset);
        InitialState = state;
        MaxMoves = vehicles.Sum(v => 2 * Math.Min(_maxOffset[v.Index], maxStep));
    }

    public IReadOnlyList<LaneVehicle> Vehicles { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int GoalVehicle { get; }
    public int GoalOffset { get; }

    /// <summary>The most cells a vehicle slides in one move.</summary>
    public int MaxStep { get; }

    public TState InitialState { get; }
    public int MaxMoves { get; }

    /// <summary>The last offset a vehicle can have along its lane.</summary>
    public int MaxOffsetOf(int vehicle) => _maxOffset[vehicle];

    /// <summary>The cells a vehicle covers at an offset along its lane, as a bitboard.</summary>
    public TBitboard MaskAt(int vehicle, int offset) => _masks[vehicle * Stride + offset];

    /// <summary>The cells every vehicle covers in a position.</summary>
    public TBitboard Occupancy(in TState state)
    {
        TBitboard occupied = TBitboard.Zero;
        ReadOnlySpan<TBitboard> masks = _masks;
        for (int v = 0; v < _count; v++) occupied |= masks[v * Stride + state.OffsetOf(v)];
        return occupied;
    }

    /// <summary>The offsets a vehicle can slide to in one move from where it stands (not counting where it stands), nearest first on each side.</summary>
    public IEnumerable<int> ReachableOffsets(TState state, int vehicle)
    {
        int at = state.OffsetOf(vehicle), baseIndex = vehicle * Stride;
        var others = Occupancy(state) & ~_masks[baseIndex + at];
        int max = _maxOffset[vehicle], up = MaxStep < max - at ? at + MaxStep : max, down = MaxStep < at ? at - MaxStep : 0;
        for (int o = at + 1; o <= up && (_masks[baseIndex + o] & others) == TBitboard.Zero; o++) yield return o;
        for (int o = at - 1; o >= down && (_masks[baseIndex + o] & others) == TBitboard.Zero; o--) yield return o;
    }

    public bool IsGoal(in TState state) => state.OffsetOf(GoalVehicle) == GoalOffset;

    public int GenerateMoves(in TState state, Span<LaneMove> moveBuffer)
    {
        int n = 0;
        ReadOnlySpan<TBitboard> masks = _masks, forward = _forward, backward = _backward;
        ReadOnlySpan<int> maxOffset = _maxOffset;
        TBitboard occupied = TBitboard.Zero;
        for (int v = 0, w = 0; v < _count; w++)
        {
            ulong packed = state.Word(w);
            for (int end = Math.Min(v + 16, _count); v < end; v++, packed >>= 4) occupied |= masks[v * Stride + (int)(packed & 0xF)];
        }
        if (MaxStep == 1)
        {
            // Rush Hour: one cell at a time, so one test of the cells just beyond each end of the vehicle.
            for (int v = 0; v < _count; v++)
            {
                int at = state.OffsetOf(v), i = v * Stride + at;
                if ((forward[i] & occupied) == TBitboard.Zero) moveBuffer[n++] = new LaneMove((byte)v, (byte)(at + 1));
                if ((backward[i] & occupied) == TBitboard.Zero) moveBuffer[n++] = new LaneMove((byte)v, (byte)(at - 1));
            }
            return n;
        }
        for (int v = 0; v < _count; v++)
        {
            // Sliding to an offset needs only the cells newly covered to be free: the vehicle's own cells are not among them.
            int at = state.OffsetOf(v), baseIndex = v * Stride;
            int max = maxOffset[v], up = MaxStep < max - at ? at + MaxStep : max, down = MaxStep < at ? at - MaxStep : 0;
            for (int o = at; o < up && (forward[baseIndex + o] & occupied) == TBitboard.Zero; o++) moveBuffer[n++] = new LaneMove((byte)v, (byte)(o + 1));
            for (int o = at; o > down && (backward[baseIndex + o] & occupied) == TBitboard.Zero; o--) moveBuffer[n++] = new LaneMove((byte)v, (byte)(o - 1));
        }
        return n;
    }


    public TState ApplyMove(in TState state, in LaneMove move) => state.WithOffset(move.Vehicle, move.Offset);

    public ulong GetHashCode64(in TState state) => state.Hash();

    private TBitboard MaskOf(LaneVehicle v, int offset)
    {
        TBitboard mask = TBitboard.Zero;
        for (int along = 0; along < v.Length; along++)
            for (int across = 0; across < v.Breadth; across++)
            {
                int row = v.IsHorizontal ? v.Lane + across : offset + along, col = v.IsHorizontal ? offset + along : v.Lane + across;
                mask |= TBitboard.One << (row * Columns + col);
            }
        return mask;
    }
}

/// <summary>Builders for the lane-sliding puzzles that need no cell register type.</summary>
public static class LaneSlide
{
    /// <summary>
    /// Classic Rush Hour on a diagram of up to 64 cells and 16 vehicles (see <see cref="LaneVehicle.SymbolsOf"/> and <see cref="LaneVehicle.Parse"/>): one cell
    /// per move, and the puzzle is solved when vehicle 0, the red car (a horizontal one), reaches the far end of its row.
    /// </summary>
    public static LaneSlideDomain<LaneState16, ulong> RushHour(IReadOnlyList<string> rows, string symbols)
    {
        var vehicles = LaneVehicle.Parse(rows, symbols);
        if (!vehicles[0].IsHorizontal) throw new ArgumentException("The red car must be horizontal.", nameof(symbols));
        return new LaneSlideDomain<LaneState16, ulong>(vehicles, rows[0].Length, rows.Count, 0, rows[0].Length - vehicles[0].Length, maxStep: 1);
    }
}
