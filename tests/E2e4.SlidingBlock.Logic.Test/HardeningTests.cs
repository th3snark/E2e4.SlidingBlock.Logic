using E2e4.SlidingBlock.Logic.Text;

namespace E2e4.SlidingBlock.Logic.Test;

/// <summary>Argument checks, solver statuses and the other guards on the public API.</summary>
public class HardeningTests
{
    // A line of ten positions with a goal that may be off the end of it: the smallest domain that can be solved, run out of moves or cut short.
    private readonly record struct Spot(int X);

    private sealed class Line(int goal) : ISlidingBlockDomain<Spot, int>
    {
        public Spot InitialState => new(0);
        public int MaxMoves => 2;
        public bool IsGoal(in Spot state) => state.X == goal;

        public int GenerateMoves(in Spot state, Span<int> moveBuffer)
        {
            int n = 0;
            if (state.X > 0) moveBuffer[n++] = -1;
            if (state.X < 9) moveBuffer[n++] = 1;
            return n;
        }

        public Spot ApplyMove(in Spot state, in int move) => new(state.X + move);
        public ulong GetHashCode64(in Spot state) => Hashing.Mix((ulong)state.X);
    }

    [Fact]
    public void SolversSayWhyTheyFoundNothing()
    {
        var reachable = new Line(5);
        var unreachable = new Line(20);
        var solver = new PuzzleSolver { MaxNodes = 3 };
        var roomy = new PuzzleSolver();

        Assert.NotNull(roomy.SolveBfs(reachable, reachable.InitialState, out var status));
        Assert.Equal(SolveStatus.Solved, status);
        Assert.Null(solver.SolveBfs(new Line(9), new Spot(0), out status));
        Assert.Equal(SolveStatus.NodeLimit, status);
        Assert.Null(roomy.SolveBfs(unreachable, unreachable.InitialState, out status));
        Assert.Equal(SolveStatus.Unsolvable, status);

        Assert.NotNull(roomy.SolveAStar(reachable, reachable.InitialState, _ => 0, out status));
        Assert.Equal(SolveStatus.Solved, status);
        Assert.Null(solver.SolveAStar(new Line(9), new Spot(0), _ => 0, out status));
        Assert.Equal(SolveStatus.NodeLimit, status);
        Assert.Null(roomy.SolveAStar(unreachable, unreachable.InitialState, _ => 0, out status));
        Assert.Equal(SolveStatus.Unsolvable, status);

        Assert.NotNull(roomy.SolveIdaStar(reachable, reachable.InitialState, _ => 0, out status));
        Assert.Equal(SolveStatus.Solved, status);
        Assert.Null(solver.SolveIdaStar(new Line(9), new Spot(0), _ => 0, out status));
        Assert.Equal(SolveStatus.NodeLimit, status);
        Assert.Null(roomy.SolveIdaStar(unreachable, unreachable.InitialState, _ => 0, out status));
        Assert.Equal(SolveStatus.Unsolvable, status);
    }

    [Fact]
    public void SolverRejectsBadArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PuzzleSolver { MaxNodes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PuzzleSolver { MaxNodes = -5 });
        var solver = new PuzzleSolver();
        Assert.Throws<ArgumentNullException>(() => solver.SolveBfs<Spot, int>(null!, new Spot(0)));
        Assert.Throws<ArgumentNullException>(() => solver.SolveAStar(new Line(1), new Spot(0), null!));
        Assert.Throws<ArgumentNullException>(() => solver.SolveIdaStar(new Line(1), new Spot(0), null!));
        Assert.Throws<ArgumentNullException>(() => PuzzleWalk.RandomWalk(new Line(1), new Spot(0), 3, null!));
        Assert.Throws<ArgumentNullException>(() => new PlaySession<Spot, int>(null!, new Spot(0)));
    }

    // A one-way corridor whose goal is the far end: with a perfect heuristic IDA* walks it in one pass, one recursion level per move.
    private sealed class Corridor(int length) : ISlidingBlockDomain<Spot, int>
    {
        public Spot InitialState => new(0);
        public int MaxMoves => 1;
        public bool IsGoal(in Spot state) => state.X == length;

        public int GenerateMoves(in Spot state, Span<int> moveBuffer)
        {
            if (state.X >= length) return 0;
            moveBuffer[0] = 1;
            return 1;
        }

        public Spot ApplyMove(in Spot state, in int move) => new(state.X + move);
        public ulong GetHashCode64(in Spot state) => Hashing.Mix((ulong)state.X);
    }

    [Fact]
    public void IdaStarThrowsInsteadOfOverflowingTheStack()
    {
        // A million levels of recursion cannot fit on any thread's stack; the guard must turn that into an exception the caller can catch.
        const int length = 1_000_000;
        var solver = new PuzzleSolver { MaxNodes = 10_000_000 };
        Assert.Throws<InsufficientExecutionStackException>(() =>
            solver.SolveIdaStar(new Corridor(length), new Spot(0), s => length - s.X));
        var shallow = solver.SolveIdaStar(new Corridor(50), new Spot(0), s => 50 - s.X);
        Assert.Equal(50, shallow!.Moves.Count);
    }

    [Fact]
    public void ReachableAnchorSearchesRejectAStartOffTheBoard()
    {
        const ulong all9 = (1UL << 9) - 1;
        Span<sbyte> parents = stackalloc sbyte[9];
        Assert.Throws<ArgumentOutOfRangeException>(() => PolyominoState<ulong>.FindReachableAnchors(-1, 1UL, all9, 0UL, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolyominoState<ulong>.FindReachableAnchors(64, 1UL, all9, 0UL, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolyominoState<ulong>.FindReachableAnchors(0, 1UL, all9, 0UL, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolyominoState<ulong>.ReconstructHookPath(-1, 4, 1UL, all9, 0UL, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolyominoState<ulong>.ReconstructHookPath(0, 4, 1UL, all9, 0UL, -3));
        var tooShort = new sbyte[4];
        try
        {
            PolyominoState<ulong>.FindReachableAnchors(6, 1UL, all9, 0UL, 3, tooShort, out _, out _);
            Assert.Fail("A parent map shorter than the start anchor must be rejected.");
        }
        catch (ArgumentException ex) { Assert.Equal("parentMap", ex.ParamName); }
        Assert.NotEqual(0UL, PolyominoState<ulong>.FindReachableAnchors(6, 1UL, all9, 0UL, 3, parents, out _, out _));
    }

    [Fact]
    public void NodeLimitCountsExpandedNodes()
    {
        // The line's goal is nine steps away, so a limit of 9 expansions is not enough to reach it by BFS.
        var line = new Line(9);
        Assert.Null(new PuzzleSolver { MaxNodes = 8 }.SolveBfs(line, new Spot(0)));
        Assert.NotNull(new PuzzleSolver { MaxNodes = 10 }.SolveBfs(line, new Spot(0)));
    }

    [Fact]
    public void ToBinaryAcceptsNoGrouping()
    {
        Assert.Equal("0101", 5UL.ToBinary(4, 0));
        Assert.Equal("01_01", 5UL.ToBinary(4, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => 5UL.ToBinary(4, -1));
    }

    [Fact]
    public void NPuzzleChecksItsPositions()
    {
        var goal = new NPuzzleState(0xFEDCBA9876543210UL);
        Assert.True(goal.IsPermutation);
        Assert.False(new NPuzzleState(0).IsPermutation);
        Assert.Throws<ArgumentException>(() => new NPuzzleDomain(new NPuzzleState(0), goal));
        Assert.Throws<ArgumentException>(() => new NPuzzleDomain(goal, new NPuzzleState(0)));
        Assert.Equal(5, goal.SquareOf(5));
        Assert.Equal(-1, new NPuzzleState(0).SquareOf(5));
    }

    [Fact]
    public void NPuzzleSolvabilityMatchesTheMoves()
    {
        var goal = new NPuzzleState(0xFEDCBA9876543210UL);
        var domain = new NPuzzleDomain(goal, goal);
        Assert.True(domain.IsSolvable(goal));
        for (int seed = 0; seed < 20; seed++)
            Assert.True(domain.IsSolvable(PuzzleWalk.RandomWalk(domain, goal, 40, new Random(seed))));

        // Swapping two tiles is an odd permutation with the blank in place: no sequence of moves undoes it.
        ulong swapped = goal.PackedNibbles & ~(0xFFUL << 4) | (0x12UL << 4);
        Assert.True(new NPuzzleState(swapped).IsPermutation);
        Assert.False(domain.IsSolvable(new NPuzzleState(swapped)));
        Assert.False(domain.IsSolvable(new NPuzzleState(0)));
    }

    [Fact]
    public void KlotskiRejectsBoardsItsKeyCannotHold()
    {
        var wide = PuzzleTopology<ulong>.CreateBuilder(6, 4);   // 24 cells: the 3-bit-per-cell key holds 21
        wide.AddPiece(wide.AddGroup("one", [(0, 0)]), "a", 0);
        Assert.Throws<ArgumentException>(() => new KlotskiDomain(wide.Build(), 0, 1));
    }

    [Fact]
    public void KlotskiChecksItsMasterPiece()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(4, 4);
        int one = b.AddGroup("one", [(0, 0)]);
        b.AddPiece(one, "a", 0);
        b.AddPiece(one, "b", 1);
        var topology = b.Build();
        Assert.Throws<ArgumentException>(() => new KlotskiDomain(topology, 0, 5));            // shares its shape with piece 1
        Assert.Throws<ArgumentOutOfRangeException>(() => new KlotskiDomain(topology, 7, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KlotskiDomain(topology, -1, 5));
    }

    [Fact]
    public void TopologyBuilderChecksItsArguments()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(3, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => b.AddWall(3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.AddWall(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.AddDiode(0, 3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.AddPiece(0, "a", 0));   // no group yet
        Assert.Throws<ArgumentException>(() => b.AddRotatableGroup("r", []));
        Assert.Throws<ArgumentOutOfRangeException>(() => PuzzleTopology<ulong>.CreateBuilder(100000, 100000));
    }

    [Fact]
    public void TopologyCollectionsCannotBeWrittenTo()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(3, 3);
        b.AddPiece(b.AddGroup("one", [(0, 0)]), "a", 0);
        var topology = b.Build();
        Assert.False(topology.Groups is EquivalenceGroup<ulong>[]);
        Assert.False(topology.Pieces is PieceTemplate[]);
        Assert.False(topology.Diodes is DirectionalDiode[]);
        Assert.False(topology.Partitions is Partition[]);
        Assert.False(topology.PressurePlates is PressurePlateRule<ulong>[]);
    }

    [Fact]
    public void LaneSlideChecksItsVehicles()
    {
        LaneVehicle[] ok = [new(0, true, 2, 1, 0, 0), new(1, true, 2, 1, 1, 0)];
        Assert.NotNull(new LaneSlideDomain<LaneState16, ulong>(ok, 4, 2, 0, 2));

        Assert.Throws<ArgumentException>(() => new LaneSlideDomain<LaneState16, ulong>([new(1, true, 2, 1, 0, 0)], 4, 2, 0, 2));       // index is not the list position
        Assert.Throws<ArgumentException>(() => new LaneSlideDomain<LaneState16, ulong>([new(0, true, 0, 1, 0, 0)], 4, 2, 0, 2));       // no length
        Assert.Throws<ArgumentException>(() => new LaneSlideDomain<LaneState16, ulong>([new(0, true, 2, 1, 5, 0)], 4, 2, 0, 2));       // lane off the board
        Assert.Throws<ArgumentException>(() => new LaneSlideDomain<LaneState16, ulong>([new(0, true, 2, 1, 0, 3)], 4, 2, 0, 2));       // starts outside its lane
        Assert.Throws<ArgumentException>(() => new LaneSlideDomain<LaneState16, ulong>([new(0, true, 2, 1, 0, 0), new(1, true, 2, 1, 0, 1)], 4, 2, 0, 2));   // overlap
    }

    [Fact]
    public void LaneSlideKeepsItsOwnCopyOfTheVehicles()
    {
        var list = new List<LaneVehicle> { new(0, true, 2, 1, 0, 0) };
        var domain = new LaneSlideDomain<LaneState16, ulong>(list, 4, 1, 0, 2);
        list.Add(new LaneVehicle(1, true, 1, 1, 0, 3));
        Assert.Single(domain.Vehicles);
    }

    [Fact]
    public void LaneStateMasksOffsetsToTheirNibble()
    {
        var state = default(LaneState16).WithOffset(1, 0x15);   // only the low four bits belong to the vehicle
        Assert.Equal(5, state.OffsetOf(1));
        Assert.Equal(0, state.OffsetOf(2));
    }

    [Fact]
    public void PushDomainTakesItsLayoutAsData()
    {
        var data = new PushDomain(5, 1, Enumerable.Range(0, 5), [3], [1], 0);
        var parsed = PushMap.CreateDomain(["@1.X."], Fixtures.CaseSymbols);
        Assert.Equal(parsed.InitialState, data.InitialState);
        Assert.Equal(parsed.Targets, data.Targets);
        Assert.Single(new PuzzleSolver().SolveBfs(data, data.InitialState)!.Moves);
    }

    [Fact]
    public void PushDomainChecksItsLayout()
    {
        int[] floor = [0, 1, 2, 3, 4];
        Assert.Throws<ArgumentException>(() => new PushDomain(5, 1, floor, [3], [1], 9));         // storeman off the board
        Assert.Throws<ArgumentException>(() => new PushDomain(5, 1, floor, [3], [1, 2], 0));      // more cases than places
        Assert.Throws<ArgumentException>(() => new PushDomain(5, 1, floor, [3], [3, 3], 0));
        Assert.Throws<ArgumentException>(() => new PushDomain(5, 1, floor, [3], [0], 0));         // a case under the storeman
        Assert.Throws<ArgumentException>(() => new PushDomain(5, 1, [0, 1, 2], [3], [1], 0));     // place on a wall
        Assert.Throws<ArgumentException>(() => new PushDomain(5, 1, floor, [3], [], 0));
        Assert.Throws<ArgumentException>(() => new PushDomain(PushDomain.MaxCells + 1, 1, floor, [3], [1], 0));
        Assert.Throws<ArgumentException>(() => PushMap.CreateDomain(Array.Empty<string>(), Fixtures.CaseSymbols));
    }

    [Fact]
    public void DefaultPushStateIsEmptyNotBroken()
    {
        var empty = default(PushState);
        Assert.Equal(0, empty.CaseCount);
        Assert.True(empty.Equals(default));
        Assert.False(empty.Equals(PushMap.CreateDomain(["@1.X."], Fixtures.CaseSymbols).InitialState));
        Assert.Throws<InvalidOperationException>(() => empty.CellOf(0));
    }
}
