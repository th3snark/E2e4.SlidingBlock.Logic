using E2e4.SlidingBlock.Logic.Labs;

namespace E2e4.SlidingBlock.Logic.Test;

public class LaneSlideTests
{
    // The default Rush Hour layout of the engine tests; X is the red car and must reach the right edge.
    private static readonly string[] RushHourMap =
    [
        "BBB..C",
        "..D..C",
        "XXD.EC",
        "F...E.",
        "F.GGG.",
        "HHH...",
    ];

    private const string Symbols = "XBCDEFGH";

    private static LaneSlideDomain<LaneState16, ulong> Rush(int maxStep) =>
        new(LaneVehicle.Parse(RushHourMap, Symbols), 6, 6, 0, 4, maxStep);

    private static IReadOnlyList<LaneMove> Solve(LaneSlideDomain<LaneState16, ulong> d) => new PuzzleSolver().SolveBfs(d, d.InitialState)!.Moves;

    [Fact]
    public void ParseReadsLengthBreadthLaneAndOffset()
    {
        var vehicles = LaneVehicle.Parse(["AA.B", "AA.B", "...B"], "AB");
        Assert.Equal(new LaneVehicle(0, false, 2, 2, 0, 0), vehicles[0]);
        Assert.Equal(new LaneVehicle(1, false, 3, 1, 3, 0), vehicles[1]);
    }

    [Fact]
    public void ParseRejectsAMissingOrNonRectangularVehicle()
    {
        Assert.Throws<ArgumentException>(() => LaneVehicle.Parse(["AA", "A."], "A"));
        Assert.Throws<ArgumentException>(() => LaneVehicle.Parse(["AA", ".."], "AB"));
    }

    /// <summary>Values measured on the original 3-bit Rush Hour domain before it was rebuilt on <see cref="LaneSlideDomain{TState,TBitboard}"/>.</summary>
    [Theory]
    [InlineData("lab", 17723, 140932, 6071, 9, 7)]
    [InlineData("default", 7272, 57962, 452, 7, 13)]
    public void ClassicRushHourHasTheStateSpaceOfTheOriginalDomain(string which, int states, int edges, int goals, int firstMoves, int solution)
    {
        var d = which == "lab" ? LabPuzzles.CreateRushHour() : TestPuzzles.RushHourDefault();
        Assert.Equal(1, d.MaxStep);
        var seen = new HashSet<LaneState16> { d.InitialState };
        var queue = new Queue<LaneState16>();
        queue.Enqueue(d.InitialState);
        var buffer = new LaneMove[d.MaxMoves];
        int edgeCount = 0, goalCount = 0;
        while (queue.Count > 0)
        {
            var s = queue.Dequeue();
            if (d.IsGoal(s)) goalCount++;
            int n = d.GenerateMoves(s, buffer);
            edgeCount += n;
            for (int i = 0; i < n; i++)
            {
                var next = d.ApplyMove(s, buffer[i]);
                Assert.Equal(1, Math.Abs(buffer[i].Offset - s.OffsetOf(buffer[i].Vehicle)));
                if (seen.Add(next)) queue.Enqueue(next);
            }
        }
        Assert.Equal((states, edges, goals), (seen.Count, edgeCount, goalCount));
        Assert.Equal(firstMoves, d.GenerateMoves(d.InitialState, buffer));
        Assert.Equal(solution, Solve(d).Count);
    }


    [Fact]
    public void ASlideOfAnyDistanceIsOneMoveSoThePuzzleTakesFewer()
    {
        var steps = Solve(Rush(1));
        var d = Rush(int.MaxValue);
        var slides = Solve(d);
        Assert.True(slides.Count < steps.Count);
        var s = d.InitialState;
        foreach (var m in slides) s = d.ApplyMove(s, m);
        Assert.True(d.IsGoal(s));
    }

    [Fact]
    public void ReachableOffsetsMatchTheGeneratedMovesAndStopAtVehicles()
    {
        var d = Rush(int.MaxValue);
        var buffer = new LaneMove[d.MaxMoves];
        int n = d.GenerateMoves(d.InitialState, buffer);
        var generated = buffer.AsSpan(0, n).ToArray().Select(m => (m.Vehicle, (int)m.Offset)).Order().ToList();
        var listed = Enumerable.Range(0, d.Vehicles.Count)
            .SelectMany(v => d.ReachableOffsets(d.InitialState, v).Select(o => ((byte)v, o))).Order().ToList();
        Assert.Equal(generated, listed);
        // The red car (row 2, columns 0-1) is blocked by D in column 2.
        Assert.Empty(d.ReachableOffsets(d.InitialState, 0));
    }

    [Fact]
    public void ATruckTwoCellsBroadBlocksTheLaneBesideIt()
    {
        var d = new LaneSlideDomain<LaneState16, ulong>(LaneVehicle.Parse(["TTT", "TTT", "...", "C..", "C.."], "TC"), 3, 5, 1, 0);
        Assert.True(d.Vehicles[0].Breadth == 2 && d.Vehicles[0].IsHorizontal);
        Assert.Equal([2], d.ReachableOffsets(d.InitialState, 1).ToArray());   // the car can slide up one cell and no further: the truck fills rows 0 and 1
        Assert.Empty(d.ReachableOffsets(d.InitialState, 0));
    }

    [Fact]
    public void ManyVehiclesOnABigBoardFitAndStatesHashAndCompareByPosition()
    {
        var rows = Enumerable.Range(0, 12).Select(r => $"{(char)('a' + r)}{(char)('a' + r)}........").ToArray();
        var d = new LaneSlideDomain<LaneState64, UInt128>(LaneVehicle.Parse(rows, "abcdefghijkl"), 10, 12, 0, 8);
        var a = d.ApplyMove(d.InitialState, new LaneMove(11, 5));
        var b = d.ApplyMove(a, new LaneMove(11, 0));
        Assert.Equal(d.InitialState, b);
        Assert.Equal(d.GetHashCode64(d.InitialState), d.GetHashCode64(b));
        Assert.NotEqual(d.GetHashCode64(d.InitialState), d.GetHashCode64(a));
        Assert.Equal(5, a.OffsetOf(11));
    }

    [Fact]
    public void RejectsBadArguments()
    {
        var one = LaneVehicle.Parse(["AA."], "A");
        Assert.Throws<ArgumentOutOfRangeException>(() => new LaneSlideDomain<LaneState16, ulong>(one, 3, 1, 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LaneSlideDomain<LaneState16, ulong>(one, 3, 1, 0, 1, maxStep: 0));
        Assert.Throws<ArgumentException>(() => new LaneSlideDomain<LaneState16, ulong>([.. Enumerable.Range(0, 65).Select(i => new LaneVehicle(i, true, 1, 1, 0, 0))], 1, 1, 0, 0));
    }
}
