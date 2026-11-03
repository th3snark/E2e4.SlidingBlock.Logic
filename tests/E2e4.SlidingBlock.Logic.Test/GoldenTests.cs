
namespace E2e4.SlidingBlock.Logic.Test;

/// <summary>
/// Pins the observable behaviour of every domain and solver: the order of generated moves, the positions they lead to and what the
/// solvers return. A refactor of the engine must leave all of these values unchanged (the Labs apply moves by buffer index and scramble
/// from a fixed seed, so even the order matters).
/// </summary>
public class GoldenTests
{
    private const int StateCap = 1500;

    /// <summary>Breadth-first walk of up to <see cref="StateCap"/> positions, folding the moves of each position in generation order.</summary>
    internal static ulong Fingerprint<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain) where TState : struct, IEquatable<TState>
    {
        var seen = new HashSet<TState> { domain.InitialState };
        var queue = new Queue<TState>();
        queue.Enqueue(domain.InitialState);
        var buffer = new TMove[domain.MaxMoves];
        ulong h = 1469598103934665603UL;
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            int n = domain.GenerateMoves(s, buffer);
            h = (h ^ (ulong)n) * 1099511628211UL;
            h = (h ^ (domain.IsGoal(s) ? 1UL : 0UL)) * 1099511628211UL;
            for (int i = 0; i < n; i++)
            {
                var next = domain.ApplyMove(s, buffer[i]);
                h = (h ^ domain.GetHashCode64(next)) * 1099511628211UL;
                if (seen.Count < StateCap && seen.Add(next)) queue.Enqueue(next);
            }
        }
        return h ^ (ulong)seen.Count;
    }

    internal static PolyominoDomain<Bitboard64> FeatureDomain()
    {
        var b = PuzzleTopology<Bitboard64>.CreateBuilder(5, 5);
        b.AddWall(2, 2);
        b.AddDiode(1, 3, GridDirections.Right);
        b.AddPressurePlate(1UL << 20, 1UL << 7);
        int dom = b.AddGroup("domino", [(0, 0), (0, 1)]);
        int horiz = b.AddGroup("horizontal", [(0, 0)], AllowedDirections.Horizontal);
        int high = b.AddGroup("high", [(0, 0)], elevationMask: 0b10);
        int tall = b.AddGroup("tall", [(0, 0), (1, 0)], elevationMask: 0b11);
        b.AddPiece(dom, "d", 0);
        b.AddPiece(horiz, "h", 4);
        b.AddPiece(high, "u", 13);
        b.AddPiece(tall, "t", 15);
        b.AddPiece(horiz, "g", 24);
        return new PolyominoDomain<Bitboard64>(b.Build(), dom, 18);
    }

    internal static PolyominoDomain<Bitboard128> WideDomain()
    {
        var b = PuzzleTopology<Bitboard128>.CreateBuilder(10, 10);
        b.AddWall(5, 5);
        int dom = b.AddGroup("domino", [(0, 0), (0, 1)]);
        int one = b.AddGroup("one", [(0, 0)]);
        b.AddPiece(dom, "d", 95);
        b.AddPiece(one, "a", 0);
        b.AddPiece(one, "b", 44);
        return new PolyominoDomain<Bitboard128>(b.Build(), dom, 0);
    }

    internal static PolyominoDomain<Bitboard64> HexDomain()
    {
        var b = PuzzleTopology<Bitboard64>.CreateBuilder(4, 4, GridGeometry.HexagonalAxial);
        b.AddWall(1, 1);
        int one = b.AddGroup("one", [(0, 0)]);
        int dom = b.AddGroup("domino", [(0, 0), (0, 1)]);
        b.AddPiece(one, "a", 0);
        b.AddPiece(one, "b", 15);
        b.AddPiece(dom, "d", 9);
        return new PolyominoDomain<Bitboard64>(b.Build(), dom, 0);
    }

    public static TheoryData<string, ulong> Expected => new()
    {
        { "npuzzle", 0xA454058DA3E90453UL },
        { "klotski", 0x358D8F596A343E3BUL },
        { "rushhour", 0xD9E7EAC7F9423106UL },
        { "lab2", 0x824041C2194168BAUL },
        { "lab3-rotation", 0xD3200486DDBA8341UL },
        { "lab3-carrier", 0x185FA007B51CEFEDUL },
        { "lab3-ice", 0x3B5C0458E46BB3D7UL },
        { "feature", 0x439792F8A66290BCUL },
        { "wide", 0xDD6BCDFED3957A35UL },
        { "hex", 0xB5D8005F5BFF2C2FUL },
        { "yard", 0xE52449608DDA4C12UL },
    };

    private static ulong Compute(string name) => name switch
    {
        "npuzzle" => Fingerprint(Fixtures.CreatePuzzle15()),
        "klotski" => Fingerprint(Fixtures.CreateKlotski()),
        "rushhour" => Fingerprint(Fixtures.CreateRushHour()),
        "lab2" => Fingerprint(Fixtures.CreateLab2()),
        "lab3-rotation" => Fingerprint(Fixtures.CreateRotation().Domain),
        "lab3-carrier" => Fingerprint(Fixtures.CreateCarrier().Domain),
        "lab3-ice" => Fingerprint(Fixtures.CreateIce().Domain),
        "feature" => Fingerprint(FeatureDomain()),
        "wide" => Fingerprint(WideDomain()),
        "hex" => Fingerprint(HexDomain()),
        "yard" => Fingerprint(Fixtures.CreateYard()),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void DomainFingerprintIsUnchanged(string name, ulong expected) => Assert.Equal($"{expected:X16}", $"{Compute(name):X16}");


    /// <summary>Reachable sets, straight/hook split, parent maps and animated paths from several positions of the Lab 2 and feature boards.</summary>
    private static ulong PathFingerprint(PolyominoDomain<Bitboard64> domain)
    {
        var buffer = new PolyominoMove[domain.MaxMoves];
        var state = domain.InitialState;
        var topo = domain.Topology;
        Span<sbyte> parent = stackalloc sbyte[64];
        ulong h = 1469598103934665603UL;
        for (int step = 0; step < 12; step++)
        {
            int n = domain.GenerateMoves(state, buffer);
            for (int i = 0; i < n; i++)
                foreach (int cell in domain.PathFor(state, buffer[i].PieceId, buffer[i].TargetAnchor)) h = (h ^ (ulong)cell) * 1099511628211UL;
            foreach (var p in topo.Pieces)
            {
                var g = topo.Groups[state.GroupOf(p.PieceId)];
                int a = state.AnchorOf(p.PieceId);
                Bitboard64 other = state.OccupancyMask ^ (g.BaseMask << a);
                Bitboard64 reach = PolyominoFloodFill.FindReachableAnchors<Bitboard64>(a, g.BaseMask, g.ValidAnchorsMask, other, topo.Columns, parent, out Bitboard64 straight, out Bitboard64 hook, g.Directions);
                h = (h ^ reach.Value ^ (straight << 1).Value ^ (hook << 2).Value) * 1099511628211UL;
                for (int c = 0; c < topo.CellCount; c++) h = (h ^ (ulong)(byte)parent[c]) * 1099511628211UL;
            }
            state = domain.ApplyMove(state, buffer[step % n]);
        }
        return h;
    }

    [Fact]
    public void Lab2PathsAreUnchanged() => Assert.Equal("1C1435462F78A74C", $"{PathFingerprint(Fixtures.CreateLab2()):X16}");

    [Fact]
    public void FeaturePathsAreUnchanged() => Assert.Equal("8B6A539FD0C8B8CD", $"{PathFingerprint(FeatureDomain()):X16}");

    // ---- solvers ---------------------------------------------------------------------------------------------

    private static ulong Fold<TMove>(SolveResult<TMove> r) where TMove : notnull
    {
        if (!r.IsSolved) return 0;
        ulong h = 1469598103934665603UL;
        foreach (var m in r.Moves) h = (h ^ (ulong)m.GetHashCode()) * 1099511628211UL;
        return h ^ (ulong)r.NodesExpanded << 20 ^ (ulong)r.Moves.Count;
    }

    private static NPuzzle4x4State Scrambled()
    {
        var d = Fixtures.CreatePuzzle15();
        return PuzzleWalk.RandomWalk(d, d.Goal, 40, new Random(15));
    }

    public static TheoryData<string, ulong> ExpectedSolves => new()
    {
        { "bfs-klotski", 0x5DCDB2DC04679C05UL },
        { "bfs-rushhour", 0xA2594C7BFF0C288FUL },
        { "bfs-lab2", 0xD63223CDB8327D2FUL },
        { "astar-npuzzle", 0xF00C854DAC1F1E27UL },
        { "ida-npuzzle", 0x43F69ED55F58B8D5UL },
        { "dijkstra-yard", 0x80843E4D7B204AC8UL },
    };

    private static ulong ComputeSolve(string name)
    {
        var solver = new PuzzleSolver();
        var p15 = Fixtures.CreatePuzzle15();
        switch (name)
        {
            case "bfs-klotski": { var d = Fixtures.CreateKlotski(); return Fold(solver.SolveBfs(d, d.InitialState)); }
            case "bfs-rushhour": { var d = Fixtures.CreateRushHour(); return Fold(solver.SolveBfs(d, d.InitialState)); }
            case "bfs-lab2": { var d = Fixtures.CreateLab2(); return Fold(solver.SolveBfs(d, d.InitialState)); }
            case "astar-npuzzle": return Fold(solver.SolveAStar(p15, Scrambled(), p15.ManhattanDistance));
            case "ida-npuzzle": return Fold(solver.SolveIdaStar(p15, Scrambled(), p15.ManhattanDistance));
            case "dijkstra-yard": { var d = Fixtures.CreateYard(); return Fold(solver.SolveDijkstra(d, d.InitialState)); }
            default: throw new ArgumentOutOfRangeException(nameof(name));
        }
    }

    [Theory]
    [MemberData(nameof(ExpectedSolves))]
    public void SolverResultIsUnchanged(string name, ulong expected) => Assert.Equal($"{expected:X16}", $"{ComputeSolve(name):X16}");

    [Fact]
    public void SeededScrambleIsUnchanged() => Assert.Equal("0xEC84FA1023DB9675", Scrambled().ToString());

    [Fact]
    public void ComputeZobristMatchesIncrementalHash()
    {
        var d = FeatureDomain();
        var buffer = new PolyominoMove[d.MaxMoves];
        var state = d.InitialState;
        for (int step = 0; step < 30; step++)
        {
            Assert.Equal(state.Hash, state.ComputeZobrist(d.Topology));
            int n = d.GenerateMoves(state, buffer);
            state = d.ApplyMove(state, buffer[(step * 7) % n]);
        }
    }

    [Fact]
    public void PieceAtAndFirstFreeCellReadThePosition()
    {
        var d = FeatureDomain();
        var s = d.InitialState;
        var t = d.Topology;
        Assert.Equal(0, s.PieceAt(t, 1));       // second cell of the domino anchored on 0
        Assert.Equal(3, s.PieceAt(t, 20));      // lower cell of the tall piece anchored on 15
        Assert.Equal(-1, s.PieceAt(t, 12));     // the wall
        Assert.Equal(2, s.FirstFreeCell(t));
    }

    [Fact]
    public void GoalIsSharedByBothDomains()
    {
        var d = FeatureDomain();
        var composite = new CompositeDomain<Bitboard64>(d);
        Assert.Same(d.Goal, composite.Goal);
        var built = new CompositeDomain<Bitboard64>(d.Topology, d.Goal);
        Assert.Equal(composite.InitialState, built.InitialState);
    }

    [Fact]
    public void BitboardExtensionsServeEveryWidth()
    {
        var narrow = new Bitboard64(0b1010UL << 8);
        var wide = new Bitboard128(1UL << 3, 1UL << 36);
        Assert.Equal((9, 11, 2), (narrow.Lsb(), narrow.Msb(), narrow.PopCount()));
        Assert.Equal((3, 100, 2), (wide.Lsb(), wide.Msb(), wide.PopCount()));
        Assert.Equal(64, default(Bitboard64).Lsb());
        Assert.Equal(128, default(Bitboard128).Lsb());
        Assert.Equal(-1, default(Bitboard64).Msb());
        Assert.True(wide.HasBit(100));
        Assert.Equal(34, wide.ToHex().Length);
    }

    // ---- allocation ------------------------------------------------------------------------------------------

    private static long AllocatedByGenerateMoves<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain) where TState : struct, IEquatable<TState>
    {
        var buffer = new TMove[domain.MaxMoves];
        var states = new List<TState> { domain.InitialState };
        for (int i = 0; i < states.Count && states.Count < 40; i++)
        {
            int n = domain.GenerateMoves(states[i], buffer);
            for (int k = 0; k < n && states.Count < 40; k++) states.Add(domain.ApplyMove(states[i], buffer[k]));
        }
        foreach (var s in states) domain.GenerateMoves(s, buffer);   // warm up (JIT, caches)
        // The least of three passes: an allocation of the move generator shows in every pass, while the runtime may allocate once in a pass
        // that happens to meet a tier-up under load (seen once on net8.0 with every target testing in parallel).
        long least = long.MaxValue;
        for (int pass = 0; pass < 3; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var s in states) domain.GenerateMoves(s, buffer);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return least;
    }

    public static TheoryData<string> Domains => new()
    {
        "npuzzle", "klotski", "rushhour", "lab2", "lab3-rotation", "lab3-carrier", "lab3-ice", "feature",
        "wide",
        "hex",
    };

    [Theory]
    [MemberData(nameof(Domains))]
    public void GenerateMovesDoesNotAllocate(string name)
    {
        long bytes = name switch
        {
            "npuzzle" => AllocatedByGenerateMoves(Fixtures.CreatePuzzle15()),
            "klotski" => AllocatedByGenerateMoves(Fixtures.CreateKlotski()),
            "rushhour" => AllocatedByGenerateMoves(Fixtures.CreateRushHour()),
            "lab2" => AllocatedByGenerateMoves(Fixtures.CreateLab2()),
            "lab3-rotation" => AllocatedByGenerateMoves(Fixtures.CreateRotation().Domain),
            "lab3-carrier" => AllocatedByGenerateMoves(Fixtures.CreateCarrier().Domain),
            "lab3-ice" => AllocatedByGenerateMoves(Fixtures.CreateIce().Domain),
            "feature" => AllocatedByGenerateMoves(FeatureDomain()),
            "wide" => AllocatedByGenerateMoves(WideDomain()),
            _ => AllocatedByGenerateMoves(HexDomain()),
        };
        Assert.Equal(0, bytes);
    }
}
