using System.Numerics;

namespace E2e4.SlidingBlock.Logic.Test;

internal static class Cells
{
    public static readonly (int, int)[] One = [(0, 0)];
    public static readonly (int, int)[] Domino = [(0, 0), (0, 1)];
    public static readonly (int, int)[] L = [(0, 0), (1, 0), (1, 1)];
    public static readonly (int, int)[] U = [(0, 0), (0, 2), (1, 0), (1, 1), (1, 2)];

    public static ulong Bits(params int[] anchors)
    {
        ulong m = 0;
        foreach (int a in anchors) m |= 1UL << a;
        return m;
    }

    public static ulong HashOf<T>(PuzzleTopology<T> topology, PolyominoState<T> state) where T : unmanaged, IBinaryInteger<T>, IUnsignedNumber<T>
    {
        ulong h = 0;
        foreach (var p in topology.Pieces) h ^= topology.ZobristKey(state.GroupOf(p.PieceId), state.AnchorOf(p.PieceId));
        return h;
    }
}

public class WideBoardAndHexTests
{
    [Fact]
    public void UInt128BoardUsesBitsBeyond63()
    {
        var b = PuzzleTopology<UInt128>.CreateBuilder(10, 10);
        int domino = b.AddGroup("domino", Cells.Domino);
        b.AddPiece(domino, "d", 95);
        var topo = b.Build();

        Assert.Equal(UInt128.One | (UInt128.One << 1), topo.Groups[0].BaseMask);
        Assert.Equal(UInt128.One, (topo.Groups[0].ValidAnchorsMask >> 98) & UInt128.One);
        Assert.Equal(UInt128.Zero, (topo.Groups[0].ValidAnchorsMask >> 99) & UInt128.One);   // would wrap onto the next row

        var domain = new PolyominoDomain<UInt128>(topo, 0, 70);
        var s = domain.InitialState;
        Assert.Equal(topo.Groups[0].BaseMask << 95, s.OccupancyMask);
        Assert.Equal(topo.ZobristKey(0, 95), s.ZobristHash);

        Span<PolyominoMove> moves = stackalloc PolyominoMove[domain.MaxMoves];
        int n = domain.GenerateMoves(s, moves);
        Assert.Equal(9 * 10 - 1, n);   // every other horizontal domino anchor
        Assert.Contains(new PolyominoMove(0, 70), moves[..n].ToArray());

        var moved = domain.ApplyMove(s, new PolyominoMove(0, 70));
        Assert.Equal(topo.ZobristKey(0, 70), moved.ZobristHash);
        Assert.Equal(topo.Groups[0].BaseMask << 70, moved.OccupancyMask);
        Assert.True(domain.IsGoal(moved));
        Assert.Equal(Cells.HashOf(topo, moved), moved.ZobristHash);
        Assert.Equal(70, (moved.OccupancyMask).Lsb);
        Assert.Equal(2, moved.OccupancyMask.BitCount);
    }

    [Fact]
    public void HexBoardMovesInSixDirectionsWithoutWrapping()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(5, 5, GridGeometry.HexagonalAxial);
        int g = b.AddGroup("hex", Cells.One, AllowedDirections.Both, physics: SlidePhysics.SingleStep);
        b.AddPiece(g, "a", 12);
        b.AddPiece(g, "b", 14);
        var topo = b.Build();

        Assert.Equal([-1, 1, -5, 5, -4, 4], topo.GetDirectionDeltas().ToArray());

        var domain = new PolyominoDomain<ulong>(topo, 0, 0);
        var s = domain.InitialState;
        Assert.Equal(Cells.Bits(11, 13, 7, 17, 8, 16), domain.ReachableFor(s, 0));
        // Column 4: no east step, and the north-east step would wrap onto row 2's column 0.
        Assert.Equal(Cells.Bits(13, 9, 19, 18), domain.ReachableFor(s, 1));
    }

    [Fact]
    public void HexFloodFillReachesEveryCell()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(5, 5, GridGeometry.HexagonalAxial);
        b.AddPiece(b.AddGroup("hex", Cells.One), "a", 12);
        var domain = new PolyominoDomain<ulong>(b.Build(), 0, 0);
        Assert.Equal((1UL << 25) - 1 - (1UL << 12), domain.ReachableFor(domain.InitialState, 0));
    }
}

public class RotationTests
{
    private static (PuzzleTopology<ulong> Topo, IReadOnlyList<int> Ring) Board(bool withObstacle)
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(4, 4);
        var ring = b.AddRotatableGroup("L", Cells.L);
        int one = b.AddGroup("one", Cells.One);
        b.AddPiece(ring[0], "L", 5);
        if (withObstacle) b.AddPiece(one, "o", 6);
        return (b.Build(), ring);
    }

    [Fact]
    public void RingHasFourOrientationsWithSignedAnchorDeltas()
    {
        var (topo, ring) = Board(false);
        Assert.Equal(4, ring.Count);
        Assert.All(ring, id => Assert.True(topo.Groups[id].CanRotate));
        Assert.Equal(1, topo.Groups[ring[2]].AnchorDeltaCw);     // 180 -> 270: the LSB moves right
        Assert.Equal(-1, topo.Groups[ring[3]].AnchorDeltaCw);    // 270 -> 0
        Assert.Equal(-1, topo.Groups[ring[3]].BoxOriginDelta);
        Assert.Equal(ring[1], topo.Groups[ring[0]].NextCwGroupId);
        Assert.Equal(ring[3], topo.Groups[ring[0]].NextCcwGroupId);
    }

    [Fact]
    public void FullTurnRestoresPositionAndHash()
    {
        var (topo, ring) = Board(false);
        var domain = new CompositeDomain<ulong>(new PolyominoDomain<ulong>(topo, 0, 0));
        var start = domain.InitialState;
        var s = start;
        var buffer = new CompositeMove[domain.MaxMoves];
        for (int i = 1; i <= 4; i++)
        {
            int n = domain.GenerateMoves(s, buffer);
            var cw = buffer.Take(n).Single(m => m.DirectionIndex == -1 && m.AsSpan()[0].NewGroupId == topo.Groups[s.GroupOf(0)].NextCwGroupId);
            s = domain.ApplyMove(s, cw);
            Assert.Equal(ring[i % 4], s.GroupOf(0));
            Assert.Equal(Cells.HashOf(topo, s), s.ZobristHash);
        }
        Assert.Equal(start, s);
        Assert.Equal(start.ZobristHash, s.ZobristHash);
        Assert.Equal(5, s.AnchorOf(0));
    }

    [Fact]
    public void ObstacleInTheEmptyCornerOfTheBoxBlocksRotation()
    {
        var (topo, _) = Board(true);
        var domain = new CompositeDomain<ulong>(new PolyominoDomain<ulong>(topo, 0, 0));
        var buffer = new CompositeMove[domain.MaxMoves];
        int n = domain.GenerateMoves(domain.InitialState, buffer);
        Assert.DoesNotContain(buffer.Take(n), m => m.DirectionIndex == -1);
    }
}

public class CoupledKinematicsTests
{
    private static (PuzzleTopology<ulong> Topo, CompositeDomain<ulong> Domain) UCarrier()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(6, 6);
        int u = b.AddGroup("U", Cells.U, physics: SlidePhysics.SingleStep, roles: RoleFlags.HollowCarrier);
        int block = b.AddGroup("block", Cells.One, roles: RoleFlags.Pushable);
        b.AddPiece(u, "U", 14);
        b.AddPiece(block, "b", 15);   // in the pocket
        var topo = b.Build();
        return (topo, new CompositeDomain<ulong>(new PolyominoDomain<ulong>(topo, 0, 0)));
    }

    private static CompositeMove[] MovesOf(CompositeDomain<ulong> domain, PolyominoState<ulong> s, int pieceId)
    {
        var buffer = new CompositeMove[domain.MaxMoves];
        int n = domain.GenerateMoves(s, buffer);
        return buffer.Take(n).Where(m => m.AsSpan()[0].PieceId == pieceId).ToArray();
    }

    [Theory]
    [InlineData(0, 2)]   // west: the left arm pushes the block
    [InlineData(1, 2)]   // east: the right arm pushes the block
    [InlineData(2, 2)]   // north, towards the open mouth: the floor lifts the block
    [InlineData(3, 1)]   // south, away from the mouth: the block stays behind
    public void CarrierMovesThePocketBlockOnlyWhenItPushesIt(int dir, int expectedCount)
    {
        var (_, domain) = UCarrier();
        var move = MovesOf(domain, domain.InitialState, 0).Single(m => m.DirectionIndex == dir);
        Assert.Equal(expectedCount, move.Count);
    }

    [Fact]
    public void IncrementalHashMatchesRecomputationAfterCarrying()
    {
        var (topo, domain) = UCarrier();
        var s = domain.InitialState;
        foreach (int dir in new[] { 1, 2, 0, 3 })
        {
            var move = MovesOf(domain, s, 0).Single(m => m.DirectionIndex == dir);
            s = domain.ApplyMove(s, move);
            Assert.Equal(Cells.HashOf(topo, s), s.ZobristHash);
        }
    }

    [Fact]
    public void PusherShovesPushableAndStopsAtAWall()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(4, 1);
        int pusher = b.AddGroup("pusher", Cells.One, roles: RoleFlags.Pusher);
        int crate = b.AddGroup("crate", Cells.One, roles: RoleFlags.Pushable);
        b.AddPiece(pusher, "p", 0);
        b.AddPiece(crate, "c", 1);
        var topo = b.Build();
        var domain = new CompositeDomain<ulong>(new PolyominoDomain<ulong>(topo, 0, 0));

        var east = MovesOf(domain, domain.InitialState, 0).Single(m => m.DirectionIndex == 1);
        Assert.Equal(2, east.Count);
        var s = domain.ApplyMove(domain.InitialState, east);
        s = domain.ApplyMove(s, MovesOf(domain, s, 0).Single(m => m.DirectionIndex == 1));   // crate now on the last cell
        Assert.Equal(3, s.AnchorOf(1));
        Assert.DoesNotContain(MovesOf(domain, s, 0), m => m.DirectionIndex == 1);
    }
}

public class RaycastTests
{
    [Fact]
    public void IceGlideEmitsOnlyTerminalAnchors()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(6, 3);
        int ice = b.AddGroup("ice", Cells.One, physics: SlidePhysics.FrictionlessRaycast);
        int rock = b.AddGroup("rock", Cells.One, physics: SlidePhysics.SingleStep);
        b.AddPiece(ice, "i", 7);
        b.AddPiece(rock, "r", 10);
        var topo = b.Build();
        var domain = new PolyominoDomain<ulong>(topo, 0, 0);

        Assert.Equal(Cells.Bits(6, 9, 1, 13), domain.ReachableFor(domain.InitialState, 0));

        Span<RaycastTrace<ulong>> traces = stackalloc RaycastTrace<ulong>[6];
        int n = domain.TraceRaycast(domain.InitialState, 0, traces);
        var east = traces[..n].ToArray().Single(t => t.Direction == 1);
        Assert.Equal(9, east.TerminalAnchor);
        Assert.Equal(2, east.StepCount);
        Assert.Equal(Cells.Bits(7, 8, 9), east.SweptCellsMask);
        Assert.Equal(Cells.Bits(10), east.BlockingBitsMask);
    }
}

public class ElevationTests
{
    private static ulong ReachOfGround(byte groundElevation)
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(5, 1);
        int bridge = b.AddGroup("bridge", [(0, 0), (0, 1), (0, 2)], elevationMask: 0b10);
        int mover = b.AddGroup("mover", Cells.One, elevationMask: groundElevation);
        b.AddPiece(bridge, "B", 1);
        b.AddPiece(mover, "m", 0);
        var domain = new PolyominoDomain<ulong>(b.Build(), 0, 0);
        return domain.ReachableFor(domain.InitialState, 1);
    }

    [Fact]
    public void GroundPieceSlidesUnderTheBridge() => Assert.Equal(Cells.Bits(1, 2, 3, 4), ReachOfGround(0b01));

    [Fact]
    public void TallPieceIsBlockedByTheBridge() => Assert.Equal(0UL, ReachOfGround(0b11));
}

public class StatefulCellTests
{
    private static PuzzleTopology<ulong> Gated(int pieceA, int pieceB)
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(6, 1);
        int one = b.AddGroup("one", Cells.One);
        b.AddPiece(one, "A", pieceA);
        b.AddPiece(one, "B", pieceB);
        b.AddPressurePlate(1UL << 0, 1UL << 3);
        return b.Build();
    }

    [Fact]
    public void ClosedGateBlocksUntilTheTriggerIsOccupied()
    {
        var topo = Gated(1, 5);
        var domain = new PolyominoDomain<ulong>(topo, 0, 0);
        var s = domain.InitialState;
        Assert.Equal(Cells.Bits(0, 2), domain.ReachableFor(s, 0));       // cannot get past the gate at 3
        Assert.Equal(Cells.Bits(4), domain.ReachableFor(s, 1));

        s = domain.ApplyMove(s, new PolyominoMove(0, 0));                          // A stands on the trigger
        Assert.Equal(0UL, domain.ActiveDynamicWalls(s));
        Assert.Equal(Cells.Bits(3, 2), domain.ReachableFor(s, 1) & Cells.Bits(3, 2));
    }

    [Fact]
    public void SteppingOffTheTriggerIsIllegalWhileAPieceSitsInTheGate()
    {
        var topo = Gated(0, 3);
        var domain = new PolyominoDomain<ulong>(topo, 0, 0);
        var buffer = new PolyominoMove[domain.MaxMoves];
        int n = domain.GenerateMoves(domain.InitialState, buffer);
        Assert.DoesNotContain(buffer.Take(n), m => m.PieceId == 0);
        Assert.Contains(buffer.Take(n), m => m.PieceId == 1);
    }

    [Fact]
    public void DiodePermitsEastButBlocksWest()
    {
        PolyominoDomain<ulong> Domain(int anchor)
        {
            var b = PuzzleTopology<ulong>.CreateBuilder(5, 1);
            b.AddPiece(b.AddGroup("one", Cells.One), "a", anchor);
            b.AddDiode(0, 2, allowedEntryDirectionsMask: 1 << 1);   // entry only while travelling east
            return new PolyominoDomain<ulong>(b.Build(), 0, 0);
        }

        var fromWest = Domain(1);
        Assert.Equal(Cells.Bits(0, 2, 3, 4), fromWest.ReachableFor(fromWest.InitialState, 0));
        var fromEast = Domain(3);
        Assert.Equal(Cells.Bits(4), fromEast.ReachableFor(fromEast.InitialState, 0));
    }
}
