using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

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
    SingleStep = 0,
    ContinuousFloodFill = 1,
    FrictionlessRaycast = 2,
}

[Flags]
public enum RoleFlags : byte
{
    None = 0,
    Pusher = 1 << 0,
    Pushable = 1 << 1,
    HollowCarrier = 1 << 2,
    Magnetic = 1 << 3,
}

/// <summary>A cell that can only be entered from the directions set in <see cref="AllowedEntryDirectionsMask"/> (bit d = direction d).</summary>
public readonly record struct DirectionalDiode(int CellIndex, byte AllowedEntryDirectionsMask);

/// <summary>
/// A gate that is solid (<paramref name="NormallyClosed"/>) until a piece on <paramref name="TriggerElevationMask"/> covers a trigger cell;
/// with <paramref name="NormallyClosed"/> false it is solid only while the trigger is covered.
/// </summary>
public readonly record struct PressurePlateRule<TBitboard>(TBitboard TriggerCellsMask, TBitboard GateCellsMask,
    byte TriggerElevationMask = 0b01, bool NormallyClosed = true)
    where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>;

/// <summary>Diagnostic record of one glide, for UI visualization.</summary>
public readonly record struct RaycastTrace<TBitboard>(int Direction, int StartAnchor, int TerminalAnchor, int StepCount,
    TBitboard SweptCellsMask, TBitboard BlockingBitsMask)
    where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>;

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

    public static T Shift<T>(T mask, int delta) where T : unmanaged, IBinaryInteger<T> => delta >= 0 ? mask << delta : mask >> -delta;

    public static int BitWidth<T>() where T : unmanaged, IBinaryInteger<T> => int.CreateChecked(T.PopCount(T.AllBitsSet));

    public static int Lsb<T>(T mask) where T : unmanaged, IBinaryInteger<T> => int.CreateChecked(T.TrailingZeroCount(mask));

    /// <summary>Mask of the cells that may be entered by a step in <paramref name="dir"/> without wrapping a row.</summary>
    public static T EntryMask<T>(int dir, int width) where T : unmanaged, IBinaryInteger<T>
    {
        int col = ColStep(dir);
        if (col == 0) return T.AllBitsSet;
        T col0 = T.Zero;
        int bits = BitWidth<T>();
        for (int i = 0; i < bits; i += width) col0 |= T.One << i;
        return col > 0 ? ~col0 : ~(col0 << (width - 1));
    }
}
