using E2e4.SlidingBlock.Logic.Labs;

namespace E2e4.SlidingBlock.Logic.Test;

public class PartitionTests
{
    private static PolyominoDomain<ulong> Line(int anchor, SlidePhysics physics, Action<PuzzleTopologyBuilder<ulong>> configure)
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(5, 1);
        b.AddPiece(b.AddGroup("one", Cells.One, physics: physics), "a", anchor);
        configure(b);
        return new PolyominoDomain<ulong>(b.Build(), 0, 0);
    }

    [Fact]
    public void PartitionBlocksAStepFromEitherSide()
    {
        void Partition(PuzzleTopologyBuilder<ulong> b) => b.AddPartition(0, 2, GridDirection.Right);
        var fromWest = Line(2, SlidePhysics.SingleStep, Partition);
        Assert.Equal(Cells.Bits(1), fromWest.ReachableFor(fromWest.InitialState, 0));
        var fromEast = Line(3, SlidePhysics.SingleStep, Partition);
        Assert.Equal(Cells.Bits(4), fromEast.ReachableFor(fromEast.InitialState, 0));
    }

    [Fact]
    public void PartitionStopsAGlideAndTheTraceNamesIt()
    {
        var domain = Line(0, SlidePhysics.FrictionlessRaycast, b => b.AddPartition(0, 2, GridDirection.Right));
        Assert.Equal(Cells.Bits(2), domain.ReachableFor(domain.InitialState, 0));

        Span<RaycastTrace<ulong>> traces = stackalloc RaycastTrace<ulong>[6];
        int n = domain.TraceRaycast(domain.InitialState, 0, traces);
        var east = traces[..n].ToArray().Single(t => t.Direction == GridDirection.Right);
        Assert.Equal(2, east.TerminalAnchor);
        Assert.Equal(2, east.StepCount);
        Assert.Equal(Cells.Bits(3), east.BlockingBitsMask);
        Assert.Equal("Partition", RaycastReport.For(domain, domain.InitialState, 0).Single(r => r.DirectionName == "Right").StopReason);
    }

    [Fact]
    public void FloodFillCannotCrossAPartitionOrRouteThroughIt()
    {
        PolyominoDomain<ulong> Board(bool sealedColumn)
        {
            var b = PuzzleTopology<ulong>.CreateBuilder(3, 3);
            b.AddPiece(b.AddGroup("one", Cells.One), "a", 0);
            b.AddPartition(0, 0, GridDirection.Right).AddPartition(1, 0, GridDirection.Right);
            if (sealedColumn) b.AddPartition(2, 0, GridDirection.Right);
            return new PolyominoDomain<ulong>(b.Build(), 0, 0);
        }

        var open = Board(false);
        Assert.Equal(Cells.Bits(1, 2, 3, 4, 5, 6, 7, 8), open.ReachableFor(open.InitialState, 0));   // round the bottom
        var closed = Board(true);
        Assert.Equal(Cells.Bits(3, 6), closed.ReachableFor(closed.InitialState, 0));
    }

    [Fact]
    public void MultiCellPieceCannotMoveAnyPartOfItselfAcrossAPartition()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(3, 2);
        b.AddPiece(b.AddGroup("domino", Cells.Domino), "d", 0);
        b.AddPartition(0, 1, GridDirection.Down);   // under the domino's right half
        var domain = new PolyominoDomain<ulong>(b.Build(), 0, 0);
        Assert.Equal(Cells.Bits(1), domain.ReachableFor(domain.InitialState, 0));   // sideways only
    }

    [Fact]
    public void PartitionAndDiodeOnTheSameCellBothApply()
    {
        var domain = Line(1, SlidePhysics.ContinuousFloodFill, b =>
        {
            b.AddDiode(0, 2, allowedEntryDirectionsMask: 1 << GridDirection.Right);
            b.AddPartition(0, 2, GridDirection.Right);
        });
        Assert.Equal(Cells.Bits(0, 2), domain.ReachableFor(domain.InitialState, 0));
    }

    [Fact]
    public void PartitionsAreCanonicalDeduplicatedAndExposed()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(5, 3);
        b.AddPartition(1, 2, GridDirection.Right).AddPartition(1, 3, GridDirection.Left).AddPartition(2, 4, GridDirection.Up);
        var topo = b.Build();
        Assert.Equal([new Partition(7, GridDirection.Right), new Partition(9, GridDirection.Down)], topo.Partitions);
        Assert.Equal(8, topo.Partitions[0].Neighbour(topo.Width));
        Assert.Equal(14, topo.Partitions[1].Neighbour(topo.Width));
        Assert.Equal(Cells.Bits(8, 14), topo.ForbiddenEntryCells(GridDirection.Right) | topo.ForbiddenEntryCells(GridDirection.Down));
        Assert.Equal(Cells.Bits(7), topo.ForbiddenEntryCells(GridDirection.Left));
        Assert.Equal(Cells.Bits(9), topo.ForbiddenEntryCells(GridDirection.Up));
    }

    [Fact]
    public void PartitionsOutsideTheBoardAreRejected()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(5, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => b.AddPartition(3, 0, GridDirection.Right));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.AddPartition(0, 5, GridDirection.Right));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.AddPartition(0, 0, GridDirection.UpRight));   // orthogonal board
        Assert.Throws<ArgumentException>(() => b.AddPartition(0, 4, GridDirection.Right));               // would leave the board
        Assert.Throws<ArgumentException>(() => b.AddPartition(0, 0, GridDirection.Up));
    }

    [Fact]
    public void HexPartitionsUseTheDiagonalDirections()
    {
        PuzzleTopology<ulong> Board(bool partition)
        {
            var b = PuzzleTopology<ulong>.CreateBuilder(4, 4, GridGeometry.HexagonalAxial);
            b.AddPiece(b.AddGroup("one", Cells.One, physics: SlidePhysics.SingleStep), "a", 5);
            if (partition) b.AddPartition(1, 1, GridDirection.UpRight);
            return b.Build();
        }

        var plain = new PolyominoDomain<ulong>(Board(false), 0, 0);
        Assert.Equal(Cells.Bits(1, 2, 4, 6, 8, 9), plain.ReachableFor(plain.InitialState, 0));
        var topo = Board(true);
        Assert.Equal([new Partition(2, GridDirection.DownLeft)], topo.Partitions);
        var split = new PolyominoDomain<ulong>(topo, 0, 0);
        Assert.Equal(Cells.Bits(1, 4, 6, 8, 9), split.ReachableFor(split.InitialState, 0));
    }

    [Fact]
    public void PartitionStopsAPushedBlock()
    {
        CompositeMove[] EastMoves(Action<PuzzleTopologyBuilder<ulong>> configure)
        {
            var b = PuzzleTopology<ulong>.CreateBuilder(5, 1);
            b.AddPiece(b.AddGroup("pusher", Cells.One, roles: RoleFlags.Pusher), "p", 0);
            b.AddPiece(b.AddGroup("crate", Cells.One, roles: RoleFlags.Pushable), "c", 1);
            configure(b);
            var domain = new CompositeDomain<ulong>(new PolyominoDomain<ulong>(b.Build(), 0, 0));
            var buffer = new CompositeMove[domain.MaxMoves];
            int n = domain.GenerateMoves(domain.InitialState, buffer);
            return buffer.Take(n).Where(m => m.AsSpan()[0].PieceId == 0 && m.DirectionIndex == GridDirection.Right).ToArray();
        }

        Assert.Single(EastMoves(_ => { }));
        Assert.Empty(EastMoves(b => b.AddPartition(0, 1, GridDirection.Right)));   // the crate may not cross
        Assert.Empty(EastMoves(b => b.AddPartition(0, 0, GridDirection.Right)));   // nor may the pusher
    }

    [Theory]
    [InlineData(1, 1, true)]    // between two cells of the rotation box: no rotation
    [InlineData(2, 2, false)]   // beside the box, not inside it
    public void RotationNeedsTheWholeBoxFreeOfPartitions(int row, int col, bool blocked)
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(4, 4);
        var ring = b.AddRotatableGroup("L", Cells.L);
        b.AddPiece(ring[0], "L", 5);                         // the box is cells 5, 6, 9 and 10
        b.AddPartition(row, col, GridDirection.Right);       // 5|6 or 10|11
        var domain = new CompositeDomain<ulong>(new PolyominoDomain<ulong>(b.Build(), 0, 0));
        var buffer = new CompositeMove[domain.MaxMoves];
        int n = domain.GenerateMoves(domain.InitialState, buffer);
        Assert.Equal(blocked, buffer.Take(n).All(m => m.DirectionIndex != -1));
    }
}
