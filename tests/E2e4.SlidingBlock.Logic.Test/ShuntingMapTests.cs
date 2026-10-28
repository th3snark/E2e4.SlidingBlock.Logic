using E2e4.SlidingBlock.Logic.Text;

namespace E2e4.SlidingBlock.Logic.Test;

public class ShuntingMapTests
{
    // An engine behind a wagon at a fork: the wagon is to go into the siding (slot 3).
    private static readonly string[] Fork =
    [
        "; engine E, wagon W",
        "track 4",
        "join 0.1 1.0",
        "join 1.1 2.0",
        "join 1.1 3.0",
        "start EW..",
        "goal ...W",
    ];

    [Fact]
    public void ReadsAForkAndTheDomainSolvesIt()
    {
        var domain = ShuntingMap.CreateDomain<ulong>(Fork, "EW", engines: "E");

        var solution = new PuzzleSolver().SolveDijkstra(domain, domain.InitialState);

        Assert.Equal(1, ShuntingCost.Primary(solution!.Cost));
        Assert.Equal(1, ShuntingCost.Steps(solution.Cost));
        Assert.Equal(1UL << 3, domain.ApplyMove(domain.InitialState, solution.Moves[0]).GroupSlots(1));
    }

    [Fact]
    public void ReadsTurntablesClearancesAndHeadings()
    {
        string[] map =
        [
            "track 4",
            "turntable 1 0,1",
            "join@ignored 0.1 1.0",
        ];
        Assert.Throws<ArgumentException>(() => ShuntingMap.Parse(map, "EW", "E"));

        string[] good =
        [
            "track 4",
            "turntable 1 0,1",
            "join 0.1 1.0 @0.0",
            "join 2.1 1.0 @0.1",
            "join 1.1 3.0",
            "start .EW.",
            "front E 0",
            "allow W 0,1,2",
            "heading W",
            "faces 1 0",
            "align 1",
        ];

        var layout = ShuntingMap.Parse(good, "EW", "E");

        Assert.Equal(1, layout.Track.TurntableCount);
        Assert.Equal([1], layout.StartAlignments);
        Assert.True(layout.Groups[0].IsEngine);
        Assert.Equal([0], layout.Groups[0].StartFronts);
        Assert.Equal([0, 1, 2], layout.Groups[1].Allowed);
        Assert.True(layout.Groups[1].TrackHeading);
        Assert.Equal([new ShuntingFrontGoal(1, 0)], layout.Goal.Fronts);
        Assert.Equal(1, layout.CreateDomain<ulong>().InitialState.Alignment(0));
    }

    [Theory]
    [InlineData("start EW..")]                                   // no track line first
    [InlineData("track 4\nbogus 1")]                             // unknown keyword
    [InlineData("track 4\njoin 0.1 1")]                          // not slot.end
    [InlineData("track 4\njoin 0.1 9.0")]                        // slot outside the track
    [InlineData("track 4\nstart EW.")]                           // wrong diagram length
    [InlineData("track 4\nstart EX..")]                          // unknown symbol
    [InlineData("track 4\nstart E...")]                          // group W has no piece
    [InlineData("track 4\ntrack 4")]                             // twice
    [InlineData("track 4\nstart EW..\nfront Q 0")]               // not a group
    [InlineData("track 4\nstart EW..\nallow W x")]               // not a number
    public void RejectsBadDescriptions(string text) =>
        Assert.Throws<ArgumentException>(() => ShuntingMap.Parse(text.Split('\n'), "EW", "E"));

    [Fact]
    public void NamesTheOffendingLine()
    {
        var ex = Assert.Throws<ArgumentException>(() => ShuntingMap.Parse(["track 4", "", "oops"], "EW", "E"));

        Assert.StartsWith("Line 3:", ex.Message);
    }

    [Fact]
    public void RejectsBadArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ShuntingMap.Parse(null!, "EW"));
        Assert.Throws<ArgumentException>(() => ShuntingMap.Parse(Fork, ""));
        Assert.Throws<ArgumentException>(() => ShuntingMap.Parse(Fork, "EE"));
        Assert.Throws<ArgumentException>(() => ShuntingMap.Parse(Fork, "E.W"));
    }

    [Fact]
    public void ReadsFoulingSets()
    {
        var layout = ShuntingMap.Parse([.. Fork, "foul 2,3"], "EW", "E");

        Assert.Equal([[2, 3]], layout.Track.FoulingSets);
        Assert.Throws<ArgumentException>(() => ShuntingMap.Parse([.. Fork, "foul 2"], "EW", "E"));
        Assert.Throws<ArgumentException>(() => ShuntingMap.CreateDomain<ulong>([.. Fork, "foul 1,2", "start .WE."], "EW", "E"));
    }

    [Fact]
    public void SeveralGoalLinesLetGroupsShareSlots()
    {
        string[] map =
        [
            "track 3",
            "join 0.1 1.0",
            "join 1.1 2.0",
            "start EW.",
            "goal .EE",
            "goal .WW",
        ];
        var domain = ShuntingMap.CreateDomain<ulong>(map, "EW", engines: "E");

        var solution = new PuzzleSolver().SolveDijkstra(domain, domain.InitialState);

        Assert.Equal(1, ShuntingCost.Primary(solution!.Cost));
    }
}
