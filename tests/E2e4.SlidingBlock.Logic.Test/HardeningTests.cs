using E2e4.SlidingBlock.Logic.Text;

namespace E2e4.SlidingBlock.Logic.Test;

/// <summary>Argument checks, solver statuses and the other guards on the public API.</summary>
public class HardeningTests
{
    // A line of ten positions with a goal that may be off the end of it: the smallest domain that can be solved, run out of moves or cut short.
    private readonly record struct Spot(int X);

    private sealed class Line(int goal) : IWeightedDomain<Spot, int>
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
        public long MoveCost(in Spot state, in int move) => 1;
    }

    [Fact]
    public void SolversSayWhyTheyFoundNothing()
    {
        var reachable = new Line(5);
        var unreachable = new Line(20);
        var solver = new PuzzleSolver { MaxNodes = 3 };
        var roomy = new PuzzleSolver();

        Assert.Equal(SolveStatus.Solved, roomy.SolveBfs(reachable, reachable.InitialState).Status);
        Assert.Equal(SolveStatus.NodeLimit, solver.SolveBfs(new Line(9), new Spot(0)).Status);
        Assert.Equal(SolveStatus.Unsolvable, roomy.SolveBfs(unreachable, unreachable.InitialState).Status);

        Assert.Equal(SolveStatus.Solved, roomy.SolveAStar(reachable, reachable.InitialState, _ => 0).Status);
        Assert.Equal(SolveStatus.NodeLimit, solver.SolveAStar(new Line(9), new Spot(0), _ => 0).Status);
        Assert.Equal(SolveStatus.Unsolvable, roomy.SolveAStar(unreachable, unreachable.InitialState, _ => 0).Status);

        Assert.Equal(SolveStatus.Solved, roomy.SolveIdaStar(reachable, reachable.InitialState, _ => 0).Status);
        Assert.Equal(SolveStatus.NodeLimit, solver.SolveIdaStar(new Line(9), new Spot(0), _ => 0).Status);
        Assert.Equal(SolveStatus.Unsolvable, roomy.SolveIdaStar(unreachable, unreachable.InitialState, _ => 0).Status);
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
        Assert.Equal(50, shallow.Moves.Count);
    }

    [Fact]
    public void ReachableAnchorSearchesRejectAStartOffTheBoard()
    {
        const ulong all9 = (1UL << 9) - 1;
        Span<sbyte> parents = stackalloc sbyte[9];
        Assert.Throws<ArgumentOutOfRangeException>(() => PolyominoFloodFill.FindReachableAnchors<Bitboard64>(-1, 1UL, all9, 0UL, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolyominoFloodFill.FindReachableAnchors<Bitboard64>(64, 1UL, all9, 0UL, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolyominoFloodFill.FindReachableAnchors<Bitboard64>(0, 1UL, all9, 0UL, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolyominoFloodFill.ReconstructHookPath<Bitboard64>(-1, 4, 1UL, all9, 0UL, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolyominoFloodFill.ReconstructHookPath<Bitboard64>(0, 4, 1UL, all9, 0UL, -3));
        var tooShort = new sbyte[4];
        try
        {
            PolyominoFloodFill.FindReachableAnchors<Bitboard64>(6, 1UL, all9, 0UL, 3, tooShort, out _, out _);
            Assert.Fail("A parent map shorter than the start anchor must be rejected.");
        }
        catch (ArgumentException ex) { Assert.Equal("parentMap", ex.ParamName); }
        Assert.NotEqual(0UL, PolyominoFloodFill.FindReachableAnchors<Bitboard64>(6, 1UL, all9, 0UL, 3, parents, out _, out _));
    }

    [Fact]
    public void NodeLimitCountsExpandedNodes()
    {
        // The line's goal is nine steps away, so a limit of 9 expansions is not enough to reach it by BFS.
        var line = new Line(9);
        Assert.False(new PuzzleSolver { MaxNodes = 8 }.SolveBfs(line, new Spot(0)).IsSolved);
        Assert.True(new PuzzleSolver { MaxNodes = 10 }.SolveBfs(line, new Spot(0)).IsSolved);
    }

    [Fact]
    public void StateLimitStopsTheSearchesThatStorePositions()
    {
        // Reaching the end of the line stores all ten of its positions.
        var line = new Line(9);
        var capped = new PuzzleSolver { MaxStoredStates = 4 };
        Assert.Equal(SolveStatus.StateLimit, capped.SolveBfs(line, line.InitialState).Status);
        Assert.Equal(SolveStatus.StateLimit, capped.SolveAStar(line, line.InitialState, _ => 0).Status);
        Assert.Equal(SolveStatus.StateLimit, capped.SolveDijkstra(line, line.InitialState).Status);
        Assert.Equal(SolveStatus.StateLimit, capped.SolveWeightedAStar(line, line.InitialState, _ => 0L).Status);
        Assert.True(capped.SolveIdaStar(line, line.InitialState, _ => 0).IsSolved);   // IDA* stores only its path
        Assert.True(capped.SolveBfs(new Line(3), new Spot(0)).IsSolved);
        Assert.True(new PuzzleSolver { MaxStoredStates = 10 }.SolveBfs(line, line.InitialState).IsSolved);
        Assert.Equal(int.MaxValue, new PuzzleSolver().MaxStoredStates);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PuzzleSolver { MaxStoredStates = 0 });
    }

    // A walk over a square grid from one corner to the other, hashed either by the cell number alone or mixed over all 64 bits.
    private readonly record struct Cell(int X, int Y);

    private sealed class Walk(int size, bool mixHash) : ISlidingBlockDomain<Cell, int>
    {
        public Cell InitialState => new(0, 0);
        public int MaxMoves => 4;
        public bool IsGoal(in Cell state) => state.X == size - 1 && state.Y == size - 1;

        public int GenerateMoves(in Cell state, Span<int> moveBuffer)
        {
            int n = 0;
            if (state.X > 0) moveBuffer[n++] = 0;
            if (state.X < size - 1) moveBuffer[n++] = 1;
            if (state.Y > 0) moveBuffer[n++] = 2;
            if (state.Y < size - 1) moveBuffer[n++] = 3;
            return n;
        }

        public Cell ApplyMove(in Cell state, in int move) => move switch
        {
            0 => state with { X = state.X - 1 },
            1 => state with { X = state.X + 1 },
            2 => state with { Y = state.Y - 1 },
            _ => state with { Y = state.Y + 1 },
        };

        public ulong GetHashCode64(in Cell state)
        {
            ulong cell = (ulong)(state.Y * size + state.X);
            return mixHash ? Hashing.Mix(cell) : cell;
        }
    }

    [Fact]
    public void AHashThatVariesInFewBitsGivesTheSameSearch()
    {
        var plain = new PuzzleSolver().SolveBfs(new Walk(120, false), new Cell(0, 0));
        var mixed = new PuzzleSolver().SolveBfs(new Walk(120, true), new Cell(0, 0));
        Assert.True(plain.IsSolved);
        Assert.Equal(2 * 119, plain.Moves.Count);
        Assert.Equal(mixed.NodesExpanded, plain.NodesExpanded);
    }

    [Fact]
    public void TableSlotsSpreadHashesThatVaryInFewBits()
    {
        // The table used to take the slot from the upper 32 bits alone, so these all landed on slot 0.
        const int bits = 11;   // a table of 2048 slots
        Assert.True(Distinct(h => h) > 1000);         // small numbers
        Assert.True(Distinct(h => h << 40) > 1000);   // a few high bits

        static int Distinct(Func<ulong, ulong> hash)
        {
            var slots = new HashSet<int>();
            for (ulong h = 0; h < 1024; h++) slots.Add(PuzzleSolver.TableSlot(hash(h), bits));
            return slots.Count;
        }
    }

    [Fact]
    public void ToBinaryAcceptsNoGrouping()
    {
        Assert.Equal("0101", new Bitboard64(5).ToBinary(4, 0));
        Assert.Equal("01_01", new Bitboard64(5).ToBinary(4, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bitboard64(5).ToBinary(4, -1));
    }

    [Fact]
    public void NPuzzleChecksItsPositions()
    {
        var goal = new NPuzzle4x4State(0xFEDCBA9876543210UL);
        Assert.True(goal.IsPermutation);
        Assert.False(new NPuzzle4x4State(0).IsPermutation);
        Assert.Throws<ArgumentException>(() => new NPuzzle4x4Domain(new NPuzzle4x4State(0), goal));
        Assert.Throws<ArgumentException>(() => new NPuzzle4x4Domain(goal, new NPuzzle4x4State(0)));
        Assert.Equal(5, goal.SquareOf(5));
        Assert.Equal(-1, new NPuzzle4x4State(0).SquareOf(5));
    }

    [Fact]
    public void NPuzzleSolvabilityMatchesTheMoves()
    {
        var goal = new NPuzzle4x4State(0xFEDCBA9876543210UL);
        var domain = new NPuzzle4x4Domain(goal, goal);
        Assert.True(domain.IsSolvable(goal));
        for (int seed = 0; seed < 20; seed++)
            Assert.True(domain.IsSolvable(PuzzleWalk.RandomWalk(domain, goal, 40, new Random(seed))));

        // Swapping two tiles is an odd permutation with the blank in place: no sequence of moves undoes it.
        ulong swapped = goal.PackedNibbles & ~(0xFFUL << 4) | (0x12UL << 4);
        Assert.True(new NPuzzle4x4State(swapped).IsPermutation);
        Assert.False(domain.IsSolvable(new NPuzzle4x4State(swapped)));
        Assert.False(domain.IsSolvable(new NPuzzle4x4State(0)));
    }

    [Fact]
    public void KlotskiRejectsBoardsItsKeyCannotHold()
    {
        var wide = PuzzleTopology<Bitboard64>.CreateBuilder(6, 4);   // 24 cells: the 3-bit-per-cell key holds 21
        wide.AddPiece(wide.AddGroup("one", [(0, 0)]), "a", 0);
        Assert.Throws<ArgumentException>(() => new KlotskiDomain(wide.Build(), 0, 1));
    }

    [Fact]
    public void KlotskiChecksItsMasterPiece()
    {
        var b = PuzzleTopology<Bitboard64>.CreateBuilder(4, 4);
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
        var b = PuzzleTopology<Bitboard64>.CreateBuilder(3, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => b.AddWall(3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.AddWall(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.AddDiode(0, 3, GridDirections.Left));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.AddPiece(0, "a", 0));   // no group yet
        Assert.Throws<ArgumentException>(() => b.AddRotatableGroup("r", []));
        Assert.Throws<ArgumentOutOfRangeException>(() => PuzzleTopology<Bitboard64>.CreateBuilder(100000, 100000));
    }

    [Fact]
    public void TopologyQueriesCheckTheirGroupAndDirection()
    {
        var b = PuzzleTopology<Bitboard64>.CreateBuilder(3, 3);
        b.AddPiece(b.AddGroup("one", [(0, 0)]), "a", 0);
        var topo = b.Build();
        Assert.Equal(topo.Groups[0].ValidAnchorsMask, topo.ValidAnchorsFor(0, GridDirection.Down));
        Assert.True(topo.ForbiddenEntryCells(GridDirection.Left).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => topo.ValidAnchorsFor(1, GridDirection.Down));
        Assert.Throws<ArgumentOutOfRangeException>(() => topo.ValidAnchorsFor(-1, GridDirection.Down));
        Assert.Throws<ArgumentOutOfRangeException>(() => topo.ValidAnchorsFor(0, GridDirection.UpRight));   // orthogonal board
        Assert.Throws<ArgumentOutOfRangeException>(() => topo.ForbiddenEntryCells(GridDirection.DownLeft));
        Assert.Throws<ArgumentOutOfRangeException>(() => topo.ForbiddenEntryCells((GridDirection)200));
    }

    [Fact]
    public void TopologyBuilderNamesTheBadBoardDimensionAndChecksLabelsNamesAndPlateMasks()
    {
        Assert.Equal("columns", Assert.Throws<ArgumentOutOfRangeException>(() => PuzzleTopology<Bitboard64>.CreateBuilder(0, 3)).ParamName);
        Assert.Equal("rows", Assert.Throws<ArgumentOutOfRangeException>(() => PuzzleTopology<Bitboard64>.CreateBuilder(3, -1)).ParamName);
        Assert.Equal("rows", Assert.Throws<ArgumentOutOfRangeException>(() => PuzzleTopology<Bitboard64>.CreateBuilder(9, 8)).ParamName);   // 72 cells, 64 bits

        var b = PuzzleTopology<Bitboard64>.CreateBuilder(3, 3);
        var one = b.AddGroup("one", [(0, 0)]);
        Assert.Throws<ArgumentNullException>(() => b.AddGroup(null!, [(0, 0)]));
        Assert.Throws<ArgumentNullException>(() => b.AddRotatableGroup(null!, [(0, 0)]));
        Assert.Throws<ArgumentNullException>(() => b.AddPiece(one, null!, 0));

        Assert.Equal("gateCellsMask", Assert.Throws<ArgumentException>(() => b.AddPressurePlate(1UL, 1UL << 9)).ParamName);
        Assert.Equal("triggerCellsMask", Assert.Throws<ArgumentException>(() => b.AddPressurePlate(1UL << 63, 1UL)).ParamName);
        b.AddPressurePlate(1UL << 8, 1UL);   // the last cell is still on the board
    }

    [Fact]
    public void TopologyCollectionsCannotBeWrittenTo()
    {
        var b = PuzzleTopology<Bitboard64>.CreateBuilder(3, 3);
        b.AddPiece(b.AddGroup("one", [(0, 0)]), "a", 0);
        var topology = b.Build();
        Assert.False(topology.Groups is EquivalenceGroup<Bitboard64>[]);
        Assert.False(topology.Pieces is PieceTemplate[]);
        Assert.False(topology.Diodes is DirectionalDiode[]);
        Assert.False(topology.Partitions is Partition[]);
        Assert.False(topology.PressurePlates is PressurePlateRule<Bitboard64>[]);
    }

    [Fact]
    public void LaneSlideChecksItsVehicles()
    {
        LaneVehicle[] ok = [new(0, true, 2, 1, 0, 0), new(1, true, 2, 1, 1, 0)];
        Assert.NotNull(new LaneSlideDomain<LaneState16, Bitboard64>(ok, 4, 2, 0, 2));

        Assert.Throws<ArgumentException>(() => new LaneSlideDomain<LaneState16, Bitboard64>([new(1, true, 2, 1, 0, 0)], 4, 2, 0, 2));       // index is not the list position
        Assert.Throws<ArgumentException>(() => new LaneSlideDomain<LaneState16, Bitboard64>([new(0, true, 0, 1, 0, 0)], 4, 2, 0, 2));       // no length
        Assert.Throws<ArgumentException>(() => new LaneSlideDomain<LaneState16, Bitboard64>([new(0, true, 2, 1, 5, 0)], 4, 2, 0, 2));       // lane off the board
        Assert.Throws<ArgumentException>(() => new LaneSlideDomain<LaneState16, Bitboard64>([new(0, true, 2, 1, 0, 3)], 4, 2, 0, 2));       // starts outside its lane
        Assert.Throws<ArgumentException>(() => new LaneSlideDomain<LaneState16, Bitboard64>([new(0, true, 2, 1, 0, 0), new(1, true, 2, 1, 0, 1)], 4, 2, 0, 2));   // overlap
    }

    [Fact]
    public void LaneSlideKeepsItsOwnCopyOfTheVehicles()
    {
        var list = new List<LaneVehicle> { new(0, true, 2, 1, 0, 0) };
        var domain = new LaneSlideDomain<LaneState16, Bitboard64>(list, 4, 1, 0, 2);
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
        Assert.Single(new PuzzleSolver().SolveBfs(data, data.InitialState).Moves);
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
    public void CompositeDomainRejectsMissingPartsAndAPushDepthBelowOne()
    {
        var b = PuzzleTopology<Bitboard64>.CreateBuilder(3, 3);
        b.AddPiece(b.AddGroup("one", Cells.One), "a", 0);
        var topology = b.Build();
        var goal = new PolyominoGoal<Bitboard64>(0, 8);
        Assert.Throws<ArgumentNullException>(() => new CompositeDomain<Bitboard64>((PolyominoDomain<Bitboard64>)null!));
        Assert.Throws<ArgumentNullException>(() => new CompositeDomain<Bitboard64>(null!, goal));
        Assert.Throws<ArgumentNullException>(() => new CompositeDomain<Bitboard64>(topology, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompositeDomain<Bitboard64>(topology, goal, 0));
        Assert.Equal(1, new CompositeDomain<Bitboard64>(topology, goal).MaxPushDepth);
    }

    [Fact]
    public void PolyominoGoalsMustFitThePositionAndTheBoard()
    {
        Assert.Equal("groupId", Assert.Throws<ArgumentOutOfRangeException>(() => new PolyominoGoal<Bitboard64>(16, 0)).ParamName);
        Assert.Equal("groupId", Assert.Throws<ArgumentOutOfRangeException>(() => new PolyominoGoal<Bitboard64>(-1, 0)).ParamName);
        Assert.Equal("anchor", Assert.Throws<ArgumentOutOfRangeException>(() => new PolyominoGoal<Bitboard64>(0, 64)).ParamName);
        Assert.Equal(100, new PolyominoGoal<Bitboard128>(0, 100).Anchor);

        var b = PuzzleTopology<Bitboard64>.CreateBuilder(3, 3);
        b.AddPiece(b.AddGroup("one", Cells.One), "a", 0);
        var topology = b.Build();
        Assert.Throws<ArgumentNullException>(() => new PolyominoDomain<Bitboard64>(null!, 0, 8));
        Assert.Throws<ArgumentNullException>(() => new PolyominoDomain<Bitboard64>(topology, (PolyominoGoal<Bitboard64>)null!));
        Assert.Equal("goalGroupId", Assert.Throws<ArgumentOutOfRangeException>(() => new PolyominoDomain<Bitboard64>(topology, 1, 8)).ParamName);
        Assert.Equal("goalAnchor", Assert.Throws<ArgumentOutOfRangeException>(() => new PolyominoDomain<Bitboard64>(topology, 0, 9)).ParamName);
        Assert.Equal("goal", Assert.Throws<ArgumentException>(() => new PolyominoDomain<Bitboard64>(topology, new PolyominoGoal<Bitboard64>(3, 0))).ParamName);
        Assert.Equal("goal", Assert.Throws<ArgumentException>(() => new CompositeDomain<Bitboard64>(topology, new PolyominoGoal<Bitboard64>(0, 9))).ParamName);
        Assert.Equal(8, new PolyominoDomain<Bitboard64>(topology, 0, 8).Goal.Anchor);
    }

    [Fact]
    public void LaneSlideChecksTheVehicleOfAQueryAtOnce()
    {
        var domain = new LaneSlideDomain<LaneState16, Bitboard64>([new(0, true, 2, 1, 0, 0)], 4, 1, 0, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => domain.ReachableOffsets(domain.InitialState, 1));   // thrown before any enumeration
        Assert.Equal([1, 2], domain.ReachableOffsets(domain.InitialState, 0));
    }

    [Fact]
    public void PushDomainCellQueriesRejectCellsOffTheWarehouse()
    {
        var domain = PushMap.CreateDomain(["@1.X."], Fixtures.CaseSymbols);
        Assert.True(domain.IsFloor(0));
        foreach (int cell in new[] { -1, domain.CellCount })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => domain.IsFloor(cell));
            Assert.Throws<ArgumentOutOfRangeException>(() => domain.IsTarget(cell));
            Assert.Throws<ArgumentOutOfRangeException>(() => domain.IsAlive(cell));
            Assert.Throws<ArgumentOutOfRangeException>(() => domain.PushDistance(cell));
        }
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
