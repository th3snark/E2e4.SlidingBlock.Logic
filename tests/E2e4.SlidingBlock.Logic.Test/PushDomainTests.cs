using E2e4.SlidingBlock.Logic.Text;

namespace E2e4.SlidingBlock.Logic.Test;

public class PushDomainTests
{
    private static readonly string[] Corridor =
    [
        "#######",
        "#@1..X#",
        "#######",
    ];

    private static readonly string[] Room =
    [
        "#######",
        "#@....#",
        "#..1X.#",
        "#.....#",
        "#######",
    ];

    private static readonly string[] TwoCases =
    [
        "@.1.X.",
        "..2.X.",
    ];

    private static PushMove Only(PushDomain d, in PushState s, int id, int to) => d.PushesOf(s, id).Single(m => m.To == to);

    [Fact]
    public void ALongPushIsOneMoveAndTheStoremanEndsBesideTheCase()
    {
        var d = PushMap.CreateDomain(Corridor, Fixtures.CaseSymbols);
        var s = d.InitialState;
        Assert.Equal([10, 11, 12], d.PushesOf(s, 0).Select(m => (int)m.To).Order().ToArray());
        var move = Only(d, s, 0, 12);
        Assert.Equal(7 + 4, move.PlayerAt);   // row 1 starts at cell 7: the storeman stands on column 4
        var after = d.ApplyMove(s, move);
        Assert.True(d.IsGoal(after));
        Assert.Equal(1, d.Heuristic(s));
        Assert.Equal(0, d.Heuristic(after));
    }

    [Fact]
    public void TheSolversSolveItInOneMove()
    {
        var d = PushMap.CreateDomain(Corridor, Fixtures.CaseSymbols);
        var bfs = new PuzzleSolver().SolveBfs(d, d.InitialState);
        var astar = new PuzzleSolver().SolveAStar(d, d.InitialState, s => d.Heuristic(s));
        Assert.Single(bfs.Moves);
        Assert.Single(astar.Moves);
    }

    [Fact]
    public void DeadSquaresAreWhereNoPushCanEverTakeACaseToAPlace()
    {
        var d = PushMap.CreateDomain(Room, Fixtures.CaseSymbols);
        var alive = Enumerable.Range(0, d.CellCount).Where(c => d.IsFloor(c) && d.IsAlive(c)).ToArray();
        // Only row 2, columns 2 to 4 (cells 16-18): the walls leave nowhere to stand to push a case off any other cell towards the place.
        Assert.Equal([16, 17, 18], alive);
        Assert.Equal(0, d.PushDistance(18));
        Assert.Equal(1, d.PushDistance(17));
        Assert.Equal(2, d.PushDistance(16));
        Assert.Equal(-1, d.PushDistance(8));
    }

    [Fact]
    public void PruningLeavesOutPushesOntoDeadSquaresButNotTheOnesTheStoremanMayMake()
    {
        var pruned = PushMap.CreateDomain(Room, Fixtures.CaseSymbols, pruneDeadSquares: true);
        var all = PushMap.CreateDomain(Room, Fixtures.CaseSymbols, pruneDeadSquares: false);
        var a = new PushMove[pruned.MaxMoves];
        var b = new PushMove[all.MaxMoves];
        int kept = pruned.GenerateMoves(pruned.InitialState, a);
        int total = all.GenerateMoves(all.InitialState, b);
        Assert.True(kept < total);
        Assert.All(a.AsSpan(0, kept).ToArray(), m => Assert.True(pruned.IsAlive(m.To)));
        // The player's own pushes are never pruned.
        Assert.Equal(total, pruned.PushesOf(pruned.InitialState, 0).Count);
    }

    [Fact]
    public void PositionsAreTheSameWhateverTheOrderOfPushesAndWhereverTheStoremanStandsInHisRegion()
    {
        var d = PushMap.CreateDomain(TwoCases, Fixtures.CaseSymbols);
        var s = d.InitialState;
        var upperFirst = d.ApplyMove(s, Only(d, s, 0, 4));
        upperFirst = d.ApplyMove(upperFirst, Only(d, upperFirst, 1, 10));
        var lowerFirst = d.ApplyMove(s, Only(d, s, 1, 10));
        lowerFirst = d.ApplyMove(lowerFirst, Only(d, lowerFirst, 0, 4));
        Assert.NotEqual(upperFirst.Player, lowerFirst.Player);
        Assert.Equal(upperFirst, lowerFirst);
        Assert.Equal(d.GetHashCode64(upperFirst), d.GetHashCode64(lowerFirst));
        Assert.NotEqual(s, upperFirst);
        Assert.True(d.IsGoal(upperFirst));
    }

    [Theory]
    [InlineData("1.X")]
    [InlineData("@.X")]
    [InlineData("@1X?")]
    [InlineData("@1.XX")]
    [InlineData("@2.X")]
    public void RejectsABadMap(string row)
    {
        Assert.Throws<ArgumentException>(() => PushMap.CreateDomain([row], Fixtures.CaseSymbols));
    }
}
