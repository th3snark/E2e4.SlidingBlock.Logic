using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

/// <summary>4x4 puzzle packed as 16 nibbles; nibble i is the tile on square i (0 = blank).</summary>
public readonly struct NPuzzleState : IEquatable<NPuzzleState>
{
    public const int Size = 4;
    private const ulong Ones = 0x1111111111111111UL;
    private const ulong Highs = 0x8888888888888888UL;

    public NPuzzleState(ulong packedNibbles) => PackedNibbles = packedNibbles;

    public ulong PackedNibbles { get; }

    public int TileAt(int square) => (int)((PackedNibbles >> (square << 2)) & 0xF);

    /// <summary>Square of the blank: lowest zero nibble via the classic has-zero-nibble trick + tzcnt.</summary>
    public int BlankIndex => BitOperations.TrailingZeroCount((PackedNibbles - Ones) & ~PackedNibbles & Highs) >> 2;

    /// <summary>Branchless swap of the blank with the tile on <paramref name="tileSquare"/>.</summary>
    public NPuzzleState SlideTileIntoBlank(int tileSquare)
    {
        ulong tile = (PackedNibbles >> (tileSquare << 2)) & 0xF;
        int blank = BlankIndex;
        return new NPuzzleState(PackedNibbles ^ (tile << (blank << 2)) ^ (tile << (tileSquare << 2)));
    }

    /// <summary>XOR mask applied by <see cref="SlideTileIntoBlank"/>.</summary>
    public ulong SlideMask(int tileSquare)
    {
        ulong tile = (PackedNibbles >> (tileSquare << 2)) & 0xF;
        return (tile << (BlankIndex << 2)) ^ (tile << (tileSquare << 2));
    }

    /// <summary>Inverse map: nibble t holds the square of tile t (O(1) PDB lookups).</summary>
    public ulong InverseMap
    {
        get
        {
            ulong inv = 0;
            for (int s = 0; s < 16; s++) inv |= (ulong)s << (TileAt(s) << 2);
            return inv;
        }
    }

    public bool Equals(NPuzzleState other) => PackedNibbles == other.PackedNibbles;
    public override bool Equals(object? obj) => obj is NPuzzleState s && Equals(s);
    public static bool operator ==(NPuzzleState left, NPuzzleState right) => left.Equals(right);
    public static bool operator !=(NPuzzleState left, NPuzzleState right) => !left.Equals(right);
    public override int GetHashCode() => PackedNibbles.GetHashCode();
    public override string ToString() => $"0x{PackedNibbles:X16}";
}

/// <summary>A tile on <see cref="From"/> slides into the blank on <see cref="To"/>.</summary>
public readonly record struct NPuzzleMove(byte From, byte To);

/// <summary>
/// Move rules for the 4x4 sliding-tile board. The goal position is supplied by the puzzle that uses the domain (the engine does not
/// know where the blank or any tile belongs); <see cref="ManhattanDistance"/> is measured against that goal.
/// </summary>
public sealed class NPuzzleDomain : ISlidingBlockDomain<NPuzzleState, NPuzzleMove>
{
    private readonly byte[] _goalRow = new byte[16], _goalCol = new byte[16];

    public NPuzzleDomain(NPuzzleState initial, NPuzzleState goal)
    {
        InitialState = initial;
        Goal = goal;
        ulong inverse = goal.InverseMap;
        for (int tile = 0; tile < 16; tile++)
        {
            int square = (int)((inverse >> (tile << 2)) & 0xF);
            _goalRow[tile] = (byte)(square >> 2);
            _goalCol[tile] = (byte)(square & 3);
        }
    }

    public NPuzzleState InitialState { get; }
    public NPuzzleState Goal { get; }
    public int MaxMoves => 4;

    public bool IsGoal(in NPuzzleState state) => state.PackedNibbles == Goal.PackedNibbles;

    /// <summary>Sum over tiles of the row and column distance to the tile's square in <see cref="Goal"/> (the blank is ignored). Admissible.</summary>
    public int ManhattanDistance(NPuzzleState state)
    {
        int sum = 0;
        for (int s = 0; s < 16; s++)
        {
            int t = state.TileAt(s);
            if (t == 0) continue;
            sum += Math.Abs((s >> 2) - _goalRow[t]) + Math.Abs((s & 3) - _goalCol[t]);
        }
        return sum;
    }

    public int GenerateMoves(in NPuzzleState state, Span<NPuzzleMove> moveBuffer)
    {
        int b = state.BlankIndex, r = b >> 2, c = b & 3, n = 0;
        if (r > 0) moveBuffer[n++] = new NPuzzleMove((byte)(b - 4), (byte)b);
        if (r < 3) moveBuffer[n++] = new NPuzzleMove((byte)(b + 4), (byte)b);
        if (c > 0) moveBuffer[n++] = new NPuzzleMove((byte)(b - 1), (byte)b);
        if (c < 3) moveBuffer[n++] = new NPuzzleMove((byte)(b + 1), (byte)b);
        return n;
    }

    public NPuzzleState ApplyMove(in NPuzzleState state, in NPuzzleMove move) => state.SlideTileIntoBlank(move.From);

    public ulong GetHashCode64(in NPuzzleState state) => state.PackedNibbles * 0x9E3779B97F4A7C15UL;
}
