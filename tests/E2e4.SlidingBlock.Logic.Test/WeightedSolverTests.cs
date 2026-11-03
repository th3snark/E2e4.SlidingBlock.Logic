namespace E2e4.SlidingBlock.Logic.Test;

public class WeightedSolverTests
{
    /// <summary>A number line 0..Length: a step of 1 costs 1, a leap of 6 costs 100, so the cheapest route is not the shortest one.</summary>
    private sealed class NumberLine(int length, int goal, long leapCost = 100) : IWeightedDomain<NumberLine.Pos, int>
    {
        public readonly record struct Pos(int Value);

        public Pos InitialState => new(0);
        public int MaxMoves => 2;
        public bool IsGoal(in Pos state) => state.Value == goal;

        public int GenerateMoves(in Pos state, Span<int> moveBuffer)
        {
            int n = 0;
            if (state.Value + 1 <= length) moveBuffer[n++] = 1;
            if (state.Value + 6 <= length) moveBuffer[n++] = 6;
            return n;
        }

        public Pos ApplyMove(in Pos state, in int move) => new(state.Value + move);
        public ulong GetHashCode64(in Pos state) => Hashing.Mix((ulong)state.Value);
        public long MoveCost(in Pos state, in int move) => move == 1 ? 1 : leapCost;
    }

    [Fact]
    public void DijkstraFindsTheCheapestRouteNotTheShortest()
    {
        var domain = new NumberLine(10, 6);
        var solver = new PuzzleSolver();

        var shortest = solver.SolveBfs(domain, domain.InitialState);
        var cheapest = solver.SolveDijkstra(domain, domain.InitialState);

        Assert.Equal([6], shortest.Moves);
        Assert.Equal(6, cheapest.Cost);
        Assert.Equal(6, cheapest.Moves.Count);
    }

    [Fact]
    public void DijkstraTakesTheLeapWhenItIsTheCheaperRoute()
    {
        var domain = new NumberLine(10, 6, leapCost: 3);

        var result = new PuzzleSolver().SolveDijkstra(domain, domain.InitialState);

        Assert.Equal([6], result.Moves);
        Assert.Equal(3, result.Cost);
    }

    [Fact]
    public void WeightedAStarWithAnAdmissibleHeuristicAgreesWithDijkstra()
    {
        var domain = new NumberLine(30, 17);
        var solver = new PuzzleSolver();

        var plain = solver.SolveDijkstra(domain, domain.InitialState);
        var guided = solver.SolveWeightedAStar(domain, domain.InitialState, p => Math.Max(0, 17 - p.Value) / 6);

        Assert.Equal(plain.Cost, guided.Cost);
        Assert.True(guided.NodesExpanded <= plain.NodesExpanded);
    }

    [Fact]
    public void ReportsUnsolvableAndTheNodeLimit()
    {
        var domain = new NumberLine(10, 11);

        Assert.Equal(SolveStatus.Unsolvable, new PuzzleSolver().SolveDijkstra(domain, domain.InitialState).Status);

        Assert.Equal(SolveStatus.NodeLimit, new PuzzleSolver { MaxNodes = 2 }.SolveDijkstra(new NumberLine(1000, 999), new NumberLine.Pos(0)).Status);
    }

    [Fact]
    public void RejectsMissingArgumentsAndNegativeCosts()
    {
        var solver = new PuzzleSolver();
        var domain = new NumberLine(10, 6, leapCost: -1);

        Assert.Throws<ArgumentNullException>(() => solver.SolveDijkstra<NumberLine.Pos, int>(null!, default));
        Assert.Throws<ArgumentNullException>(() => solver.SolveWeightedAStar(domain, domain.InitialState, null!));
        Assert.Throws<InvalidOperationException>(() => solver.SolveDijkstra(domain, domain.InitialState));
    }
}
