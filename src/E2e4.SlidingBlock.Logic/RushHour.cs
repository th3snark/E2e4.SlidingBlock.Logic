namespace E2e4.SlidingBlock.Logic;

public sealed record Vehicle(int Index, char Label, int Length, bool IsHorizontal, int Lane, int InitialOffset);

/// <summary>Static data for a 6x6 Rush Hour puzzle: per-vehicle lane geometry and precomputed bitboards.</summary>
public sealed class RushHourTopology
{
    public const int Size = 6;
    public const int MaxVehicles = 16;

    private RushHourTopology(IReadOnlyList<Vehicle> vehicles)
    {
        Vehicles = vehicles;
        int n = vehicles.Count;
        VehicleMasks = new ulong[n, Size];
        FrontNeighborBit = new ulong[n, Size];
        BackNeighborBit = new ulong[n, Size];
        foreach (var v in vehicles)
            for (int off = 0; off <= Size - v.Length; off++)
            {
                ulong m = 0;
                for (int k = 0; k < v.Length; k++) m |= Bit(v, off + k);
                VehicleMasks[v.Index, off] = m;
                FrontNeighborBit[v.Index, off] = off + v.Length < Size ? Bit(v, off + v.Length) : 0;
                BackNeighborBit[v.Index, off] = off > 0 ? Bit(v, off - 1) : 0;
            }
    }

    public IReadOnlyList<Vehicle> Vehicles { get; }
    public int VehicleCount => Vehicles.Count;
    public ulong[,] VehicleMasks { get; }
    /// <summary>Single bit just past the vehicle's front; 0 means the board edge (move impossible).</summary>
    public ulong[,] FrontNeighborBit { get; }
    public ulong[,] BackNeighborBit { get; }

    private static ulong Bit(Vehicle v, int along) => 1UL << (v.IsHorizontal ? v.Lane * Size + along : along * Size + v.Lane);

    /// <summary>Index of the vehicle drawn with <paramref name="label"/>.</summary>
    public int IndexOf(char label) => Vehicles.First(v => v.Label == label).Index;

    /// <summary>
    /// Parses six 6-character rows; '.' is empty, every other character is one vehicle. Vehicles are indexed in label order, except that
    /// the labels in <paramref name="leadingLabels"/> come first, in that order. Which vehicle must reach which offset is the puzzle's
    /// business (see <see cref="RushHourDomain"/>).
    /// </summary>
    public static RushHourTopology FromAsciiMap(IReadOnlyList<string> rows, string leadingLabels = "")
    {
        var cells = new SortedDictionary<char, List<(int r, int c)>>();
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < Size; c++)
                if (rows[r][c] != '.')
                {
                    if (!cells.TryGetValue(rows[r][c], out var l)) cells[rows[r][c]] = l = [];
                    l.Add((r, c));
                }
        var vehicles = new List<Vehicle>();
        foreach (char ch in cells.Keys.OrderBy(k => { int i = leadingLabels.IndexOf(k, StringComparison.Ordinal); return i < 0 ? int.MaxValue : i; }).ThenBy(k => k))
        {
            var l = cells[ch];
            bool horizontal = l.All(x => x.r == l[0].r);
            int lane = horizontal ? l[0].r : l[0].c;
            int off = horizontal ? l.Min(x => x.c) : l.Min(x => x.r);
            vehicles.Add(new Vehicle(vehicles.Count, ch, l.Count, horizontal, lane, off));
        }
        if (vehicles.Count > MaxVehicles) throw new ArgumentException("Too many vehicles.");
        if (vehicles.Count == 0) throw new ArgumentException("No vehicles.");
        return new RushHourTopology(vehicles);
    }

    public RushHourState InitialState()
    {
        ulong packed = 0;
        foreach (var v in Vehicles) packed |= (ulong)v.InitialOffset << (3 * v.Index);
        return new RushHourState(packed);
    }
}

/// <summary>3 bits per vehicle (offset along its fixed lane) packed into a single ulong.</summary>
public readonly struct RushHourState : IEquatable<RushHourState>
{
    public RushHourState(ulong packedOffsets) => PackedOffsets = packedOffsets;

    public ulong PackedOffsets { get; }

    public int OffsetOf(int vehicle) => (int)((PackedOffsets >> (3 * vehicle)) & 7);

    /// <summary>36-bit occupancy reconstructed in O(N) from the VehicleMasks lookup table.</summary>
    public ulong Occupancy(RushHourTopology topo)
    {
        ulong occ = 0;
        for (int v = 0; v < topo.VehicleCount; v++) occ |= topo.VehicleMasks[v, OffsetOf(v)];
        return occ;
    }

    public RushHourState Shift(int vehicle, int delta) =>
        new(delta > 0 ? PackedOffsets + (1UL << (3 * vehicle)) : PackedOffsets - (1UL << (3 * vehicle)));

    public bool Equals(RushHourState other) => PackedOffsets == other.PackedOffsets;
    public override bool Equals(object? obj) => obj is RushHourState s && Equals(s);
    public static bool operator ==(RushHourState left, RushHourState right) => left.Equals(right);
    public static bool operator !=(RushHourState left, RushHourState right) => !left.Equals(right);
    public override int GetHashCode() => PackedOffsets.GetHashCode();
}

public readonly record struct RushHourMove(byte Vehicle, sbyte Delta);

public sealed class RushHourDomain : ISlidingBlockDomain<RushHourState, RushHourMove>
{
    /// <summary>The puzzle is solved when vehicle <paramref name="goalVehicle"/> reaches <paramref name="goalOffset"/> along its lane.</summary>
    public RushHourDomain(RushHourTopology topology, int goalVehicle, int goalOffset)
    {
        if (goalVehicle < 0 || goalVehicle >= topology.VehicleCount) throw new ArgumentOutOfRangeException(nameof(goalVehicle));
        if (goalOffset < 0 || goalOffset > RushHourTopology.Size - topology.Vehicles[goalVehicle].Length) throw new ArgumentOutOfRangeException(nameof(goalOffset));
        Topology = topology;
        GoalVehicle = goalVehicle;
        GoalOffset = goalOffset;
        InitialState = topology.InitialState();
    }

    public RushHourTopology Topology { get; }
    public int GoalVehicle { get; }
    public int GoalOffset { get; }
    public RushHourState InitialState { get; }
    public int MaxMoves => Topology.VehicleCount * 2;

    public bool IsGoal(in RushHourState state) => state.OffsetOf(GoalVehicle) == GoalOffset;

    public int GenerateMoves(in RushHourState state, Span<RushHourMove> moveBuffer)
    {
        ulong occ = state.Occupancy(Topology);
        int n = 0;
        for (int v = 0; v < Topology.VehicleCount; v++)
        {
            int off = state.OffsetOf(v);
            ulong back = Topology.BackNeighborBit[v, off], front = Topology.FrontNeighborBit[v, off];
            if (back != 0 && (occ & back) == 0) moveBuffer[n++] = new RushHourMove((byte)v, -1);
            if (front != 0 && (occ & front) == 0) moveBuffer[n++] = new RushHourMove((byte)v, 1);
        }
        return n;
    }

    public RushHourState ApplyMove(in RushHourState state, in RushHourMove move) => state.Shift(move.Vehicle, move.Delta);

    public ulong GetHashCode64(in RushHourState state) => state.PackedOffsets * 0x9E3779B97F4A7C15UL;
}
