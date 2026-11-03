namespace E2e4.SlidingBlock.Logic;

/// <summary>The constants of a bitboard type, read once from its instance members.</summary>
internal static class Bit<T> where T : unmanaged, IBitboard<T>
{
    public static T Zero => default;
    public static readonly T One = default(T).One;
    public static readonly T All = default(T).All;
    public static readonly int Width = default(T).Width;
}
