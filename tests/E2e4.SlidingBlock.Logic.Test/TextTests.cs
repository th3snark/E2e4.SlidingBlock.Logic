using E2e4.SlidingBlock.Logic.Text;

namespace E2e4.SlidingBlock.Logic.Test;

/// <summary>The diagram readers of <c>E2e4.SlidingBlock.Logic.Text</c>: the symbols are the caller's, not the engine's.</summary>
public class TextTests
{
    private static readonly (char, PieceKind)[] Legend =
    [
        ('a', new PieceKind("Bar", Key: "bar")),
        ('b', new PieceKind("Dot", Key: "dot")),
    ];

    [Fact]
    public void TopologyMapTakesItsWallAndEmptySymbolsFromTheCaller()
    {
        var plain = TopologyMap.Parse<ulong>(["#a.", "#a.", "..b"], Legend);
        var custom = TopologyMap.Parse<ulong>(["~a_", "~a_", "__b"], Legend, wall: '~', empty: '_');
        Assert.Equal(plain.StaticWallsMask, custom.StaticWallsMask);
        Assert.Equal(2, custom.Pieces.Count);
        Assert.Equal(plain.Groups.Select(g => g.Name), custom.Groups.Select(g => g.Name));
        Assert.Equal(plain.Pieces.Select(p => (p.Label, p.InitialAnchor)), custom.Pieces.Select(p => (p.Label, p.InitialAnchor)));
    }

    [Fact]
    public void TopologyMapGroupsPiecesByShapeAndKey()
    {
        var topology = TopologyMap.Parse<ulong>(["ab", "ab"], [('a', new PieceKind("Bar", Key: "bar")), ('b', new PieceKind("Bar again", Key: "bar"))]);
        Assert.Single(topology.Groups);   // the name is not part of the identity
        Assert.Equal(2, topology.Pieces.Count);
    }

    [Fact]
    public void TopologyMapRejectsBadDiagrams()
    {
        Assert.Throws<ArgumentException>(() => TopologyMap.Parse<ulong>([], Legend));
        Assert.Throws<ArgumentException>(() => TopologyMap.Parse<ulong>(["a.", "a"], Legend));
        Assert.Throws<ArgumentException>(() => TopologyMap.Parse<ulong>(["ac", ".."], Legend));   // 'c' is not in the legend
    }

    [Fact]
    public void LaneDiagramPutsTheLeadingVehiclesFirst()
    {
        string[] rows = ["BB.A", "..XX"];
        Assert.Equal("XAB", LaneDiagram.SymbolsOf(rows, leadingLabels: "X"));
        Assert.Equal("ABX", LaneDiagram.SymbolsOf(rows));
        Assert.Equal("AB", LaneDiagram.SymbolsOf(["BB-A", "--XX"], empty: '-', leadingLabels: "X").Replace("X", ""));
    }

    [Fact]
    public void LaneDiagramReadsVehiclesAlongTheirLongerSide()
    {
        var vehicles = LaneDiagram.Parse(["AA.B", "AA.B", "...B"], "AB");
        Assert.Equal(new LaneVehicle(0, false, 2, 2, 0, 0), vehicles[0]);   // a square slides along its columns
        Assert.Equal(new LaneVehicle(1, false, 3, 1, 3, 0), vehicles[1]);
    }

    [Fact]
    public void TheReadmeRushHourSampleSolves()
    {
        // The same lines as the README, so the sample cannot drift from the API.
        string[] diagram = ["CCH..F", "..HB.F", ".XXB..", "D..B..", "DGG...", "..EEE."];
        var vehicles = LaneDiagram.Parse(diagram, LaneDiagram.SymbolsOf(diagram, leadingLabels: "X"));
        var rushHour = new LaneSlideDomain<LaneState16, ulong>(vehicles, columns: 6, rows: 6, goalVehicle: 0, goalOffset: 4, maxStep: 1);
        Assert.NotNull(new PuzzleSolver().SolveBfs(rushHour, rushHour.InitialState)?.Moves.Count);
    }

    [Fact]
    public void PushMapTakesItsSymbolsFromTheCaller()
    {
        var plain = PushMap.CreateDomain(["@1.X."], "1");
        var custom = PushMap.Parse(["p0_t_"], "0", floor: '_', target: 't', player: 'p').CreateDomain();
        Assert.Equal(plain.InitialState, custom.InitialState);
        Assert.Equal(plain.Targets, custom.Targets);
    }

    [Fact]
    public void PushMapReadsWallsAndOutsideCells()
    {
        var layout = PushMap.Parse(["#####", "#@1X#", "## ##"], "1");
        Assert.Equal((5, 3), (layout.Columns, layout.Rows));
        Assert.Equal([6, 7, 8], layout.Floor);
        Assert.Equal([8], layout.Targets);
        Assert.Equal([7], layout.Cases);
        Assert.Equal(6, layout.Player);
        Assert.Single(new PuzzleSolver().SolveBfs(layout.CreateDomain(), layout.CreateDomain().InitialState)!.Moves);
    }

    [Fact]
    public void PushMapRejectsBadDiagrams()
    {
        Assert.Throws<ArgumentException>(() => PushMap.Parse([], "1"));
        Assert.Throws<ArgumentException>(() => PushMap.Parse(["@1.X?"], "1"));   // unknown symbol
        Assert.Throws<ArgumentException>(() => PushMap.Parse(["1.X."], "1"));    // no storeman
        Assert.Throws<ArgumentException>(() => PushMap.Parse(["@.X."], "1"));    // no cases
        Assert.Throws<ArgumentException>(() => PushMap.Parse(["@2.X."], "12"));  // case 1 is missing: ids must not skip
        Assert.Throws<ArgumentException>(() => PushMap.Parse(["@1.XX"], "1"));   // more places than cases
    }
}
