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

    // ---- Lab 3 (extended mechanics) -------------------------------------------------------------------------

    public const int Lab3Width = 6;

    /// <summary>One Lab 3 board: its domain for play and solving, the plain polyomino domain that owns the goal, and what the player is told.</summary>
    public sealed record Lab3Mode(string Title, string Brief, PolyominoDomain Goal, CompositeDomain<ulong> Domain,
        IReadOnlyDictionary<string, string> Colors);

    /// <summary>Mode 1: an L-tromino turns through four orientation groups; a blocker sits in the empty corner of its 2x2 rotation box.</summary>
    public static Lab3Mode CreateLab3Rotation()
    {
        var b = PuzzleTopology.CreateBuilder(Lab3Width, Lab3Width);
        b.AddWall(4, 4);
        var ring = b.AddRotatableGroup("L-Tromino", [(0, 0), (1, 0), (1, 1)]);
        int blocker = b.AddGroup("1x1 Blocker", [(0, 0)]);
        b.AddPiece(ring[0], "L", 7);
        b.AddPiece(blocker, "A", 8);
        b.AddPiece(blocker, "B", 20);
        var goal = new PolyominoDomain(b.Build(), ring[3], 15);
        return new Lab3Mode("Orientation ring & swept-clearance rotation",
            "Rotate the L-tromino 90 degrees with the two rotation buttons. A turn needs the whole 2x2 rotation box free, so blocker A has to leave " +
            "the box first. Turning into the third orientation moves the LSB anchor by one square. Goal: the L in its fourth orientation (an upside-down L) on anchor 15 (dashed outline).",
            goal, new CompositeDomain<ulong>(goal), Lab3RotationColors);
    }

    public static readonly IReadOnlyDictionary<string, string> Lab3RotationColors = new Dictionary<string, string>
    {
        ["L-Tromino"] = "#F5B301",
        ["1x1 Blocker"] = "#10B981",
    };

    /// <summary>Mode 2: a hollow U carries the crate in its pocket; the pusher avatar shoves crates in a chain.</summary>
    public static Lab3Mode CreateLab3Carrier()
    {
        static PuzzleTopology Build(int crateA)
        {
            var b = PuzzleTopology.CreateBuilder(Lab3Width, Lab3Width);
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
        var goal = new PolyominoDomain(topo, PolyominoState.FromTopology(Build(32)), 1UL << 1);
        return new Lab3Mode("Coupled kinematics: hollow U-carrier & Sokoban push",
            "Slide the U-carrier or the pusher one square. The U drags the crate in its pocket along when it moves left, right or up, " +
            "and leaves it behind when it moves down, out of the open mouth. A pusher shoves up to three pieces in a row. Goal: crates on anchors 8 and 32.",
            goal, new CompositeDomain<ulong>(goal, maxPushDepth: 3), Lab3CarrierColors);
    }

    public static readonly IReadOnlyDictionary<string, string> Lab3CarrierColors = new Dictionary<string, string>
    {
        ["Hollow U-Carrier"] = "#3B82F6",
        ["Crate"] = "#F59E0B",
        ["Sokoban Pusher"] = "#EF4444",
    };

    /// <summary>Mode 3: frictionless ice. Pieces glide until a wall, the edge or another piece stops them.</summary>
    public static Lab3Mode CreateLab3Ice()
    {
        var b = PuzzleTopology.CreateBuilder(Lab3Width, Lab3Width);
        b.AddWall(0, 4);
        b.AddWall(2, 0);
        b.AddWall(5, 3);
        int glider = b.AddGroup("Ice Glider", [(0, 0)], physics: SlidePhysics.FrictionlessRaycast);
        int bar = b.AddGroup("Ice Slider Bar", [(0, 0), (1, 0)], physics: SlidePhysics.FrictionlessRaycast);
        int block = b.AddGroup("Ice L-Block", [(0, 0), (1, 0), (1, 1)], physics: SlidePhysics.FrictionlessRaycast);
        b.AddPiece(glider, "G", 0);
        b.AddPiece(bar, "I", 19);
        b.AddPiece(block, "L", 15);
        var goal = new PolyominoDomain(b.Build(), glider, 20);
        return new Lab3Mode("Frictionless raycast: ricochet physics",
            "Pick a piece and a direction: it glides until the next square would leave the board, hit a bumper wall or hit another piece, " +
            "and cannot stop in between. Goal: the 1x1 glider on anchor 20 (dashed outline); use the other blocks as stoppers.",
            goal, new CompositeDomain<ulong>(goal), Lab3IceColors);
    }

    public static readonly IReadOnlyDictionary<string, string> Lab3IceColors = new Dictionary<string, string>
    {
        ["Ice Glider"] = "#22D3EE",
        ["Ice Slider Bar"] = "#A855F7",
        ["Ice L-Block"] = "#F5B301",
    };
}
