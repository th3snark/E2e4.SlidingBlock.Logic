namespace E2e4.SlidingBlock.Logic;

/// <summary>How a shunting puzzle counts moves (see <see cref="ShuntingCost"/> for the numbers).</summary>
public enum ShuntingMetric
{
    /// <summary>A move is one run of an engine in one direction: it counts again when an engine starts, reverses or hands over to another engine. A turntable turn counts as a move too.</summary>
    EngineReversals,

    /// <summary>Only a turntable turn is a move. Engine runs between turns are free to count, but cost a secondary reversal when an engine starts or reverses.</summary>
    TurntableRotations,
}

/// <summary>
/// A set of interchangeable pieces on a shunting track: the wagons of one kind, or a single engine. Pieces of a group are not told apart, so
/// swapping two of them is no move. Engines always track which way they face; wagons only if <see cref="TrackHeading"/> is set.
/// </summary>
/// <param name="Name">A name the consumer can show; the engine does not read it.</param>
/// <param name="IsEngine">True for a self-propelled engine. An engine group has exactly one piece.</param>
/// <param name="Start">The slots the pieces start on.</param>
public sealed record ShuntingGroup(string Name, bool IsEngine, IReadOnlyList<int> Start)
{
    /// <summary>The slots the pieces may occupy (a siding that admits wagons but not engines, say); null means anywhere.</summary>
    public IReadOnlyList<int>? Allowed { get; init; }

    /// <summary>For each starting piece, the end (0 or 1) of its slot its front faces; null means 0 for all.</summary>
    public IReadOnlyList<int>? StartFronts { get; init; }

    /// <summary>True if the goal or the rules depend on which way the pieces face. Always true for engines.</summary>
    public bool TrackHeading { get; init; }
}

/// <summary>The slots a group must end on: every piece of group <paramref name="Group"/> must stand on one of <paramref name="Slots"/>.</summary>
/// <param name="Group">The group number, counting from 0 in the order the groups were given.</param>
/// <param name="Slots">The slots the group's pieces must all be on.</param>
public sealed record ShuntingGroupGoal(int Group, IReadOnlyList<int> Slots);

/// <summary>A heading requirement: some piece must stand on <paramref name="Slot"/> with its front facing end <paramref name="Front"/>.</summary>
/// <param name="Slot">The slot to check.</param>
/// <param name="Front">The end (0 or 1) the piece's front must face.</param>
public sealed record ShuntingFrontGoal(int Slot, int Front);

/// <summary>When a shunting puzzle is solved: every listed group is on its slots and every listed heading holds. Momentum plays no part.</summary>
/// <param name="Groups">The groups that must reach given slots.</param>
public sealed record ShuntingGoal(IReadOnlyList<ShuntingGroupGoal> Groups)
{
    /// <summary>Slots that must hold a piece facing a given way, for turn-round goals; empty by default.</summary>
    public IReadOnlyList<ShuntingFrontGoal> Fronts { get; init; } = [];
}

/// <summary>Packs and unpacks the cost the shunting domain reports: primary moves, then secondary reversals, then slot steps.</summary>
public static class ShuntingCost
{
    private const int PrimaryShift = 40;
    private const int SecondaryShift = 20;
    private const long Field = (1L << SecondaryShift) - 1;

    /// <summary>Packs three counts, each below 2^20 (the primary below 2^23), into one comparable number.</summary>
    public static long Pack(int primary, int secondary, int steps) =>
        ((long)primary << PrimaryShift) | ((long)secondary << SecondaryShift) | (long)steps;

    /// <summary>The moves Hordern counts: engine runs or turntable turns.</summary>
    public static int Primary(long cost) => (int)(cost >> PrimaryShift);

    /// <summary>The secondary count: how many times an engine started, reversed or handed over to another engine.</summary>
    public static int Secondary(long cost) => (int)((cost >> SecondaryShift) & Field);

    /// <summary>The single-slot steps the engines took.</summary>
    public static int Steps(long cost) => (int)(cost & Field);
}
