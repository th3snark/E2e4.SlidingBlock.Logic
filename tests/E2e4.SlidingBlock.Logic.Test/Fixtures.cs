using E2e4.SlidingBlock.Logic.Text;

namespace E2e4.SlidingBlock.Logic.Test;

/// <summary>
/// The layouts the engine tests play on. They are the tests' own: the golden values elsewhere in the suite are measured on exactly these, so a
/// change to an example application never moves them.
/// </summary>
internal static class Fixtures
{
    /// <summary>The symbol of each case of a Soko diagram, by case id: case 1 is '1', case 10 is 'A', and so on (no 'X': that marks a place).</summary>
    public const string CaseSymbols = "123456789ABCDEFGHIJKLMNOPQRSTUVWYZ";

    // ---- 15-puzzle ------------------------------------------------------------------------------------------

    /// <summary>Tile t on square t, blank on square 0 (nibble i holds the tile on square i).</summary>
    public static readonly NPuzzleState Puzzle15Solved = new(0xFEDCBA9876543210UL);

    public static NPuzzleDomain CreatePuzzle15() => new(Puzzle15Solved, Puzzle15Solved);

    // ---- Klotski --------------------------------------------------------------------------------------------

    private static readonly string[] KlotskiMap =
    [
        "aMMb",
        "aMMb",
        "cHHd",
        "cefd",
        "g..h",
    ];

    /// <summary>The classic 4x5 layout: one 2x2, four 1x2 vertical, one 2x1 horizontal, four 1x1; the 2x2 has to reach anchor 13.</summary>
    public static KlotskiDomain CreateKlotski()
    {
        var topo = TopologyMap.Parse<ulong>(KlotskiMap,
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
            ('M', new PieceKind("Master 2x2", Key: "master")),
        ]);
        int master = topo.Pieces.First(p => p.Label == "M").PieceId;
        return new KlotskiDomain(topo, master, 13);
    }

    // ---- Rush Hour ------------------------------------------------------------------------------------------

    /// <summary>An 8-vehicle layout; the red car X is vehicle 0 and has to reach the right edge (offset 4 for a 2-long car); a move is one cell.</summary>
    public static readonly string[] RushHourMap =
    [
        "CCH..F",
        "..HB.F",
        ".XXB..",
        "D..B..",
        "DGG...",
        "..EEE.",
    ];

    /// <summary>Another 8-vehicle layout, solved in 13 moves.</summary>
    public static readonly string[] RushHourDefaultMap =
    [
        "BBB..C",
        "..D..C",
        "XXD.EC",
        "F...E.",
        "F.GGG.",
        "HHH...",
    ];

    public static LaneSlideDomain<LaneState16, ulong> CreateRushHour() => RushHour(RushHourMap);

    public static LaneSlideDomain<LaneState16, ulong> RushHourDefault() => RushHour(RushHourDefaultMap);

    /// <summary>The red car (X, a horizontal one) is vehicle 0 and has to reach the far end of its row; a move is one cell.</summary>
    public static LaneSlideDomain<LaneState16, ulong> RushHour(IReadOnlyList<string> rows)
    {
        var vehicles = LaneDiagram.Parse(rows, LaneDiagram.SymbolsOf(rows, leadingLabels: "X"));
        return new LaneSlideDomain<LaneState16, ulong>(vehicles, rows[0].Length, rows.Count, 0, rows[0].Length - vehicles[0].Length, maxStep: 1);
    }

    // ---- Polyominoes (the "Lab 2" board) ------------------------------------------------------------------------

    /// <summary>The 5x5 board: L-tromino, upward T, J-tromino, two identical 1x1 blocks, wall at index 12; the L has to reach anchor 16.</summary>
    public static PolyominoDomain<ulong> CreateLab2()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(5, 5);
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
        return new PolyominoDomain<ulong>(b.Build(), 0, 16);
    }

    // ---- Extended mechanics (the "Lab 3" boards) ------------------------------------------------------------------

    private const int Width = 6;

    /// <summary>One board of the extended mechanics: its domain for play and solving, and the plain polyomino domain that owns the goal.</summary>
    public sealed record Mode(PolyominoDomain<ulong> Goal, CompositeDomain<ulong> Domain);

    /// <summary>An L-tromino turns through four orientation groups; a blocker sits in the empty corner of its 2x2 rotation box.</summary>
    public static Mode CreateRotation()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(Width, Width);
        b.AddWall(4, 4);
        var ring = b.AddRotatableGroup("L-Tromino", [(0, 0), (1, 0), (1, 1)]);
        int blocker = b.AddGroup("1x1 Blocker", [(0, 0)]);
        b.AddPiece(ring[0], "L", 7);
        b.AddPiece(blocker, "A", 8);
        b.AddPiece(blocker, "B", 20);
        var goal = new PolyominoDomain<ulong>(b.Build(), ring[3], 15);
        return new Mode(goal, new CompositeDomain<ulong>(goal));
    }

    /// <summary>A hollow U carries the crate in its pocket; the pusher avatar shoves crates in a chain.</summary>
    public static Mode CreateCarrier()
    {
        static PuzzleTopology<ulong> Build(int crateA)
        {
            var b = PuzzleTopology<ulong>.CreateBuilder(Width, Width);
            b.AddWall(3, 0);
            b.AddWall(3, 5);
            int u = b.AddGroup("Hollow U-Carrier", [(0, 0), (0, 2), (1, 0), (1, 1), (1, 2)], physics: SlidePhysics.SingleStep,
                roles: RoleFlags.Pusher | RoleFlags.HollowCarrier);
            int crate = b.AddGroup("Crate", [(0, 0)], physics: SlidePhysics.SingleStep, roles: RoleFlags.Pushable);
            int avatar = b.AddGroup("Sokoban Pusher", [(0, 0)], physics: SlidePhysics.SingleStep, roles: RoleFlags.Pusher);
            b.AddPiece(u, "U", 13);
            b.AddPiece(crate, "A", crateA);
            b.AddPiece(crate, "B", 8);
            b.AddPiece(avatar, "P", 2);
            return b.Build();
        }

        var topo = Build(14);
        // Both crates have to match; the carrier and the pusher may end anywhere.
        var goal = new PolyominoDomain<ulong>(topo, PolyominoState<ulong>.FromTopology(Build(32)), 1UL << 1);
        return new Mode(goal, new CompositeDomain<ulong>(goal, maxPushDepth: 3));
    }

    /// <summary>Frictionless ice: pieces glide until a wall, the edge or another piece stops them.</summary>
    public static Mode CreateIce()
    {
        var b = PuzzleTopology<ulong>.CreateBuilder(Width, Width);
        b.AddWall(0, 4);
        b.AddWall(2, 0);
        b.AddWall(5, 3);
        int glider = b.AddGroup("Ice Glider", [(0, 0)], physics: SlidePhysics.FrictionlessRaycast);
        int bar = b.AddGroup("Ice Slider Bar", [(0, 0), (1, 0)], physics: SlidePhysics.FrictionlessRaycast);
        int block = b.AddGroup("Ice L-Block", [(0, 0), (1, 0), (1, 1)], physics: SlidePhysics.FrictionlessRaycast);
        b.AddPiece(glider, "G", 0);
        b.AddPiece(bar, "I", 19);
        b.AddPiece(block, "L", 15);
        var goal = new PolyominoDomain<ulong>(b.Build(), glider, 20);
        return new Mode(goal, new CompositeDomain<ulong>(goal));
    }

    // ---- Shunting -------------------------------------------------------------------------------------------

    /// <summary>
    /// A small yard: a main line 0-4, a siding 5-7 forking off slot 2, an engine behind two wagons, and the goal of getting wagon A to the end of
    /// the main line.
    /// </summary>
    public static ShuntingDomain<ulong> CreateYard() => ShuntingMap.CreateDomain<ulong>(
    [
        "track 8",
        "join 0.1 1.0",
        "join 1.1 2.0",
        "join 2.1 3.0",
        "join 3.1 4.0",
        "join 2.1 5.0",
        "join 5.1 6.0",
        "join 6.1 7.0",
        "start EAB.....",
        "goal ....A...",
    ], "EAB", engines: "E");
}
