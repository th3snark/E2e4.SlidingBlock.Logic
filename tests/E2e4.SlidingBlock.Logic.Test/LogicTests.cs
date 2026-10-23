using System.Numerics;
using E2e4.SlidingBlock.Logic;
using E2e4.SlidingBlock.Logic.Labs;

namespace E2e4.SlidingBlock.Logic.Test;

public class LsbNormalizationTests
{
    private static readonly Dictionary<string, (int, int)[]> Shapes = new()
    {
        ["L"] = [(0, 0), (1, 0), (1, 1)],
        ["T"] = [(0, 0), (1, -1), (1, 0), (1, 1)],
        ["J"] = [(0, 0), (1, -1), (1, 0)],
        ["S"] = [(0, 1), (0, 2), (1, 0), (1, 1)],
        ["Z"] = [(0, 0), (0, 1), (1, 1), (1, 2)],
        ["U"] = [(0, 0), (0, 2), (1, 0), (1, 1), (1, 2)],
    };

    [Theory]
    [InlineData("L", 0x61UL)]
    [InlineData("T", 0x71UL)]
    [InlineData("J", 0x31UL)]
    public void KnownBaseMasksOnFiveWideBoard(string shape, ulong expected)
    {
        var (mask, _) = PuzzleTopologyBuilder.Normalize(Shapes[shape], 5);
        Assert.Equal(expected, mask);
    }

    [Theory]
    [InlineData("L")]
    [InlineData("T")]
    [InlineData("J")]
    [InlineData("S")]
    [InlineData("Z")]
    [InlineData("U")]
    public void AllOffsetsAreNonNegativeAndBitZeroIsSet(string shape)
    {
        for (int width = 3; width <= 8; width++)
        {
            var (mask, rel) = PuzzleTopologyBuilder.Normalize(Shapes[shape], width);
            Assert.Equal(1UL, mask & 1UL);
            Assert.Equal(0, BitOperations.TrailingZeroCount(mask));
            Assert.Equal((0, 0), rel[0]);
            foreach (var (dr, dc) in rel)
            {
                int bit = dr * width + dc;
                Assert.True(bit >= 0, $"{shape} width {width}: ({dr},{dc}) -> {bit}");
                Assert.True(((mask >> bit) & 1UL) != 0);
                if (dr == 0) Assert.True(dc >= 0);
                else Assert.True(dr >= 1);
            }
            Assert.Equal(rel.Count, BitOperations.PopCount(mask));
        }
    }

    [Fact]
    public void AnchorIsTopStemNotBoundingBoxCorner()
    {
        var d = LabPuzzles.CreateLab2();
        var s = d.InitialState;
        Assert.Equal(16, BitOperations.TrailingZeroCount(d.Topology.PieceMask(1, s.AnchorOf(1))));
        Assert.Equal(4, BitOperations.TrailingZeroCount(d.Topology.PieceMask(2, s.AnchorOf(2))));
    }
}

public class ValidAnchorsTests
{
    [Fact]
    public void TAndJCannotAnchorInColumnZero()
    {
        var topo = LabPuzzles.CreateLab2().Topology;
        foreach (int g in new[] { 1, 2 })
            for (int r = 0; r < 5; r++)
                Assert.False(((topo.Groups[g].ValidAnchorsMask >> (r * 5)) & 1UL) != 0, $"group {g} row {r}");
    }

    [Fact]
    public void ValidAnchorsMatchTwoDimensionalGroundTruthAndNeverWrap()
    {
        var topo = LabPuzzles.CreateLab2().Topology;
        foreach (var g in topo.Groups)
            for (int s = 0; s < 25; s++)
            {
                int r = s / 5, c = s % 5;
                bool inside = g.RelativeCells.All(x =>
                    r + x.dRow is >= 0 and < 5 && c + x.dCol is >= 0 and < 5 && (r + x.dRow) * 5 + c + x.dCol != 12);
                Assert.Equal(inside, ((g.ValidAnchorsMask >> s) & 1UL) != 0);
                if (inside)
                {
                    // The shifted bit pattern must equal the 2D cells (no row wrap-around).
                    ulong expected = g.RelativeCells.Aggregate(0UL, (m, x) => m | 1UL << ((r + x.dRow) * 5 + c + x.dCol));
                    Assert.Equal(expected, g.BaseMask << s);
                }
            }
    }
}

public class ReachableAnchorsTests
{
    private const int W = 3;
    private const ulong All9 = (1UL << 9) - 1;

    [Fact]
    public void FindsCornerHookDestinations()
    {
        // 1x1 piece at (0,0), block at (0,1): (1,1) is reachable only by going down then right.
        ulong reach = PolyominoState.FindReachableAnchors(0, 1UL, All9, 1UL << 1, W);
        Assert.True((reach >> 4 & 1) != 0, "hook destination (1,1)");
        Assert.True((reach >> 3 & 1) != 0);
        Assert.True((reach >> 8 & 1) != 0);
        Assert.False((reach >> 1 & 1) != 0, "blocked cell");
        Assert.False((reach & 1) != 0, "start excluded");
    }

    [Fact]
    public void RejectsBlockedCorners()
    {
        ulong reach = PolyominoState.FindReachableAnchors(0, 1UL, All9, (1UL << 1) | (1UL << 3), W);
        Assert.Equal(0UL, reach);
    }

    [Fact]
    public void DoesNotWrapAcrossRows()
    {
        // 3-wide board, piece at (0,2); +1 shift would wrap to (1,0) which must not be reached directly.
        ulong reach = PolyominoState.FindReachableAnchors(2, 1UL, All9, (1UL << 5) | (1UL << 1), W);
        Assert.Equal(0UL, reach);
    }

    [Fact]
    public void RespectsAllowedDirections()
    {
        ulong horiz = PolyominoState.FindReachableAnchors(0, 1UL, All9, 0, W, AllowedDirections.Horizontal);
        Assert.Equal(0b110UL, horiz);
    }

    [Fact]
    public void HookPathIsContiguousAndAvoidsObstacles()
    {
        var path = PolyominoState.ReconstructHookPath(0, 4, 1UL, All9, 1UL << 1, W);
        Assert.Equal(new[] { 0, 3, 4 }, path);
        Assert.Empty(PolyominoState.ReconstructHookPath(0, 4, 1UL, All9, (1UL << 1) | (1UL << 3), W));
    }

    [Fact]
    public void MultiCellPieceHooksAroundObstaclesOnLabBoard()
    {
        var d = LabPuzzles.CreateLab2();
        var s = d.InitialState;
        for (int p = 0; p < 5; p++)
        {
            var g = d.Topology.Groups[d.Topology.Pieces[p].GroupId];
            int a = s.AnchorOf(p);
            ulong other = s.OccupancyMask ^ (g.BaseMask << a);
            ulong reach = d.ReachableFor(s, p);
            for (ulong r = reach; r != 0; r &= r - 1)
            {
                int target = BitOperations.TrailingZeroCount(r);
                var path = PolyominoState.ReconstructHookPath(a, target, g.BaseMask, g.ValidAnchorsMask, other, 5);
                Assert.Equal(a, path[0]);
                Assert.Equal(target, path[^1]);
                foreach (int step in path) Assert.Equal(0UL, (g.BaseMask << step) & other);
                for (int i = 1; i < path.Count; i++)
                {
                    int diff = Math.Abs(path[i] - path[i - 1]);
                    Assert.True(diff == 1 || diff == 5);
                    if (diff == 1) Assert.Equal(path[i] / 5, path[i - 1] / 5);
                }
            }
        }
    }
}

public class SymmetryTests
{
    [Fact]
    public void SwappingIdenticalPiecesKeepsGroupAnchorsAndHash()
    {
        var d = LabPuzzles.CreateLab2();
        var s = d.InitialState;
        int a = s.AnchorOf(3), b = s.AnchorOf(4);
        // Swap via two moves through a temporary state: set A->B's square is occupied, so rebuild directly.
        var topo = d.Topology;
        var swapped = s.WithPieceMoved(topo, 3, 24).WithPieceMoved(topo, 4, a).WithPieceMoved(topo, 3, b);
        Assert.Equal(b, swapped.AnchorOf(3));
        Assert.Equal(a, swapped.AnchorOf(4));
        Assert.Equal(s.GroupAnchorsOf(3), swapped.GroupAnchorsOf(3));
        Assert.Equal(s.ZobristHash, swapped.ZobristHash);
        Assert.Equal(s.OccupancyMask, swapped.OccupancyMask);
        Assert.True(s.Equals(swapped));
        Assert.Equal(s.GetHashCode(), swapped.GetHashCode());
    }

    [Fact]
    public void IncrementalZobristMatchesFullRecompute()
    {
        var d = LabPuzzles.CreateLab2();
        var s = d.InitialState;
        var buf = new PolyominoMove[d.MaxMoves];
        var rng = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            int n = d.GenerateMoves(s, buf);
            s = d.ApplyMove(s, buf[rng.Next(n)]);
            ulong h = 0, occ = 0;
            foreach (var p in d.Topology.Pieces)
            {
                h ^= d.Topology.ZobristTable[p.GroupId, s.AnchorOf(p.PieceId)];
                occ |= d.Topology.PieceMask(p.PieceId, s.AnchorOf(p.PieceId));
            }
            Assert.Equal(h, s.ZobristHash);
            Assert.Equal(occ, s.OccupancyMask);
        }
    }

    [Fact]
    public void ZobristTableIsDeterministic()
    {
        var a = LabPuzzles.CreateLab2().Topology.ZobristTable;
        var b = LabPuzzles.CreateLab2().Topology.ZobristTable;
        Assert.Equal(a.Cast<ulong>(), b.Cast<ulong>());
    }
}

/// <summary>Layouts only the engine tests need (the Lab layouts come from <see cref="LabPuzzles"/>).</summary>
internal static class TestPuzzles
{
    private static readonly string[] RushHourDefaultMap =
    [
        "BBB..C",
        "..D..C",
        "XXD.EC",
        "F...E.",
        "F.GGG.",
        "HHH...",
    ];

    public static RushHourDomain RushHourDefault()
    {
        var topo = RushHourTopology.FromAsciiMap(RushHourDefaultMap, leadingLabels: "X");
        return new RushHourDomain(topo, topo.IndexOf('X'), RushHourTopology.Size - 2);
    }
}

public class DomainTests
{
    [Fact]
    public void NPuzzleSlideIsNibbleXorSwap()
    {
        var domain = LabPuzzles.CreatePuzzle15();
        var s = domain.InitialState;
        Assert.Equal(0, s.BlankIndex);
        var m = s.SlideTileIntoBlank(1);
        Assert.Equal(1, m.BlankIndex);
        Assert.Equal(1, m.TileAt(0));
        Assert.Equal(s, m.SlideTileIntoBlank(0));
        Assert.Equal(0, domain.ManhattanDistance(s));
        Assert.Equal(1, domain.ManhattanDistance(m));
    }

    [Fact]
    public void NPuzzleGoalIsSuppliedByTheCaller()
    {
        // Same board, goal with the blank in the bottom-right corner: the engine has no opinion on where the blank belongs.
        var goal = new NPuzzleState(0x0FEDCBA987654321UL);
        var domain = new NPuzzleDomain(goal, goal);
        Assert.Equal(15, goal.BlankIndex);
        Assert.True(domain.IsGoal(goal));
        Assert.False(domain.IsGoal(LabPuzzles.Puzzle15Solved));
        Assert.Equal(0, domain.ManhattanDistance(goal));
        var oneAway = goal.SlideTileIntoBlank(14);
        Assert.Equal(1, domain.ManhattanDistance(oneAway));
    }

    [Fact]
    public void RandomWalkStaysReachableAndIsDeterministicPerSeed()
    {
        var domain = LabPuzzles.CreatePuzzle15();
        var a = PuzzleWalk.RandomWalk(domain, domain.Goal, 40, new Random(9));
        var b = PuzzleWalk.RandomWalk(domain, domain.Goal, 40, new Random(9));
        Assert.Equal(a, b);
        Assert.NotEqual(domain.Goal, a);
        Assert.Equal(domain.Goal, PuzzleWalk.RandomWalk(domain, domain.Goal, 0, new Random(9)));
    }

    [Fact]
    public void NPuzzleIdaStarSolvesScramble()
    {
        var solved = LabPuzzles.CreatePuzzle15();
        var start = PuzzleWalk.RandomWalk(solved, solved.Goal, 25, new Random(3));
        var domain = new NPuzzleDomain(start, solved.Goal);
        var result = new PuzzleSolver().SolveIdaStar(domain, start, domain.ManhattanDistance);
        Assert.NotNull(result);
        var s = start;
        foreach (var mv in result!.Moves) s = domain.ApplyMove(s, mv);
        Assert.True(domain.IsGoal(s));
    }

    [Fact]
    public void RushHourDefaultIsSolvableAndOccupancyConsistent()
    {
        var d = TestPuzzles.RushHourDefault();
        var result = new PuzzleSolver().SolveBfs(d, d.InitialState);
        Assert.NotNull(result);
        var s = d.InitialState;
        foreach (var mv in result!.Moves)
        {
            s = d.ApplyMove(s, mv);
            ulong occ = s.Occupancy(d.Topology);
            Assert.Equal(d.Topology.Vehicles.Sum(v => v.Length), BitOperations.PopCount(occ));
        }
        Assert.True(d.IsGoal(s));
    }

    [Fact]
    public void KlotskiClassicHasOptimalSolutionOf116SingleSteps()
    {
        var d = LabPuzzles.CreateKlotski();
        Assert.Equal(20 - 2, BitOperations.PopCount(d.InitialState.OccupancyMask));
        var result = new PuzzleSolver().SolveBfs(d, d.InitialState);
        Assert.NotNull(result);
        Assert.Equal(116, result!.Moves.Count);
    }

    [Fact]
    public void KlotskiSwapOfIdenticalPiecesIsSameState()
    {
        var d = LabPuzzles.CreateKlotski();
        // Pieces 'e' and 'f' are the two adjacent singles in row 3.
        int e = d.Topology.Pieces.First(p => p.Label == "e").PieceId, f = d.Topology.Pieces.First(p => p.Label == "f").PieceId;
        Assert.Equal(d.Topology.Groups[d.Topology.Pieces[e].GroupId], d.Topology.Groups[d.Topology.Pieces[f].GroupId]);
        var s = d.InitialState;
        int ae = s.AnchorOf(e), af = s.AnchorOf(f);
        var swapped = s.WithPieceMoved(d.Topology, e, af).WithPieceMoved(d.Topology, f, ae);
        Assert.Equal(af, swapped.AnchorOf(e));
        Assert.Equal(s.CanonicalShapeKey, swapped.CanonicalShapeKey);
        Assert.True(s.Equals(swapped));
    }

    [Fact]
    public void PolyominoLab2GoalIsSolvable()
    {
        var d = LabPuzzles.CreateLab2();
        var result = new PuzzleSolver().SolveBfs(d, d.InitialState);
        Assert.NotNull(result);
        var s = d.InitialState;
        foreach (var mv in result!.Moves) s = d.ApplyMove(s, mv);
        Assert.True(d.IsGoal(s));
    }
}

public class LabSpecTests
{
    [Fact]
    public void Lab1RushHourLayoutMatchesTheBrief()
    {
        var d = LabPuzzles.CreateRushHour();
        var t = d.Topology;
        Assert.Equal(8, t.VehicleCount);
        (int lane, int offset, int length, bool horizontal)[] expected =
        [
            (2, 1, 2, true), (3, 1, 3, false), (0, 0, 2, true), (0, 3, 2, false),
            (5, 2, 3, true), (5, 0, 2, false), (4, 1, 2, true), (2, 0, 2, false),
        ];
        for (int v = 0; v < 8; v++)
        {
            Assert.Equal(expected[v].lane, t.Vehicles[v].Lane);
            Assert.Equal(expected[v].offset, d.InitialState.OffsetOf(v));
            Assert.Equal(expected[v].length, t.Vehicles[v].Length);
            Assert.Equal(expected[v].horizontal, t.Vehicles[v].IsHorizontal);
        }
        Assert.NotNull(new PuzzleSolver().SolveBfs(d, d.InitialState));
    }

    [Fact]
    public void KlotskiShapeClassesFollowTheBrief()
    {
        var d = LabPuzzles.CreateKlotski();
        Assert.Equal(0x9FFFFUL, d.InitialState.OccupancyMask);
        ulong key = d.InitialState.CanonicalShapeKey;
        int Code(int cell) => (int)((key >> (3 * cell)) & 7);
        Assert.Equal(2, Code(0));  // 1x2 vertical
        Assert.Equal(4, Code(1));  // 2x2 master
        Assert.Equal(3, Code(9));  // 2x1 horizontal
        Assert.Equal(1, Code(13)); // 1x1
        Assert.Equal(0, Code(17)); // empty
    }

    [Fact]
    public void ParentMapFloodFillSplitsStraightFromHookAndRebuildsThePath()
    {
        // 3x3 board, 1x1 piece at 0, blocker at 4: 8 is reachable only by turning a corner.
        const ulong All9 = 0x1FFUL;
        var parent = new sbyte[9];
        ulong reach = PolyominoState.FindReachableAnchors(0, 1UL, All9, 1UL << 4, 3, parent, out ulong straight, out ulong hook);
        Assert.Equal(reach, PolyominoState.FindReachableAnchors(0, 1UL, All9, 1UL << 4, 3));
        Assert.Equal(straight | hook, reach);
        Assert.Equal(0UL, straight & hook);
        Assert.NotEqual(0UL, straight & (1UL << 2)); // same row, no corner
        Assert.NotEqual(0UL, hook & (1UL << 8));     // needs both axes
        var rebuilt = new List<int>();
        for (int a = 8; a != 0; a = parent[a]) rebuilt.Add(a);
        rebuilt.Add(0);
        rebuilt.Reverse();
        Assert.Equal(5, rebuilt.Count); // 0, 1, 2, 5, 8 or 0, 3, 6, 7, 8
        Assert.Equal(PolyominoState.ReconstructHookPath(0, 8, 1UL, All9, 1UL << 4, 3).Count, rebuilt.Count);
    }

    [Fact]
    public void HookDemoPresetNeedsAFourStepHookAroundTheWall()
    {
        var d = LabPuzzles.CreateLab2();
        var t = d.Topology;
        int a = t.Pieces.First(p => p.Label == "A").PieceId, j = t.Pieces.First(p => p.Label == "J").PieceId;
        var s = d.InitialState.WithPieceMoved(t, a, 7).WithPieceMoved(t, j, 19);
        var g = t.Groups[t.Pieces[a].GroupId];
        ulong other = s.OccupancyMask ^ (g.BaseMask << 7);
        var parent = new sbyte[25];
        PolyominoState.FindReachableAnchors(7, g.BaseMask, g.ValidAnchorsMask, other, 5, parent, out _, out ulong hook);
        Assert.NotEqual(0UL, hook & (1UL << 17));
        Assert.Equal([7, 8, 13, 18, 17], PolyominoState.ReconstructHookPath(7, 17, g.BaseMask, g.ValidAnchorsMask, other, 5));
    }

    [Fact]
    public void LabGroupNamesMatchTheBrief()
    {
        var names = LabPuzzles.CreateLab2().Topology.Groups.Select(g => g.Name).ToArray();
        Assert.Equal(["L-Tromino", "Upward T", "J-Tromino", "1x1 Single"], names);
    }
}

public class PlaySessionTests
{
    private static PlaySession<NPuzzleState, NPuzzleMove> NewSession(out NPuzzleDomain domain)
    {
        domain = LabPuzzles.CreatePuzzle15();
        return new PlaySession<NPuzzleState, NPuzzleMove>(domain, PuzzleWalk.RandomWalk(domain, domain.Goal, 12, new Random(5)));
    }

    [Fact]
    public void ApplyUndoAndResetTrackTheHistory()
    {
        var play = NewSession(out var domain);
        var start = play.State;
        Span<NPuzzleMove> buf = stackalloc NPuzzleMove[domain.MaxMoves];
        domain.GenerateMoves(play.State, buf);
        play.Apply(buf[0]);
        Assert.Equal(1, play.MoveCount);
        Assert.True(play.CanUndo);
        Assert.True(play.Undo());
        Assert.Equal(start, play.State);
        Assert.False(play.Undo());
        play.Apply(buf[0]);
        play.Reset();
        Assert.Equal(start, play.State);
        Assert.Equal(0, play.MoveCount);
    }
}

public class PolyominoGoalPositionTests
{
    [Fact]
    public void GoalCanBeAWholePositionInWhichIdenticalPiecesAreInterchangeable()
    {
        var lab = LabPuzzles.CreateLab2();
        var topo = lab.Topology;
        var domain = new PolyominoDomain(topo, lab.InitialState);
        Assert.NotNull(domain.GoalPosition);
        Assert.Equal(-1, domain.GoalGroupId);
        Assert.True(domain.IsGoal(domain.InitialState));

        // Swapping the two identical 1x1 blocks (pieces 3 and 4) is still the goal; moving a different piece is not.
        int a = lab.InitialState.AnchorOf(3), b = lab.InitialState.AnchorOf(4);
        var swapped = lab.InitialState.WithPieceMoved(topo, 3, 24).WithPieceMoved(topo, 4, a).WithPieceMoved(topo, 3, b);
        Assert.True(domain.IsGoal(swapped));
        Assert.False(domain.IsGoal(lab.InitialState.WithPieceMoved(topo, 3, 24)));
        Assert.Null(lab.GoalPosition);
    }

    [Fact]
    public void GroupsLeftOutOfTheMatchMaskMayEndUpAnywhere()
    {
        var lab = LabPuzzles.CreateLab2();
        var topo = lab.Topology;
        // Only group 0 (the L) has to match: moving the T (group 1) does not matter, moving the L does.
        var domain = new PolyominoDomain(topo, lab.InitialState, matchedGroups: 1UL << 0);
        Assert.True(domain.IsGoal(lab.InitialState));
        Assert.True(domain.IsGoal(lab.InitialState.WithPieceMoved(topo, 1, 22)));
        Assert.False(domain.IsGoal(lab.InitialState.WithPieceMoved(topo, 0, 22)));
    }

    [Fact]
    public void StateHoldsSixteenGroups()
    {
        var b = PuzzleTopology.CreateBuilder(4, 4);
        for (int g = 0; g < GroupAnchorBuffer.Length; g++)
        {
            int group = b.AddGroup($"g{g}", [(0, 0)]);
            b.AddPiece(group, $"p{g}", g);
        }
        var state = PolyominoState.FromTopology(b.Build());
        for (int g = 0; g < GroupAnchorBuffer.Length; g++) Assert.Equal(1UL << g, state.GroupAnchorsOf(g));
    }
}
