namespace E2e4.SlidingBlock.Logic.Test;

public class ShuntingMovedPiecesTests
{
    private static ShuntingTrack Line(int slots)
    {
        var b = new ShuntingTrackBuilder(slots);
        for (int i = 0; i + 1 < slots; i++) b.Connect(i, 1, i + 1, 0);
        return b.Build();
    }

    [Fact]
    public void ListsTheEnginePushedAndPulledPieces()
    {
        // wagon 0 | engine 1 | wagon 2: driving to end 1 pushes the wagon ahead and pulls the one behind.
        var domain = new ShuntingDomain<ulong>(Line(5),
            [new("E", true, [1]) { StartFronts = [1] }, new("W", false, [0, 2])], new([]));
        var buffer = new ShuntingMove[domain.MaxMoves];
        int n = domain.GenerateMoves(domain.InitialState, buffer);

        var both = buffer.Take(n).Single(m => m.Lead == 1 && m.RearLength == 1);

        Assert.Equal([(1, 2), (2, 3), (0, 1)], domain.MovedPieces(domain.InitialState, both));
    }

    [Fact]
    public void ListsTheBridgePiecesOfATurnThatTurnsRoundAndNothingOtherwise()
    {
        var b = new ShuntingTrackBuilder(2);
        int t = b.AddTurntable([0, 1], [false, true, true]);
        var track = b.Connect(0, 1, 1, 0).Build();
        var domain = new ShuntingDomain<ulong>(track, [new("W", false, [0])], new([]));
        var buffer = new ShuntingMove[domain.MaxMoves];
        int n = domain.GenerateMoves(domain.InitialState, buffer);
        var moves = buffer.Take(n).Where(m => m.IsTurn).ToList();

        Assert.Equal([(0, 1)], domain.MovedPieces(domain.InitialState, moves.Single(m => m.Alignment == 1)));
        var turned = domain.ApplyMove(domain.InitialState, moves.Single(m => m.Alignment == 1));
        var onwards = new ShuntingMove[domain.MaxMoves];
        int k = domain.GenerateMoves(turned, onwards);
        Assert.Empty(domain.MovedPieces(turned, onwards.Take(k).Single(m => m.IsTurn && m.Alignment == 2)));
        Assert.Equal(0, t);
    }
}
