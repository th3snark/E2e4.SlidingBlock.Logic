
namespace E2e4.SlidingBlock.Logic.Test;

internal static class Lab3Helpers
{
    public static CompositeMove[] MovesOf(Fixtures.Mode mode, PolyominoState<ulong> s, int pieceId)
    {
        var buffer = new CompositeMove[mode.Domain.MaxMoves];
        int n = mode.Domain.GenerateMoves(s, buffer);
        return buffer.Take(n).Where(m => m.AsSpan()[0].PieceId == pieceId).ToArray();
    }

    public static PolyominoState<ulong> Play(Fixtures.Mode mode, PolyominoState<ulong> s, int pieceId, int direction) =>
        mode.Domain.ApplyMove(s, MovesOf(mode, s, pieceId).Single(m => m.DirectionIndex == direction));

    public static void AssertSolvable(Fixtures.Mode mode)
    {
        var result = new PuzzleSolver().SolveBfs(mode.Domain, mode.Domain.InitialState);
        Assert.NotNull(result);
        var s = mode.Domain.InitialState;
        foreach (var m in result.Moves) s = mode.Domain.ApplyMove(s, m);
        Assert.True(mode.Domain.IsGoal(s));
    }
}

public class Lab3RotationTests
{
    private const int Master = 0, BlockerA = 1;

    private static CompositeMove? Rotation(Fixtures.Mode mode, PolyominoState<ulong> s, bool clockwise)
    {
        var g = mode.Domain.Topology.Groups[s.GroupOf(Master)];
        int next = clockwise ? g.NextCwGroupId : g.NextCcwGroupId;
        return Lab3Helpers.MovesOf(mode, s, Master).Where(m => m.DirectionIndex == -1 && m.AsSpan()[0].NewGroupId == next).Cast<CompositeMove?>().FirstOrDefault();
    }

    [Fact]
    public void BlockerInTheEmptyCornerBlocksBothTurns()
    {
        var mode = Fixtures.CreateRotation();
        var s = mode.Domain.InitialState;
        Assert.Equal(7, s.AnchorOf(Master));
        Assert.Equal(8, s.AnchorOf(BlockerA));
        Assert.Equal(1UL << 8, s.OccupancyMask & (0xC3UL << 7) & ~(0xC1UL << 7));
        Assert.Null(Rotation(mode, s, true));
        Assert.Null(Rotation(mode, s, false));
    }

    [Fact]
    public void FullTurnWalksTheRingAndShiftsTheAnchorOnTheFourthOrientation()
    {
        var mode = Fixtures.CreateRotation();
        var topo = mode.Domain.Topology;
        var s = Lab3Helpers.Play(mode, mode.Domain.InitialState, BlockerA, 1);   // blocker A east: 8 -> 9
        Assert.Equal(9, s.AnchorOf(BlockerA));
        var start = s;

        int[] groups = [1, 2, 3, 0], anchors = [7, 7, 8, 7];
        for (int i = 0; i < 4; i++)
        {
            s = mode.Domain.ApplyMove(s, Rotation(mode, s, true)!.Value);
            Assert.Equal(groups[i], s.GroupOf(Master));
            Assert.Equal(anchors[i], s.AnchorOf(Master));
            Assert.Equal(Cells.HashOf(topo, s), s.ZobristHash);
        }
        Assert.Equal(start, s);
        Assert.Equal(start.ZobristHash, s.ZobristHash);
        for (int g = 0; g < topo.Groups.Count; g++) Assert.Equal(start.GroupAnchorsOf(g), s.GroupAnchorsOf(g));
    }

    [Fact]
    public void RingMasksMatchTheBrief()
    {
        var groups = Fixtures.CreateRotation().Domain.Topology.Groups.Take(4).ToArray();
        Assert.Equal([0xC1UL, 0x43UL, 0x83UL, 0x61UL], groups.Select(g => g.BaseMask));
        Assert.All(groups, g => Assert.Equal(0xC3UL, g.SweptClearanceMask));
        Assert.Equal([0, 0, 0, -1], groups.Select(g => g.BoxOriginDelta));
        Assert.Equal([0, 0, 1, -1], groups.Select(g => g.AnchorDeltaCw));
        Assert.Equal([1, 0, 0, -1], groups.Select(g => g.AnchorDeltaCcw));
    }

    [Fact]
    public void GoalIsSolvable() => Lab3Helpers.AssertSolvable(Fixtures.CreateRotation());
}

public class Lab3CarrierTests
{
    private const int Carrier = 0, CrateA = 1, CrateB = 2, Pusher = 3;

    [Theory]
    [InlineData(1, 2)]   // east: the right arm pushes crate A
    [InlineData(3, 1)]   // south, out of the open mouth: the crate stays behind
    public void CarrierCouplesWithThePocketCrateOnlyWhenItPushesIt(int dir, int count)
    {
        var mode = Fixtures.CreateCarrier();
        var move = Lab3Helpers.MovesOf(mode, mode.Domain.InitialState, Carrier).Single(m => m.DirectionIndex == dir);
        Assert.Equal(count, move.Count);
        var after = mode.Domain.ApplyMove(mode.Domain.InitialState, move);
        Assert.Equal(dir == 3 ? 19 : 14, after.AnchorOf(Carrier));
        Assert.Equal(dir == 3 ? 14 : 15, after.AnchorOf(CrateA));
    }

    [Fact]
    public void WestIsBlockedByTheWallAndUpByTheCrateAboveThePocket()
    {
        var mode = Fixtures.CreateCarrier();
        var moves = Lab3Helpers.MovesOf(mode, mode.Domain.InitialState, Carrier);
        Assert.DoesNotContain(moves, m => m.DirectionIndex == 0);   // the U's floor would cover the wall on bit 18
        Assert.DoesNotContain(moves, m => m.DirectionIndex == 2);   // crate A would be lifted onto crate B, the chain hits the pusher and the edge
    }

    [Fact]
    public void WestCarriesTheCrateBackAndUpCarriesItOnceTheWayIsClear()
    {
        var mode = Fixtures.CreateCarrier();
        var s = Lab3Helpers.Play(mode, mode.Domain.InitialState, Carrier, 1);
        var west = Lab3Helpers.MovesOf(mode, s, Carrier).Single(m => m.DirectionIndex == 0);
        Assert.Equal(2, west.Count);
        s = mode.Domain.ApplyMove(s, west);
        Assert.Equal(mode.Domain.InitialState, s);

        s = Lab3Helpers.Play(mode, Lab3Helpers.Play(mode, s, CrateB, 0), CrateB, 0);   // crate B out of the way: 8 -> 6
        var up = Lab3Helpers.MovesOf(mode, s, Carrier).Single(m => m.DirectionIndex == 2);
        Assert.Equal(2, up.Count);
        s = mode.Domain.ApplyMove(s, up);
        Assert.Equal(7, s.AnchorOf(Carrier));
        Assert.Equal(8, s.AnchorOf(CrateA));
    }

    [Fact]
    public void FourTermHashMatchesRecomputation()
    {
        var mode = Fixtures.CreateCarrier();
        var topo = mode.Domain.Topology;
        var s = mode.Domain.InitialState;
        var move = Lab3Helpers.MovesOf(mode, s, Carrier).Single(m => m.DirectionIndex == 1);
        ulong expected = s.ZobristHash;
        foreach (var d in move.AsSpan()) expected ^= topo.ZobristKey(d.OldGroupId, d.OldAnchor) ^ topo.ZobristKey(d.NewGroupId, d.NewAnchor);
        s = mode.Domain.ApplyMove(s, move);
        Assert.Equal(expected, s.ZobristHash);
        Assert.Equal(Cells.HashOf(topo, s), s.ZobristHash);
    }

    [Fact]
    public void PusherIsBlockedWhileTheChainEndsInTheCarrier() =>
        Assert.DoesNotContain(Lab3Helpers.MovesOf(Fixtures.CreateCarrier(), Fixtures.CreateCarrier().Domain.InitialState, Pusher), m => m.DirectionIndex == 3);

    [Fact]
    public void GoalIsSolvable() => Lab3Helpers.AssertSolvable(Fixtures.CreateCarrier());
}

public class Lab3IceTests
{
    [Fact]
    public void GliderStopsOnlyBesideTheBumperWalls()
    {
        var mode = Fixtures.CreateIce();
        var results = RaycastReport.For(mode.Goal, mode.Domain.InitialState, 0);

        var right = results.Single(r => r.DirectionName == "Right");
        Assert.Equal((3, 3, "Static Bumper Wall", 1UL << 4), (right.TerminalAnchor, right.StepCount, right.StopReason, right.BlockingBitsMask));
        var down = results.Single(r => r.DirectionName == "Down");
        Assert.Equal((6, 1, "Static Bumper Wall", 1UL << 12), (down.TerminalAnchor, down.StepCount, down.StopReason, down.BlockingBitsMask));
        Assert.Equal(0, results.Single(r => r.DirectionName == "Left").StepCount);
        Assert.Equal("Board Edge", results.Single(r => r.DirectionName == "Up").StopReason);

        var targets = Lab3Helpers.MovesOf(mode, mode.Domain.InitialState, 0).Select(m => (int)m.AsSpan()[0].NewAnchor).Order().ToArray();
        Assert.Equal([3, 6], targets);   // squares 1 and 2 are never offered
    }

    [Fact]
    public void PieceCollisionIsReportedAsSuch()
    {
        var mode = Fixtures.CreateIce();
        var slide = RaycastReport.For(mode.Goal, mode.Domain.InitialState, 1).Single(r => r.DirectionName == "Right");   // bar on 19 runs into the L-block on 21
        Assert.Equal((20, "Piece Collision"), (slide.TerminalAnchor, slide.StopReason));
    }

    [Fact]
    public void GoalIsSolvable() => Lab3Helpers.AssertSolvable(Fixtures.CreateIce());
}
