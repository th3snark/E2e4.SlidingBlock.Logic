using E2e4.SlidingBlock.Logic.Text;

namespace E2e4.SlidingBlock.Logic.Test;

/// <summary>The optional per-group rules of the shunting domain: pushed-only wagons, permanent units, engines without a front coupler, and carrying limits.</summary>
public class ShuntingRuleTests
{
    private static ShuntingTrack Line(int slots)
    {
        var b = new ShuntingTrackBuilder(slots);
        for (int i = 0; i + 1 < slots; i++) b.Connect(i, 1, i + 1, 0);
        return b.Build();
    }

    private static ShuntingDomain<Bitboard64> Domain(int slots, params ShuntingGroup[] groups) => new(Line(slots), groups, new([]));

    private static List<ShuntingMove> Moves(ShuntingDomain<Bitboard64> domain)
    {
        var buffer = new ShuntingMove[domain.MaxMoves];
        int n = domain.GenerateMoves(domain.InitialState, buffer);
        return [.. buffer.Take(n)];
    }

    private static ShuntingGroup Engine(int slot, bool frontCoupler = true, int? maxCarried = null) =>
        new("E", true, [slot]) { StartFronts = [1], FrontCoupler = frontCoupler, MaxCarried = maxCarried };

    [Theory]
    [InlineData(true, new[] { 0, 1 })]
    [InlineData(false, new[] { 0 })]
    public void AWagonWithoutCouplersCanBePushedButNotPulled(bool canBePulled, int[] rearLengths)
    {
        var domain = Domain(4, Engine(1), new("W", false, [0]) { CanBePulled = canBePulled });

        var forward = Moves(domain).Where(m => m.Lead == 1);

        Assert.Equal(rearLengths, forward.Select(m => (int)m.RearLength).Order());
    }

    [Fact]
    public void APushedOnlyWagonIsStillPushed()
    {
        var domain = Domain(4, Engine(0), new("W", false, [1]) { CanBePulled = false });

        Assert.Single(Moves(domain), m => m.Lead == 1 && m.FrontLength == 2);
    }

    [Fact]
    public void AnEngineNeverLeavesPartOfAPermanentUnitBehind()
    {
        var domain = Domain(4, Engine(2), new("A", false, [0]) { Unit = 0 }, new("B", false, [1]) { Unit = 0 });

        var forward = Moves(domain).Where(m => m.Lead == 1);

        Assert.Equal([0, 2], forward.Select(m => (int)m.RearLength).Order());
    }

    [Theory]
    [InlineData(true, new[] { 0, 1 })]
    [InlineData(false, new[] { 0 })]
    public void WithoutAFrontCouplerAnEngineCannotPullWhileDrivingRearFirst(bool frontCoupler, int[] rearLengths)
    {
        // The engine faces end 1, the wagon is in front of it, and driving to end 0 is driving rear first.
        var domain = Domain(6, Engine(2, frontCoupler), new("W", false, [3]));

        var rearFirst = Moves(domain).Where(m => m.Lead == 0);

        Assert.Equal(rearLengths, rearFirst.Select(m => (int)m.RearLength).Order());
    }

    [Fact]
    public void WithoutAFrontCouplerAnEngineStillPullsWhileDrivingFrontFirst()
    {
        var domain = Domain(6, Engine(2, frontCoupler: false), new("W", false, [1]));

        Assert.Contains(Moves(domain), m => m.Lead == 1 && m.RearLength == 1);
    }

    [Fact]
    public void AnEngineMayBeLimitedToCarryingOneWagon()
    {
        var pushTwo = Domain(6, Engine(1, maxCarried: 1), new("W", false, [2, 3]));
        Assert.DoesNotContain(Moves(pushTwo), m => m.Lead == 1);

        var pushAndPull = Domain(6, Engine(1, maxCarried: 1), new("W", false, [0, 2]));
        var forward = Moves(pushAndPull).Where(m => m.Lead == 1).ToList();
        Assert.Single(forward);
        Assert.Equal(0, forward[0].RearLength);
    }

    [Fact]
    public void RejectsBadRuleValues()
    {
        Assert.Throws<ArgumentException>(() => Domain(4, Engine(0), new("W", false, [1]) { Unit = -2 }));
        Assert.Throws<ArgumentException>(() => Domain(4, Engine(0, maxCarried: -1), new("W", false, [1])));
    }

    [Fact]
    public void TheTextReaderReadsTheRules()
    {
        string[] map =
        [
            "track 6",
            "join 0.1 1.0", "join 1.1 2.0", "join 2.1 3.0", "join 3.1 4.0", "join 4.1 5.0",
            "start E1234.",
            "pushonly 4",
            "joined 123",
            "nofrontcoupler E",
            "carries E 2",
        ];

        var layout = ShuntingMap.Parse(map, "E1234", "E");

        Assert.False(layout.Groups[0].FrontCoupler);
        Assert.Equal(2, layout.Groups[0].MaxCarried);
        Assert.Equal([0, 0, 0], layout.Groups.Skip(1).Take(3).Select(g => g.Unit));
        Assert.Equal(-1, layout.Groups[4].Unit);
        Assert.False(layout.Groups[4].CanBePulled);
        Assert.True(layout.Groups[1].CanBePulled);
        Assert.Throws<ArgumentException>(() => ShuntingMap.Parse([.. map, "joined 1"], "E1234", "E"));
        Assert.Throws<ArgumentException>(() => ShuntingMap.Parse([.. map, "pushonly Q"], "E1234", "E"));
    }
}
