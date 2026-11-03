using System.Numerics;
using System.Runtime.CompilerServices;

namespace E2e4.SlidingBlock.Logic;

/// <summary>How the cells of a board touch.</summary>
public enum GridGeometry : byte
{
    /// <summary>Cardinal deltas: -1, +1, -W, +W.</summary>
    Orthogonal4Way = 0,

    /// <summary>Axial (q, r) row-major deltas: -1, +1, -W, +W, -W + 1, +W - 1.</summary>
    HexagonalAxial = 1,
}

/// <summary>How a group travels: one cell, any distance by flood fill, or gliding until blocked.</summary>
public enum SlidePhysics : byte
{
    /// <summary>A move is one cell.</summary>
    SingleStep = 0,
    /// <summary>A move takes the piece to anywhere it can reach through free cells.</summary>
    ContinuousFloodFill = 1,
    /// <summary>A move glides the piece in one direction until something stops it.</summary>
    FrictionlessRaycast = 2,
}

/// <summary>What a group does to other pieces when it moves; the flags combine.</summary>
[Flags]
public enum RoleFlags : byte
{
    /// <summary>The group does nothing special.</summary>
    None = 0,
    /// <summary>Moving the group pushes pieces in its way.</summary>
    Pusher = 1 << 0,
    /// <summary>Pieces can be pushed by a group with <see cref="Pusher"/>.</summary>
    Pushable = 1 << 1,
    /// <summary>The group carries the pieces inside its hollow shape along with it.</summary>
    HollowCarrier = 1 << 2,
    /// <summary>The group drags touching magnetic pieces along.</summary>
    Magnetic = 1 << 3,
}

/// <summary>A cell that can only be entered from the directions set in <see cref="AllowedEntryDirectionsMask"/> (bit d = direction d).</summary>
public readonly record struct DirectionalDiode(int CellIndex, byte AllowedEntryDirectionsMask);

/// <summary>The engine's direction numbers (bit d of a diode mask, the <see cref="Partition.Direction"/> of a partition).</summary>
public static class GridDirection
{
    /// <summary>One cell towards column 0.</summary>
    public const int Left = 0;
    /// <summary>One cell towards the last column.</summary>
    public const int Right = 1;
    /// <summary>One row towards row 0.</summary>
    public const int Up = 2;
    /// <summary>One row towards the last row.</summary>
    public const int Down = 3;

    /// <summary>Hex grids only: up and to the right (-W + 1).</summary>
    public const int UpRight = 4;

    /// <summary>Hex grids only: down and to the left (+W - 1).</summary>
    public const int DownLeft = 5;
}

/// <summary>
/// An immovable partition on the edge between <paramref name="Cell"/> and its neighbour in <paramref name="Direction"/>: no piece crosses it
/// in either direction, not even in the middle of a longer move. The builder keeps it in one canonical form, <paramref name="Direction"/>
/// being <see cref="GridDirection.Right"/>, <see cref="GridDirection.Down"/> or <see cref="GridDirection.DownLeft"/> (the neighbour has
/// the higher cell index), so equal partitions compare equal however they were declared.
/// </summary>
public readonly record struct Partition(int Cell, int Direction)
{
    /// <summary>The cell on the other side of the partition, on a board <paramref name="width"/> cells wide.</summary>
    public int Neighbour(int width) => Cell + Grid.Delta(width, Direction);
}

/// <summary>
/// A gate that is solid (<paramref name="NormallyClosed"/>) until a piece on <paramref name="TriggerElevationMask"/> covers a trigger cell;
/// with <paramref name="NormallyClosed"/> false it is solid only while the trigger is covered.
/// </summary>
public readonly record struct PressurePlateRule<TBitboard>(TBitboard TriggerCellsMask, TBitboard GateCellsMask,
    byte TriggerElevationMask = 0b01, bool NormallyClosed = true)
    where TBitboard : unmanaged, IBitboard<TBitboard>;

/// <summary>Diagnostic record of one glide, for UI visualization.</summary>
public readonly record struct RaycastTrace<TBitboard>(int Direction, int StartAnchor, int TerminalAnchor, int StepCount,
    TBitboard SweptCellsMask, TBitboard BlockingBitsMask)
    where TBitboard : unmanaged, IBitboard<TBitboard>;

/// <summary>Direction tables shared by the move generators. Directions 0..3 are W, E, N, S; 4 and 5 (hex only) are NE and SW.</summary>
internal static class Grid
{
    public const int MaxDirections = 6;

    public static int DirectionCount(GridGeometry geometry) => geometry == GridGeometry.HexagonalAxial ? 6 : 4;

    public static int Delta(int width, int dir) => dir switch
    {
        0 => -1,
        1 => 1,
        2 => -width,
        3 => width,
        4 => -width + 1,
        _ => width - 1,
    };

    /// <summary>Column change of a step.</summary>
    public static int ColStep(int dir) => dir switch { 0 => -1, 1 => 1, 4 => 1, 5 => -1, _ => 0 };

    /// <summary>Movement axis of a direction: 0 = along the row, 1 = along the column, 2 = the hex diagonal.</summary>
    public static int Axis(int dir) => dir >> 1;

    public static bool IsAllowed(AllowedDirections allowed, int dir) => dir switch
    {
        < 2 => (allowed & AllowedDirections.Horizontal) != 0,
        < 4 => (allowed & AllowedDirections.Vertical) != 0,
        _ => allowed == AllowedDirections.Both,
    };

    /// <summary>True when a one-step move from <paramref name="anchor"/> in <paramref name="dir"/> stays on the board without wrapping a row.</summary>
    public static bool StepStaysOnBoard(int anchor, int dir, int width, int cellCount)
    {
        int next = anchor + Delta(width, dir);
        if ((uint)next >= (uint)cellCount) return false;
        int col = anchor % width + ColStep(dir);
        return (uint)col < (uint)width;
    }

    public static T Shift<T>(T mask, int delta) where T : unmanaged, IBitboard<T> => delta >= 0 ? mask.ShiftLeft(delta) : mask.ShiftRight(-delta);

    public static int BitWidth<T>() where T : unmanaged, IBitboard<T> => Bit<T>.Width;

    public static int Lsb<T>(T mask) where T : unmanaged, IBitboard<T> => mask.TrailingZeroCount();

    /// <summary>True when bit <paramref name="index"/> of <paramref name="mask"/> is set.</summary>
    public static bool Has<T>(T mask, int index) where T : unmanaged, IBitboard<T> => mask.HasBit(index);

    /// <summary>The indices of the set bits of <paramref name="mask"/>, lowest first, without allocating.</summary>
    public static SetBits<T> Bits<T>(T mask) where T : unmanaged, IBitboard<T> => new(mask);

    /// <summary>Mask of the cells that may be entered by a step in <paramref name="dir"/> without wrapping a row.</summary>
    public static T EntryMask<T>(int dir, int width) where T : unmanaged, IBitboard<T>
    {
        int col = ColStep(dir);
        if (col == 0) return Bit<T>.All;
        T col0 = Bit<T>.Zero;
        int bits = BitWidth<T>();
        for (int i = 0; i < bits; i += width) col0 = col0.Or(Bit<T>.One.ShiftLeft(i));
        return col > 0 ? col0.Not() : col0.ShiftLeft(width - 1).Not();
    }
}

/// <summary>Allocation-free <c>foreach (int index in Grid.Bits(mask))</c> over the set bits of a bitboard.</summary>
internal struct SetBits<T> where T : unmanaged, IBitboard<T>
{
    private T _rest;

    public SetBits(T mask) => _rest = mask;

    public int Current => Grid.Lsb(_rest);

    public bool MoveNext()
    {
        // The first call only reports whether there is a bit; later calls clear the one just returned.
        if (_started) _rest = _rest.ClearLowest();
        _started = true;
        return !_rest.IsEmpty;
    }

    private bool _started;

    public readonly SetBits<T> GetEnumerator() => this;
}
