using System.Numerics;

namespace E2e4.SlidingBlock.Logic.Test;

public class ShuntingTests
{
    private static ShuntingTrack Line(int slots)
    {
        var b = new ShuntingTrackBuilder(slots);
        for (int i = 0; i + 1 < slots; i++) b.Connect(i, 1, i + 1, 0);
        return b.Build();
    }

    private static ShuntingGroup Engine(int slot, int front = 1) => new("E", true, [slot]) { StartFronts = [front] };

    private static ShuntingGoal WagonAt(int slot) => new([new ShuntingGroupGoal(1, [slot])]);

    private static ShuntingDomain<ulong> LineDomain(int slots, int engineAt, int wagonAt, int goal, ShuntingMetric metric = ShuntingMetric.EngineReversals) =>
        new(Line(slots), [Engine(engineAt), new("W", false, [wagonAt])], WagonAt(goal), metric);

    private static List<ShuntingMove> Moves(ShuntingDomain<ulong> domain, ShuntingState<ulong> state)
    {
        var buffer = new ShuntingMove[domain.MaxMoves];
        int n = domain.GenerateMoves(state, buffer);
        return [.. buffer.Take(n)];
    }

    [Fact]
    public void AnEnginePushesTheWagonAheadAndMayPullItBehind()
    {
        var domain = LineDomain(5, engineAt: 1, wagonAt: 2, goal: 4);
        var start = domain.InitialState;

        var moves = Moves(domain, start);

        Assert.Equal(3, moves.Count);   // lead 0 alone, lead 0 pulling the wagon, lead 1 pushing it
        var push = moves.Single(m => m.Lead == 1);
        var afterPush = domain.ApplyMove(start, push);
        Assert.Equal(1UL << 2, afterPush.GroupSlots(0));
        Assert.Equal(1UL << 3, afterPush.GroupSlots(1));

        var pull = moves.Single(m => m.Lead == 0 && m.RearLength == 1);
        var afterPull = domain.ApplyMove(start, pull);
        Assert.Equal(1UL << 0, afterPull.GroupSlots(0));
        Assert.Equal(1UL << 1, afterPull.GroupSlots(1));

        var leave = moves.Single(m => m.Lead == 0 && m.RearLength == 0);
        Assert.Equal(1UL << 2, domain.ApplyMove(start, leave).GroupSlots(1));
    }

    [Fact]
    public void ADeadEndBlocksAPush()
    {
        var domain = LineDomain(5, engineAt: 3, wagonAt: 4, goal: 0);

        Assert.DoesNotContain(Moves(domain, domain.InitialState), m => m.Lead == 1);
    }

    [Fact]
    public void AClearanceMaskKeepsAPieceOutOfASlot()
    {
        var domain = new ShuntingDomain<ulong>(Line(5), [Engine(2), new("W", false, [3]) { Allowed = [0, 1, 2, 3] }], WagonAt(0));

        Assert.DoesNotContain(Moves(domain, domain.InitialState), m => m.Lead == 1);
    }

    [Fact]
    public void ASwitchGivesOneMovePerBranchAndPushesIntoTheChosenOne()
    {
        // 0 - 1, and the end of 1 forks to the main line (2) and a siding (3).
        var track = new ShuntingTrackBuilder(4).Connect(0, 1, 1, 0).Connect(1, 1, 2, 0).Connect(1, 1, 3, 0).Build();
        var domain = new ShuntingDomain<ulong>(track, [Engine(0), new("W", false, [1])], WagonAt(3));

        var moves = Moves(domain, domain.InitialState);

        Assert.Equal(2, moves.Count);
        var states = moves.Select(m => domain.ApplyMove(domain.InitialState, m)).ToList();
        Assert.Contains(states, s => s.GroupSlots(1) == 1UL << 2);
        Assert.Contains(states, s => s.GroupSlots(1) == 1UL << 3);

        var solution = new PuzzleSolver().SolveDijkstra(domain, domain.InitialState);
        Assert.Equal(1, ShuntingCost.Primary(solution!.Cost));
        Assert.Equal(1, ShuntingCost.Steps(solution.Cost));
    }

    [Fact]
    public void ARunCostsOnceHoweverFarItGoesAndAReversalCostsAgain()
    {
        var domain = LineDomain(6, engineAt: 0, wagonAt: 1, goal: 5);

        var solution = new PuzzleSolver().SolveDijkstra(domain, domain.InitialState);

        Assert.Equal(1, ShuntingCost.Primary(solution!.Cost));
        Assert.Equal(4, ShuntingCost.Steps(solution.Cost));

        var first = Moves(domain, domain.InitialState).Single();
        var moved = domain.ApplyMove(domain.InitialState, first);
        Assert.Equal(ShuntingCost.Pack(1, 1, 1), domain.MoveCost(domain.InitialState, first));
        Assert.Equal(0, moved.ActiveEngine);
        var carryOn = Moves(domain, moved).Single(m => m.Lead == 1);
        var back = Moves(domain, moved).First(m => m.Lead == 0);
        Assert.Equal(ShuntingCost.Pack(0, 0, 1), domain.MoveCost(moved, carryOn));
        Assert.Equal(ShuntingCost.Pack(1, 1, 1), domain.MoveCost(moved, back));
    }

    [Fact]
    public void TheTurntableMetricOnlyChargesSecondaryReversalsForEngineRuns()
    {
        var domain = LineDomain(6, engineAt: 0, wagonAt: 1, goal: 5, ShuntingMetric.TurntableRotations);
        var first = Moves(domain, domain.InitialState).Single();

        Assert.Equal(ShuntingCost.Pack(0, 0, 1), domain.MoveCost(domain.InitialState, first));
        var moved = domain.ApplyMove(domain.InitialState, first);
        var back = Moves(domain, moved).First(m => m.Lead == 0);
        Assert.Equal(ShuntingCost.Pack(0, 1, 1), domain.MoveCost(moved, back));
    }

    [Fact]
    public void TheDirectionChangeMetricDoesNotChargeTheFirstStart()
    {
        var domain = LineDomain(6, engineAt: 0, wagonAt: 1, goal: 5, ShuntingMetric.DirectionChanges);
        var first = Moves(domain, domain.InitialState).Single();
        var moved = domain.ApplyMove(domain.InitialState, first);

        Assert.Equal(ShuntingCost.Pack(0, 1, 1), domain.MoveCost(domain.InitialState, first));
        Assert.Equal(ShuntingCost.Pack(0, 0, 1), domain.MoveCost(moved, Moves(domain, moved).Single(m => m.Lead == 1)));
        Assert.Equal(ShuntingCost.Pack(1, 1, 1), domain.MoveCost(moved, Moves(domain, moved).First(m => m.Lead == 0)));
        Assert.Equal(0, ShuntingCost.Primary(new PuzzleSolver().SolveDijkstra(domain, domain.InitialState)!.Cost));
    }

    /// <summary>A stem 0-1 and a switch at the far end of slot 1 to two legs, 2-3 and 4-5, whose first slots foul each other.</summary>
    private static ShuntingTrack FouledSwitch(bool fouling)
    {
        var b = new ShuntingTrackBuilder(6).Connect(0, 1, 1, 0).Connect(1, 1, 2, 0).Connect(2, 1, 3, 0).Connect(1, 1, 4, 0).Connect(4, 1, 5, 0);
        if (fouling) b.AddFoulingSet([2, 4]);
        return b.Build();
    }

    [Theory]
    [InlineData(false, new[] { 2, 4 })]
    [InlineData(true, new[] { 2 })]
    public void APieceJustPastASwitchFoulsTheOtherLeg(bool fouling, int[] engineSlots)
    {
        // The wagon stands on the first slot of one leg; the engine at the switch may push it on, but may enter the other leg only without fouling.
        var domain = new ShuntingDomain<ulong>(FouledSwitch(fouling), [Engine(1), new("W", false, [2])], new([]));

        var forward = Moves(domain, domain.InitialState).Where(m => m.Lead == 1);

        Assert.Equal(engineSlots, forward.Select(m => BitOperations.TrailingZeroCount(domain.ApplyMove(domain.InitialState, m).GroupSlots(0))).Order());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ATrainCannotLeaveAPieceJustPastASwitchAndDivertTheRest(bool fouling, bool diverts)
    {
        // A train has pushed wagon V just past the switch with W behind it at the switch: W may not go on into the other leg and leave V there.
        var domain = new ShuntingDomain<ulong>(FouledSwitch(fouling), [Engine(0), new("W", false, [1]), new("V", false, [2])], new([]));
        var start = domain.InitialState;

        Assert.Equal(diverts, Moves(domain, start).Any(m => domain.ApplyMove(start, m).GroupSlots(1) == 1UL << 4));
    }

    [Fact]
    public void AStartThatFoulsIsRejected() =>
        Assert.Throws<ArgumentException>(() => new ShuntingDomain<ulong>(FouledSwitch(true), [Engine(2), new("W", false, [4])], new([])));

    [Fact]
    public void GoalAndEqualityIgnoreWhichIdenticalWagonIsWhich()
    {
        var track = Line(4);
        var a = new ShuntingDomain<ulong>(track, [new("W", false, [0, 1])], new([new ShuntingGroupGoal(0, [0, 1])]));
        var b = new ShuntingDomain<ulong>(track, [new("W", false, [1, 0])], new([new ShuntingGroupGoal(0, [0, 1])]));

        Assert.Equal(a.InitialState, b.InitialState);
        Assert.Equal(a.GetHashCode64(a.InitialState), b.GetHashCode64(b.InitialState));
        Assert.True(a.IsGoal(a.InitialState));
    }

    [Fact]
    public void AWyeChordTurnsTheEngineRoundButStraightTrackDoesNot()
    {
        var wye = new ShuntingTrackBuilder(2).Connect(0, 1, 1, 1).Build();
        var straight = new ShuntingTrackBuilder(2).Connect(0, 1, 1, 0).Build();

        foreach (var (track, expectedBit) in new[] { (wye, 0UL), (straight, 1UL << 1) })
        {
            var domain = new ShuntingDomain<ulong>(track, [Engine(0)], new([]));
            var move = Moves(domain, domain.InitialState).Single(m => m.Lead == 1);
            var after = domain.ApplyMove(domain.InitialState, move);
            Assert.Equal(expectedBit, after.Heading);
        }
    }

    [Fact]
    public void ATurnaroundGoalNeedsTheHeading()
    {
        var wye = new ShuntingTrackBuilder(2).Connect(0, 1, 1, 1).Build();
        var goal = new ShuntingGoal([]) { Fronts = [new ShuntingFrontGoal(1, 0)] };
        var domain = new ShuntingDomain<ulong>(wye, [Engine(0)], goal);

        var solution = new PuzzleSolver().SolveDijkstra(domain, domain.InitialState);

        Assert.Single(solution!.Moves);
    }

    [Fact]
    public void ATurntableTurnReversesTheBridgeFlipsHeadingsAndStopsTheEngine()
    {
        // 0 - [1 2] bridge - 3; turned the other way round, the bridge joins the same outer slots.
        var b = new ShuntingTrackBuilder(4);
        int t = b.AddTurntable([1, 2], [false, true]);
        var track = b.Connect(1, 1, 2, 0)
            .ConnectWhen(t, 0, 0, 1, 1, 0).ConnectWhen(t, 0, 2, 1, 3, 0)
            .ConnectWhen(t, 1, 0, 1, 2, 1).ConnectWhen(t, 1, 1, 0, 3, 0)
            .Build();
        var domain = new ShuntingDomain<ulong>(track, [Engine(1, front: 1), new("W", false, [2])], new([]));
        var start = domain.InitialState;

        var turn = Moves(domain, start).Single(m => m.IsTurn);
        var after = domain.ApplyMove(start, turn);

        Assert.Equal(1, after.Alignment(0));
        Assert.Equal(1UL << 2, after.GroupSlots(0));
        Assert.Equal(1UL << 1, after.GroupSlots(1));
        Assert.Equal(0UL, after.Heading);   // the engine faced end 1 before the turn; turning the bridge round flips that
        Assert.Equal(-1, after.ActiveEngine);
        Assert.Equal(ShuntingCost.Pack(1, 0, 1), domain.MoveCost(start, turn));
        Assert.Contains(Moves(domain, after), m => m.IsTurn && m.Alignment == 0);
    }

    [Fact]
    public void RejectsBadDefinitions()
    {
        var track = Line(3);
        var goal = new ShuntingGoal([]);

        Assert.Throws<ArgumentNullException>(() => new ShuntingDomain<ulong>(null!, [Engine(0)], goal));
        Assert.Throws<ArgumentException>(() => new ShuntingDomain<ulong>(track, [], goal));
        Assert.Throws<ArgumentException>(() => new ShuntingDomain<ulong>(track, [new("E", true, [0, 1])], goal));
        Assert.Throws<ArgumentException>(() => new ShuntingDomain<ulong>(track, [Engine(0), new("W", false, [0])], goal));
        Assert.Throws<ArgumentException>(() => new ShuntingDomain<ulong>(track, [Engine(7)], goal));
        Assert.Throws<ArgumentException>(() => new ShuntingDomain<ulong>(track, [Engine(0, front: 2)], goal));
        Assert.Throws<ArgumentException>(() => new ShuntingDomain<ulong>(track, [Engine(0)], new([new ShuntingGroupGoal(3, [0])])));
        Assert.Throws<ArgumentException>(() => new ShuntingDomain<ulong>(Line(70), [Engine(0)], goal));
    }
}
