using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

/// <summary>4x4 puzzle packed as 16 nibbles; nibble i is the tile on square i (0 = blank).</summary>
public readonly struct NPuzzle4x4State : IEquatable<NPuzzle4x4State>
{
    /// <summary>The board is <see cref="Size"/> x <see cref="Size"/>.</summary>
    public const int Size = 4;
    private const ulong Ones = 0x1111111111111111UL;
    private const ulong Highs = 0x8888888888888888UL;

    /// <summary>Wraps a packed board without checking it; see <see cref="IsPermutation"/> (the domain checks the positions it is given).</summary>
    public NPuzzle4x4State(ulong packedNibbles) => PackedNibbles = packedNibbles;

    /// <summary>The sixteen nibbles, square 0 in the lowest one.</summary>
    public ulong PackedNibbles { get; }

    /// <summary>True if every tile 0 to 15 appears exactly once, i.e. this is a real position.</summary>
    public bool IsPermutation
    {
        get
        {
            ushort seen = 0;
            for (int s = 0; s < 16; s++) seen |= (ushort)(1 << TileAt(s));
            return seen == ushort.MaxValue;
        }
    }

    /// <summary>The tile on <paramref name="square"/> (0 is the blank).</summary>
    public int TileAt(int square) => (int)((PackedNibbles >> (square << 2)) & 0xF);

    /// <summary>Square of the blank: lowest zero nibble via the classic has-zero-nibble trick + tzcnt.</summary>
    public int BlankIndex => BitOps.TrailingZeroCount((PackedNibbles - Ones) & ~PackedNibbles & Highs) >> 2;

    /// <summary>Branchless swap of the blank with the tile on <paramref name="tileSquare"/>.</summary>
    public NPuzzle4x4State SlideTileIntoBlank(int tileSquare)
    {
        ulong tile = (PackedNibbles >> (tileSquare << 2)) & 0xF;
        int blank = BlankIndex;
        return new NPuzzle4x4State(PackedNibbles ^ (tile << (blank << 2)) ^ (tile << (tileSquare << 2)));
    }

    /// <summary>XOR mask applied by <see cref="SlideTileIntoBlank"/>.</summary>
    internal ulong SlideMask(int tileSquare)
    {
        ulong tile = (PackedNibbles >> (tileSquare << 2)) & 0xF;
        return (tile << (BlankIndex << 2)) ^ (tile << (tileSquare << 2));
    }

    /// <summary>The square <paramref name="tile"/> stands on, or -1 if it is not on the board. Use <see cref="InverseMap"/> for repeated lookups.</summary>
    public int SquareOf(int tile)
    {
        for (int s = 0; s < 16; s++)
            if (TileAt(s) == tile) return s;
        return -1;
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

    /// <inheritdoc/>
    public bool Equals(NPuzzle4x4State other) => PackedNibbles == other.PackedNibbles;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NPuzzle4x4State s && Equals(s);

    /// <summary>True if both boards hold the same tiles on the same squares.</summary>
    public static bool operator ==(NPuzzle4x4State left, NPuzzle4x4State right) => left.Equals(right);

    /// <summary>True if the boards differ.</summary>
    public static bool operator !=(NPuzzle4x4State left, NPuzzle4x4State right) => !left.Equals(right);

    /// <inheritdoc/>
    public override int GetHashCode() => PackedNibbles.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => $"0x{PackedNibbles:X16}";
}

/// <summary>A tile on <see cref="From"/> slides into the blank on <see cref="To"/>.</summary>
/// <param name="From">Square the tile leaves.</param>
/// <param name="To">Square of the blank the tile enters.</param>
public readonly record struct NPuzzle4x4Move(byte From, byte To);

/// <summary>
/// Move rules for the 4x4 sliding-tile board. The goal position is supplied by the puzzle that uses the domain (the engine does not
/// know where the blank or any tile belongs); <see cref="ManhattanDistance"/> is measured against that goal.
/// </summary>
public sealed class NPuzzle4x4Domain : ISlidingBlockDomain<NPuzzle4x4State, NPuzzle4x4Move>
{
    // [tile * 16 + square]: Manhattan distance from the square to the tile's goal square; the blank costs nothing.
    private readonly byte[] _distance = new byte[16 * 16];

    /// <summary>Creates the rules for a board that starts at <paramref name="initial"/> and is solved at <paramref name="goal"/>.</summary>
    /// <exception cref="ArgumentException">Either board does not hold each tile 0 to 15 exactly once.</exception>
    public NPuzzle4x4Domain(NPuzzle4x4State initial, NPuzzle4x4State goal)
    {
        if (!initial.IsPermutation) throw new ArgumentException("The initial board must hold each tile 0 to 15 exactly once.", nameof(initial));
        if (!goal.IsPermutation) throw new ArgumentException("The goal board must hold each tile 0 to 15 exactly once.", nameof(goal));
        InitialState = initial;
        Goal = goal;
        ulong inverse = goal.InverseMap;
        for (int tile = 0; tile < 16; tile++)
        {
            int square = (int)((inverse >> (tile << 2)) & 0xF);
            if (tile == 0) continue;
            for (int s = 0; s < 16; s++) _distance[tile * 16 + s] = (byte)(Math.Abs((s >> 2) - (square >> 2)) + Math.Abs((s & 3) - (square & 3)));
        }
    }

    /// <inheritdoc/>
    public NPuzzle4x4State InitialState { get; }

    /// <summary>The position that counts as solved.</summary>
    public NPuzzle4x4State Goal { get; }

    /// <inheritdoc/>
    public int MaxMoves => 4;

    /// <summary>
    /// True if <paramref name="state"/> can reach <see cref="Goal"/>. Each move is a transposition that also moves the blank one square, so the
    /// parity of the tile permutation has to equal the parity of the blank's Manhattan distance to its goal square.
    /// </summary>
    public bool IsSolvable(NPuzzle4x4State state)
    {
        if (!state.IsPermutation) return false;
        ulong goalSquares = Goal.InverseMap;
        Span<int> target = stackalloc int[16];
        for (int s = 0; s < 16; s++) target[s] = (int)((goalSquares >> (state.TileAt(s) << 2)) & 0xF);
        int cycles = 0;
        ushort visited = 0;
        for (int s = 0; s < 16; s++)
        {
            if ((visited & (1 << s)) != 0) continue;
            cycles++;
            for (int t = s; (visited & (1 << t)) == 0; t = target[t]) visited |= (ushort)(1 << t);
        }
        int blank = state.BlankIndex, goalBlank = Goal.BlankIndex;
        int blankDistance = Math.Abs((blank >> 2) - (goalBlank >> 2)) + Math.Abs((blank & 3) - (goalBlank & 3));
        return ((16 - cycles) & 1) == (blankDistance & 1);
    }

    /// <inheritdoc/>
    public bool IsGoal(in NPuzzle4x4State state) => state.PackedNibbles == Goal.PackedNibbles;

    /// <summary>Sum over tiles of the row and column distance to the tile's square in <see cref="Goal"/> (the blank is ignored). Admissible.</summary>
    public int ManhattanDistance(NPuzzle4x4State state)
    {
        int sum = 0;
        ulong packed = state.PackedNibbles;
        for (int s = 0; s < 16; s++, packed >>= 4) sum += _distance[(int)(packed & 0xF) * 16 + s];
        return sum;
    }

    /// <inheritdoc/>
    public int GenerateMoves(in NPuzzle4x4State state, Span<NPuzzle4x4Move> moveBuffer)
    {
        int b = state.BlankIndex, r = b >> 2, c = b & 3, n = 0;
        if (r > 0) moveBuffer[n++] = new NPuzzle4x4Move((byte)(b - 4), (byte)b);
        if (r < 3) moveBuffer[n++] = new NPuzzle4x4Move((byte)(b + 4), (byte)b);
        if (c > 0) moveBuffer[n++] = new NPuzzle4x4Move((byte)(b - 1), (byte)b);
        if (c < 3) moveBuffer[n++] = new NPuzzle4x4Move((byte)(b + 1), (byte)b);
        return n;
    }

    /// <inheritdoc/>
    public NPuzzle4x4State ApplyMove(in NPuzzle4x4State state, in NPuzzle4x4Move move) => state.SlideTileIntoBlank(move.From);

    /// <inheritdoc/>
    public ulong GetHashCode64(in NPuzzle4x4State state) => Hashing.Mix(state.PackedNibbles);
}
