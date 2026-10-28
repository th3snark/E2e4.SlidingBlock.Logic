namespace E2e4.SlidingBlock.Logic.Test;

public class ShuntingTrackTests
{
    [Fact]
    public void ConnectionsWorkBothWaysAndASwitchHasTwoEdgesOnOneEnd()
    {
        // 0 - 1 and 1 branches to 2 (main) and 3 (siding) at end 1.
        var track = new ShuntingTrackBuilder(4)
            .Connect(0, 1, 1, 0)
            .Connect(1, 1, 2, 0)
            .Connect(1, 1, 3, 0)
            .Build();

        Assert.Equal(4, track.SlotCount);
        Assert.Equal([new ShuntingEdge(1, 0, -1, 0)], track.EdgesFrom(0, 1).ToArray());
        Assert.Equal([new ShuntingEdge(0, 1, -1, 0)], track.EdgesFrom(1, 0).ToArray());
        Assert.Equal(2, track.EdgesFrom(1, 1).Length);
        Assert.Equal(0, track.EdgesFrom(0, 0).Length);
    }

    [Fact]
    public void AWyeChordJoinsEndsOfTheSameNumber()
    {
        var track = new ShuntingTrackBuilder(2).Connect(0, 1, 1, 1).Build();

        Assert.Equal([new ShuntingEdge(1, 1, -1, 0)], track.EdgesFrom(0, 1).ToArray());
        Assert.Equal([new ShuntingEdge(0, 1, -1, 0)], track.EdgesFrom(1, 1).ToArray());
    }

    [Fact]
    public void TurntableEdgesCarryTheirAlignment()
    {
        var builder = new ShuntingTrackBuilder(3);
        int t = builder.AddTurntable([1], [false, true]);
        var track = builder.ConnectWhen(t, 0, 0, 1, 1, 0).ConnectWhen(t, 1, 2, 1, 1, 0).Build();

        Assert.Equal(1, track.TurntableCount);
        Assert.Equal([1], track.BridgeSlots(0));
        Assert.Equal(2, track.AlignmentCount(0));
        Assert.False(track.IsInverted(0, 0));
        Assert.True(track.IsInverted(0, 1));
        Assert.Equal([new ShuntingEdge(1, 0, 0, 0)], track.EdgesFrom(0, 1).ToArray());
        Assert.Equal(2, track.EdgesFrom(1, 0).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ShuntingTrack.MaxSlots + 1)]
    public void RejectsASlotCountOutOfRange(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShuntingTrackBuilder(count));

    [Fact]
    public void RejectsBadConnections()
    {
        var builder = new ShuntingTrackBuilder(3).Connect(0, 1, 1, 0);

        Assert.Throws<ArgumentException>(() => builder.Connect(0, 1, 3, 0));
        Assert.Throws<ArgumentException>(() => builder.Connect(0, 2, 1, 0));
        Assert.Throws<ArgumentException>(() => builder.Connect(1, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => builder.Connect(0, 1, 1, 0));
        Assert.Throws<ArgumentException>(() => builder.ConnectWhen(0, 0, 1, 1, 2, 0));
    }

    [Fact]
    public void RejectsBadTurntables()
    {
        var builder = new ShuntingTrackBuilder(4);
        builder.AddTurntable([1], [false, true]);

        Assert.Throws<ArgumentException>(() => builder.AddTurntable([], [false, true]));
        Assert.Throws<ArgumentException>(() => builder.AddTurntable([2], [false]));
        Assert.Throws<ArgumentException>(() => builder.AddTurntable([1], [false, true]));
        Assert.Throws<ArgumentException>(() => builder.AddTurntable([2, 2], [false, true]));
        Assert.Throws<ArgumentException>(() => builder.AddTurntable([9], [false, true]));
        Assert.Throws<ArgumentException>(() => builder.ConnectWhen(0, 5, 0, 1, 1, 0));
    }

    [Fact]
    public void FoulingSetsAreKeptAndChecked()
    {
        var builder = new ShuntingTrackBuilder(4).AddFoulingSet([1, 3]);

        Assert.Equal([[1, 3]], builder.Build().FoulingSets);
        Assert.Empty(new ShuntingTrackBuilder(2).Build().FoulingSets);
        Assert.Throws<ArgumentNullException>(() => builder.AddFoulingSet(null!));
        Assert.Throws<ArgumentException>(() => builder.AddFoulingSet([1]));
        Assert.Throws<ArgumentException>(() => builder.AddFoulingSet([1, 1]));
        Assert.Throws<ArgumentException>(() => builder.AddFoulingSet([1, 4]));
    }
}
