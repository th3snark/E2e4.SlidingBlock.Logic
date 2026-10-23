namespace E2e4.SlidingBlock.Logic.Labs;

/// <summary>
/// The puzzles the laboratories play with: layouts, goal positions and group colours. The engine knows none of this; it only
/// receives the goals as constructor data. Plain C# (no WPF types) so the test project can link this file and check the
/// layouts against the Lab briefs.
/// </summary>
public static class LabPuzzles
{
    // ---- 15-puzzle ------------------------------------------------------------------------------------------

    /// <summary>Tile t on square t, blank on square 0 (nibble i holds the tile on square i).</summary>
    public static readonly NPuzzleState Puzzle15Solved = new(0xFEDCBA9876543210UL);

    public static NPuzzleDomain CreatePuzzle15() => new(Puzzle15Solved, Puzzle15Solved);

    // ---- Klotski --------------------------------------------------------------------------------------------

    public const string KlotskiMasterName = "Master 2x2";
    public const int KlotskiGoalAnchor = 13;

    public static readonly string[] KlotskiMap =
    [
        "aMMb",
        "aMMb",
        "cHHd",
        "cefd",
        "g..h",
    ];

    /// <summary>Group colours by group name.</summary>
    public static readonly IReadOnlyDictionary<string, string> KlotskiColors = new Dictionary<string, string>
    {
        ["Single 1x1"] = "#10B981",
        ["Vertical 1x2"] = "#3B82F6",
        ["Horizontal 2x1"] = "#F59E0B",
        [KlotskiMasterName] = "#EF4444",
    };

    /// <summary>The classic 4x5 layout: one 2x2, four 1x2 vertical, one 2x1 horizontal, four 1x1.</summary>
    public static KlotskiDomain CreateKlotski()
    {
        var topo = PuzzleTopology.FromAsciiMap(KlotskiMap,
        [
            // Legend order fixes the shape class: 1 = 1x1, 2 = 1x2 vertical, 3 = 2x1 horizontal, 4 = 2x2.
            ('e', new PieceKind("Single 1x1", Key: "single")),
            ('f', new PieceKind("Single 1x1", Key: "single")),
            ('g', new PieceKind("Single 1x1", Key: "single")),
            ('h', new PieceKind("Single 1x1", Key: "single")),
            ('a', new PieceKind("Vertical 1x2", Key: "vertical")),
            ('b', new PieceKind("Vertical 1x2", Key: "vertical")),
            ('c', new PieceKind("Vertical 1x2", Key: "vertical")),
            ('d', new PieceKind("Vertical 1x2", Key: "vertical")),
            ('H', new PieceKind("Horizontal 2x1", Key: "horizontal")),
            ('M', new PieceKind(KlotskiMasterName, Key: "master")),
        ]);
        int master = topo.Pieces.First(p => p.Label == "M").PieceId;
        return new KlotskiDomain(topo, master, KlotskiGoalAnchor);
    }

    // ---- Rush Hour ------------------------------------------------------------------------------------------

    public const char RushHourRedCar = 'X';

    /// <summary>The 8-vehicle layout from the Lab 1 brief (vehicles v0..v7 are X, B..H).</summary>
    public static readonly string[] RushHourMap =
    [
        "CCH..F",
        "..HB.F",
        ".XXB..",
        "D..B..",
        "DGG...",
        "..EEE.",
    ];

    /// <summary>The red car X is vehicle 0 and has to reach the right edge (offset 4 for a 2-long car).</summary>
    public static RushHourDomain CreateRushHour()
    {
        var topo = RushHourTopology.FromAsciiMap(RushHourMap, leadingLabels: RushHourRedCar.ToString());
        int red = topo.IndexOf(RushHourRedCar);
        return new RushHourDomain(topo, red, RushHourTopology.Size - topo.Vehicles[red].Length);
    }

    // ---- Lab 2 (polyominoes) --------------------------------------------------------------------------------

    public const int Lab2Width = 5;
    public const int Lab2MasterGroup = 0;
    public const int Lab2GoalAnchor = 16;

    /// <summary>Group colours by group name.</summary>
    public static readonly IReadOnlyDictionary<string, string> Lab2Colors = new Dictionary<string, string>
    {
        ["L-Tromino"] = "#F5B301",
        ["Upward T"] = "#22D3EE",
        ["J-Tromino"] = "#A855F7",
        ["1x1 Single"] = "#10B981",
    };

    /// <summary>The 5x5 Lab 2 board: L-tromino, upward T, J-tromino, two identical 1x1 blocks, wall at index 12.</summary>
    public static PolyominoDomain CreateLab2()
    {
        var b = PuzzleTopology.CreateBuilder(Lab2Width, Lab2Width);
        b.AddWall(2, 2);
        int l = b.AddGroup("L-Tromino", [(0, 0), (1, 0), (1, 1)]);
        int t = b.AddGroup("Upward T", [(0, 0), (1, -1), (1, 0), (1, 1)]);
        int j = b.AddGroup("J-Tromino", [(0, 0), (1, -1), (1, 0)]);
        int s = b.AddGroup("1x1 Single", [(0, 0)]);
        b.AddPiece(l, "L", 0);
        b.AddPiece(t, "T", 16);
        b.AddPiece(j, "J", 4);
        b.AddPiece(s, "A", 2);
        b.AddPiece(s, "B", 14);
        return new PolyominoDomain(b.Build(), Lab2MasterGroup, Lab2GoalAnchor);
    }
}
