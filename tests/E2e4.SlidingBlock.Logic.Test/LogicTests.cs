using System.Numerics;
using E2e4.SlidingBlock.Logic;

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
        var d = PolyominoDomain.CreateLab2();
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
        var topo = PolyominoDomain.CreateLab2().Topology;
        foreach (int g in new[] { 1, 2 })
            for (int r = 0; r < 5; r++)
                Assert.False(((topo.Groups[g].ValidAnchorsMask >> (r * 5)) & 1UL) != 0, $"group {g} row {r}");
    }

    [Fact]
    public void ValidAnchorsMatchTwoDimensionalGroundTruthAndNeverWrap()
    {
        var topo = PolyominoDomain.CreateLab2().Topology;
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
        var d = PolyominoDomain.CreateLab2();
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
        var d = PolyominoDomain.CreateLab2();
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
        var d = PolyominoDomain.CreateLab2();
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
        var a = PolyominoDomain.CreateLab2().Topology.ZobristTable;
        var b = PolyominoDomain.CreateLab2().Topology.ZobristTable;
        Assert.Equal(a.Cast<ulong>(), b.Cast<ulong>());
    }
}

public class DomainTests
{
    [Fact]
    public void NPuzzleSlideIsNibbleXorSwap()
    {
        var s = new NPuzzleState(NPuzzleState.Solved);
        Assert.Equal(0, s.BlankIndex);
        var m = s.SlideTileIntoBlank(1);
        Assert.Equal(1, m.BlankIndex);
        Assert.Equal(1, m.TileAt(0));
        Assert.Equal(s, m.SlideTileIntoBlank(0));
        Assert.Equal(0, s.ManhattanDistance);
        Assert.Equal(1, m.ManhattanDistance);
    }

    [Fact]
    public void NPuzzleIdaStarSolvesScramble()
    {
        var start = NPuzzleDomain.Scramble(25, new Random(3));
        var domain = new NPuzzleDomain(start);
        var result = new PuzzleSolver().SolveIdaStar(domain, start, NPuzzleDomain.Manhattan);
        Assert.NotNull(result);
        var s = start;
        foreach (var mv in result!.Moves) s = domain.ApplyMove(s, mv);
        Assert.True(domain.IsGoal(s));
    }

    [Fact]
    public void RushHourDefaultIsSolvableAndOccupancyConsistent()
    {
        var d = RushHourDomain.CreateDefault();
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
        var d = KlotskiDomain.CreateClassic();
        Assert.Equal(20 - 2, BitOperations.PopCount(d.InitialState.OccupancyMask));
        var result = new PuzzleSolver().SolveBfs(d, d.InitialState);
        Assert.NotNull(result);
        Assert.Equal(116, result!.Moves.Count);
    }

    [Fact]
    public void KlotskiSwapOfIdenticalPiecesIsSameState()
    {
        var d = KlotskiDomain.CreateClassic();
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
        var d = PolyominoDomain.CreateLab2();
        var result = new PuzzleSolver().SolveBfs(d, d.InitialState);
        Assert.NotNull(result);
        var s = d.InitialState;
        foreach (var mv in result!.Moves) s = d.ApplyMove(s, mv);
        Assert.True(d.IsGoal(s));
    }
}
