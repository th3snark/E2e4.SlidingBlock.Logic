using System.Numerics;
using System.Runtime.CompilerServices;

namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// A vehicle of a lane-sliding puzzle: a block of cells that slides only along its length. <paramref name="Length"/> is its extent along the
/// lane and <paramref name="Breadth"/> across it (1 for a car, 2 for a truck); <paramref name="Lane"/> is the row (horizontal) or column
/// (vertical) of its first cell and <paramref name="InitialOffset"/> the column or row of its first cell at the start.
/// <see cref="Text.LaneDiagram"/> reads vehicles off a diagram.
/// </summary>
/// <param name="Index">The vehicle number: 0, 1, 2 ... in list order.</param>
/// <param name="IsHorizontal">True if the vehicle slides along a row.</param>
/// <param name="Length">Extent along the lane, in cells.</param>
/// <param name="Breadth">Extent across the lane, in cells.</param>
/// <param name="Lane">The row (horizontal) or column (vertical) of the vehicle's first cell.</param>
/// <param name="InitialOffset">The column (horizontal) or row (vertical) of its first cell at the start.</param>
public sealed record LaneVehicle(int Index, bool IsHorizontal, int Length, int Breadth, int Lane, int InitialOffset);

/// <summary>
/// A position of a lane-sliding puzzle, packed: where every vehicle stands along its lane, four bits each (lanes are at most 16 cells long).
/// The domain is generic over it so that a small puzzle pays only for the vehicles it has.
/// </summary>
public interface ILaneState<TState> : IEquatable<TState> where TState : struct, ILaneState<TState>
{
    /// <summary>How many vehicles the position holds: 16 to a 64-bit word.</summary>
    int Capacity { get; }

    /// <summary>The position of a vehicle along its lane: the column (horizontal) or row (vertical) of its first cell.</summary>
    int OffsetOf(int vehicle);

    /// <summary>The position with <paramref name="vehicle"/> moved to <paramref name="offset"/> along its lane.</summary>
    TState WithOffset(int vehicle, int offset);

    /// <summary>One 64-bit word of the packed offsets: word 0 holds vehicles 0 to 15, word 1 vehicles 16 to 31, and so on.</summary>
    ulong Word(int index);

    /// <summary>A 64-bit hash of the position.</summary>
    ulong Hash();
}

/// <summary>A position of up to 16 vehicles, in one 64-bit word.</summary>
public readonly struct LaneState16 : ILaneState<LaneState16>
{
    private readonly ulong _word;

    private LaneState16(ulong word) => _word = word;

    /// <inheritdoc/>
    public int Capacity => 16;

    /// <inheritdoc/>
    public ulong Word(int index) => _word;
    /// <inheritdoc/>
    public int OffsetOf(int vehicle) => (int)((_word >> (4 * vehicle)) & 0xF);
    /// <inheritdoc/>
    public LaneState16 WithOffset(int vehicle, int offset) => new((_word & ~(0xFUL << (4 * vehicle))) | ((ulong)(offset & 0xF) << (4 * vehicle)));
    /// <inheritdoc/>
    public ulong Hash() => Hashing.Mix(_word);

    /// <inheritdoc/>
    public bool Equals(LaneState16 other) => _word == other._word;
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LaneState16 s && Equals(s);
    /// <inheritdoc/>
    public override int GetHashCode() => _word.GetHashCode();
    /// <summary>True if both positions have every vehicle at the same offset.</summary>
    public static bool operator ==(LaneState16 left, LaneState16 right) => left.Equals(right);
    /// <summary>True if the positions differ.</summary>
    public static bool operator !=(LaneState16 left, LaneState16 right) => !left.Equals(right);
}

/// <summary>A position of up to 64 vehicles, in four 64-bit words.</summary>
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

    /// <inheritdoc/>
    public int Capacity => 64;

    /// <inheritdoc/>
    public ulong Word(int index) => (index & 3) switch { 0 => _w0, 1 => _w1, 2 => _w2, _ => _w3 };
    /// <inheritdoc/>
    public int OffsetOf(int vehicle) => (int)((Word(vehicle >> 4) >> (4 * (vehicle & 15))) & 0xF);

    /// <inheritdoc/>
    public LaneState64 WithOffset(int vehicle, int offset)
    {
        int shift = 4 * (vehicle & 15);
        ulong Replace(ulong word) => (word & ~(0xFUL << shift)) | ((ulong)(offset & 0xF) << shift);
        return (vehicle >> 4) switch
        {
            0 => new LaneState64(Replace(_w0), _w1, _w2, _w3),
            1 => new LaneState64(_w0, Replace(_w1), _w2, _w3),
            2 => new LaneState64(_w0, _w1, Replace(_w2), _w3),
            _ => new LaneState64(_w0, _w1, _w2, Replace(_w3)),
        };
    }

    /// <inheritdoc/>
    public ulong Hash() => Hashing.Mix(_w0, _w1, _w2, _w3);

    /// <inheritdoc/>
    public bool Equals(LaneState64 other) => _w0 == other._w0 && _w1 == other._w1 && _w2 == other._w2 && _w3 == other._w3;
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LaneState64 s && Equals(s);
    /// <inheritdoc/>
    public override int GetHashCode() => Hash().GetHashCode();
    /// <summary>True if both positions have every vehicle at the same offset.</summary>
    public static bool operator ==(LaneState64 left, LaneState64 right) => left.Equals(right);
    /// <summary>True if the positions differ.</summary>
    public static bool operator !=(LaneState64 left, LaneState64 right) => !left.Equals(right);
}

/// <summary>Slide a vehicle along its lane so that its first cell is at <paramref name="Offset"/> (a column or a row).</summary>
public readonly record struct LaneMove(byte Vehicle, byte Offset);

/// <summary>
/// The rules of vehicles that slide forwards or backwards along their length, never sideways or round a corner: Rush Hour, garages, shunting
/// yards. A move takes one vehicle through free cells, up to <c>maxStep</c> cells; <c>1</c> counts one cell at a time and the default
/// counts any distance as one move. The position is the packed lane offsets, four bits each, in <typeparamref name="TState"/>:
/// <see cref="LaneState16"/> for up to 16 vehicles, <see cref="LaneState64"/> for up to 64, far past the polyomino domain's 16 pieces.
/// <typeparamref name="TBitboard"/> is the board's cell register, as for the polyomino domain: <see cref="ulong"/> for boards of up to 64
/// cells, <c>UInt128</c> for up to 128. The puzzle is solved when the goal vehicle reaches the goal offset.
/// </summary>
public sealed class LaneSlideDomain<TState, TBitboard> : ISlidingBlockDomain<TState, LaneMove>
    where TState : struct, ILaneState<TState>
    where TBitboard : unmanaged, IBitboard<TBitboard>
{
    private const int Stride = 16;   // offsets per vehicle in the mask table: lanes are at most 16 cells
    // The cells covered at an offset, and the cells that sliding one cell forward / backward from an offset newly covers (every cell, so never free, at the end of the lane).
    private readonly TBitboard[] _masks, _forward, _backward;
    private readonly int _count;
    private readonly int[] _maxOffset;

    /// <summary>
    /// Creates the rules.
    /// </summary>
    /// <param name="vehicles">The vehicles, numbered 0, 1, 2 ... in list order. The list is copied.</param>
    /// <param name="columns">Columns of the board.</param>
    /// <param name="rows">Rows of the board.</param>
    /// <param name="goalVehicle">The vehicle that has to reach <paramref name="goalOffset"/>.</param>
    /// <param name="goalOffset">The offset along its lane that solves the puzzle for the goal vehicle.</param>
    /// <param name="maxStep">The most cells a vehicle slides in one move.</param>
    /// <exception cref="ArgumentNullException"><paramref name="vehicles"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="columns"/>, <paramref name="rows"/> or <paramref name="maxStep"/> is below 1, or the goal vehicle or offset does not exist.</exception>
    /// <exception cref="ArgumentException">A vehicle is malformed, outside the board, in the way of another at the start, or there are too many.</exception>
    public LaneSlideDomain(IReadOnlyList<LaneVehicle> vehicles, int columns, int rows, int goalVehicle, int goalOffset, int maxStep = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        if (vehicles.Count == 0) throw new ArgumentException("There must be at least one vehicle.", nameof(vehicles));
        int capacity = default(TState).Capacity;
        if (vehicles.Count > capacity) throw new ArgumentException($"At most {capacity} vehicles.", nameof(vehicles));
        if (columns * rows > Grid.BitWidth<TBitboard>()) throw new ArgumentException($"The board has at most {Grid.BitWidth<TBitboard>()} cells.");
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStep, 1);
        if (goalVehicle < 0 || goalVehicle >= vehicles.Count) throw new ArgumentOutOfRangeException(nameof(goalVehicle));
        vehicles = [.. vehicles];
        Vehicles = vehicles;
        Columns = columns;
        Rows = rows;
        GoalVehicle = goalVehicle;
        GoalOffset = goalOffset;
        MaxStep = maxStep;
        _maxOffset = [.. vehicles.Select(v => (v.IsHorizontal ? columns : rows) - v.Length)];
        if (_maxOffset.Any(m => m < 0 || m > 15)) throw new ArgumentException("A vehicle does not fit its lane, or the lane is longer than 16 cells.", nameof(vehicles));
        if (goalOffset < 0 || goalOffset > _maxOffset[goalVehicle]) throw new ArgumentOutOfRangeException(nameof(goalOffset));
        for (int i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            if (v.Index != i) throw new ArgumentException($"Vehicle {i} has index {v.Index}: the indexes must be 0, 1, 2, ... in list order.", nameof(vehicles));
            if (v.Length < 1 || v.Breadth < 1) throw new ArgumentException($"Vehicle {i} has no area.", nameof(vehicles));
            if (v.Lane < 0 || v.Lane + v.Breadth > (v.IsHorizontal ? rows : columns)) throw new ArgumentException($"Vehicle {i} is outside the board across its lane.", nameof(vehicles));
            if (v.InitialOffset < 0 || v.InitialOffset > _maxOffset[i]) throw new ArgumentException($"Vehicle {i} starts outside its lane.", nameof(vehicles));
        }
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
                _forward[i] = o < _maxOffset[v.Index] ? _masks[i + 1].AndNot(_masks[i]) : Bit<TBitboard>.All;
                _backward[i] = o > 0 ? _masks[i - 1].AndNot(_masks[i]) : Bit<TBitboard>.All;
            }
        var state = default(TState);
        var covered = Bit<TBitboard>.Zero;
        foreach (var v in vehicles)
        {
            state = state.WithOffset(v.Index, v.InitialOffset);
            var mask = _masks[v.Index * Stride + v.InitialOffset];
            if (covered.Intersects(mask)) throw new ArgumentException($"Vehicle {v.Index} overlaps another at the start.", nameof(vehicles));
            covered = covered.Or(mask);
        }
        InitialState = state;
        MaxMoves = vehicles.Sum(v => 2 * Math.Min(_maxOffset[v.Index], maxStep));
    }

    /// <summary>The vehicles, indexed by vehicle number.</summary>
    public IReadOnlyList<LaneVehicle> Vehicles { get; }
    /// <summary>Columns of the board.</summary>
    public int Columns { get; }
    /// <summary>Rows of the board.</summary>
    public int Rows { get; }
    /// <summary>The vehicle that has to reach <see cref="GoalOffset"/>.</summary>
    public int GoalVehicle { get; }
    /// <summary>The offset along its lane at which the goal vehicle solves the puzzle.</summary>
    public int GoalOffset { get; }

    /// <summary>The most cells a vehicle slides in one move.</summary>
    public int MaxStep { get; }

    /// <inheritdoc/>
    public TState InitialState { get; }
    /// <inheritdoc/>
    public int MaxMoves { get; }

    /// <summary>The last offset a vehicle can have along its lane.</summary>
    public int MaxOffsetOf(int vehicle) => _maxOffset[vehicle];

    /// <summary>The cells a vehicle covers at an offset along its lane, as a bitboard.</summary>
    public TBitboard MaskAt(int vehicle, int offset) => _masks[vehicle * Stride + offset];

    /// <summary>The cells every vehicle covers in a position.</summary>
    public TBitboard Occupancy(in TState state)
    {
        TBitboard occupied = Bit<TBitboard>.Zero;
        ReadOnlySpan<TBitboard> masks = _masks;
        for (int v = 0; v < _count; v++) occupied = occupied.Or(masks[v * Stride + state.OffsetOf(v)]);
        return occupied;
    }

    /// <summary>The offsets a vehicle can slide to in one move from where it stands (not counting where it stands), nearest first on each side.</summary>
    public IEnumerable<int> ReachableOffsets(TState state, int vehicle)
    {
        int at = state.OffsetOf(vehicle), baseIndex = vehicle * Stride;
        var others = Occupancy(state).AndNot(_masks[baseIndex + at]);
        int max = _maxOffset[vehicle], up = MaxStep < max - at ? at + MaxStep : max, down = MaxStep < at ? at - MaxStep : 0;
        for (int o = at + 1; o <= up && !_masks[baseIndex + o].Intersects(others); o++) yield return o;
        for (int o = at - 1; o >= down && !_masks[baseIndex + o].Intersects(others); o--) yield return o;
    }

    /// <inheritdoc/>
    public bool IsGoal(in TState state) => state.OffsetOf(GoalVehicle) == GoalOffset;

    /// <inheritdoc/>
    public int GenerateMoves(in TState state, Span<LaneMove> moveBuffer)
    {
        int n = 0;
        ReadOnlySpan<TBitboard> masks = _masks, forward = _forward, backward = _backward;
        ReadOnlySpan<int> maxOffset = _maxOffset;
        TBitboard occupied = Bit<TBitboard>.Zero;
        for (int v = 0, w = 0; v < _count; w++)
        {
            ulong packed = state.Word(w);
            for (int end = Math.Min(v + 16, _count); v < end; v++, packed >>= 4) occupied = occupied.Or(masks[v * Stride + (int)(packed & 0xF)]);
        }
        if (MaxStep == 1)
        {
            // Rush Hour: one cell at a time, so one test of the cells just beyond each end of the vehicle.
            for (int v = 0; v < _count; v++)
            {
                int at = state.OffsetOf(v), i = v * Stride + at;
                if (!forward[i].Intersects(occupied)) moveBuffer[n++] = new LaneMove((byte)v, (byte)(at + 1));
                if (!backward[i].Intersects(occupied)) moveBuffer[n++] = new LaneMove((byte)v, (byte)(at - 1));
            }
            return n;
        }
        for (int v = 0; v < _count; v++)
        {
            // Sliding to an offset needs only the cells newly covered to be free: the vehicle's own cells are not among them.
            int at = state.OffsetOf(v), baseIndex = v * Stride;
            int max = maxOffset[v], up = MaxStep < max - at ? at + MaxStep : max, down = MaxStep < at ? at - MaxStep : 0;
            for (int o = at; o < up && !forward[baseIndex + o].Intersects(occupied); o++) moveBuffer[n++] = new LaneMove((byte)v, (byte)(o + 1));
            for (int o = at; o > down && !backward[baseIndex + o].Intersects(occupied); o--) moveBuffer[n++] = new LaneMove((byte)v, (byte)(o - 1));
        }
        return n;
    }

    /// <inheritdoc/>
    public TState ApplyMove(in TState state, in LaneMove move) => state.WithOffset(move.Vehicle, move.Offset);

    /// <inheritdoc/>
    public ulong GetHashCode64(in TState state) => state.Hash();

    private TBitboard MaskOf(LaneVehicle v, int offset)
    {
        TBitboard mask = Bit<TBitboard>.Zero;
        for (int along = 0; along < v.Length; along++)
            for (int across = 0; across < v.Breadth; across++)
            {
                int row = v.IsHorizontal ? v.Lane + across : offset + along, col = v.IsHorizontal ? offset + along : v.Lane + across;
                mask = mask.Or(Bit<TBitboard>.One.ShiftLeft(row * Columns + col));
            }
        return mask;
    }
}
