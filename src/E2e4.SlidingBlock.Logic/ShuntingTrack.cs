namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// One way off a slot: the slot it reaches, the end of that slot it enters, and the turntable alignment it needs (if any).
/// A train that leaves a slot through one end and enters the next through end <see cref="ToEnd"/> keeps travelling away from that end.
/// </summary>
/// <param name="To">The slot the edge reaches.</param>
/// <param name="ToEnd">The end (0 or 1) of <paramref name="To"/> the train enters through.</param>
/// <param name="Turntable">The turntable the edge belongs to, or -1 if the edge is always there.</param>
/// <param name="Alignment">The alignment of <paramref name="Turntable"/> in which the edge exists; ignored when the turntable is -1.</param>
public readonly record struct ShuntingEdge(int To, int ToEnd, int Turntable, int Alignment);

/// <summary>
/// An immutable railway track cut into unit slots, each with two ends (0 and 1). It records which slot end meets which, so switches are just
/// ends with several edges, and a wye is a connection that joins two ends of the same number. Turntables switch edges on and off by alignment.
/// Build one with <see cref="ShuntingTrackBuilder"/>.
/// </summary>
public sealed class ShuntingTrack
{
    /// <summary>The most slots a track may have: the width of the widest bitboard the engine supports (<see cref="UInt128"/>).</summary>
    public const int MaxSlots = 128;

    private readonly ShuntingEdge[][] _edges;
    private readonly int[][] _bridges;
    private readonly bool[][] _inverted;

    internal ShuntingTrack(int slotCount, ShuntingEdge[][] edges, int[][] bridges, bool[][] inverted)
    {
        SlotCount = slotCount;
        _edges = edges;
        _bridges = bridges;
        _inverted = inverted;
    }

    /// <summary>The number of slots, numbered from 0.</summary>
    public int SlotCount { get; }

    /// <summary>The number of turntables, numbered from 0.</summary>
    public int TurntableCount => _bridges.Length;

    /// <summary>The edges a train leaving <paramref name="slot"/> through <paramref name="end"/> can take, before turntable alignments are applied.</summary>
    public ReadOnlySpan<ShuntingEdge> EdgesFrom(int slot, int end) => _edges[slot * 2 + end];

    /// <summary>The slots on the bridge of <paramref name="turntable"/>, in order.</summary>
    public IReadOnlyList<int> BridgeSlots(int turntable) => _bridges[turntable];

    /// <summary>How many alignments <paramref name="turntable"/> has.</summary>
    public int AlignmentCount(int turntable) => _inverted[turntable].Length;

    /// <summary>
    /// True if the bridge of <paramref name="turntable"/> lies turned round by 180 degrees in <paramref name="alignment"/>. Turning from one
    /// alignment to another with a different value reverses whatever stands on the bridge.
    /// </summary>
    public bool IsInverted(int turntable, int alignment) => _inverted[turntable][alignment];
}

/// <summary>Collects the slots, connections and turntables of a <see cref="ShuntingTrack"/> and checks them.</summary>
public sealed class ShuntingTrackBuilder
{
    private readonly int _slotCount;
    private readonly List<ShuntingEdge>[] _edges;
    private readonly List<int[]> _bridges = [];
    private readonly List<bool[]> _inverted = [];

    /// <summary>Starts a track of <paramref name="slotCount"/> slots, numbered from 0, with no connections.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slotCount"/> is not between 1 and <see cref="ShuntingTrack.MaxSlots"/>.</exception>
    public ShuntingTrackBuilder(int slotCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slotCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slotCount, ShuntingTrack.MaxSlots);
        _slotCount = slotCount;
        _edges = new List<ShuntingEdge>[slotCount * 2];
        for (int i = 0; i < _edges.Length; i++) _edges[i] = [];
    }

    /// <summary>
    /// Joins end <paramref name="endA"/> of slot <paramref name="slotA"/> to end <paramref name="endB"/> of slot <paramref name="slotB"/>, both ways.
    /// Ends of different numbers make ordinary straight track; the same number twice makes the chord of a wye, which turns a train round.
    /// </summary>
    /// <exception cref="ArgumentException">A slot or end is out of range, the slots are the same, or the connection exists already.</exception>
    public ShuntingTrackBuilder Connect(int slotA, int endA, int slotB, int endB) => Add(-1, 0, slotA, endA, slotB, endB);

    /// <summary>As <see cref="Connect"/>, but the connection exists only while <paramref name="turntable"/> stands in <paramref name="alignment"/>.</summary>
    /// <exception cref="ArgumentException">As <see cref="Connect"/>, or the turntable or alignment does not exist.</exception>
    public ShuntingTrackBuilder ConnectWhen(int turntable, int alignment, int slotA, int endA, int slotB, int endB)
    {
        if ((uint)turntable >= (uint)_bridges.Count) throw new ArgumentException($"Turntable {turntable} does not exist.", nameof(turntable));
        if ((uint)alignment >= (uint)_inverted[turntable].Length) throw new ArgumentException($"Turntable {turntable} has no alignment {alignment}.", nameof(alignment));
        return Add(turntable, alignment, slotA, endA, slotB, endB);
    }

    /// <summary>
    /// Declares a turntable. Its bridge is the given slots, which the caller joins to each other with <see cref="Connect"/>; the alignments are
    /// numbered from 0 in the order of <paramref name="inverted"/>, which says for each whether the bridge is turned round there.
    /// </summary>
    /// <returns>The number of the new turntable.</returns>
    /// <exception cref="ArgumentException">The bridge is empty, repeats a slot, uses a slot of another turntable, or there are fewer than two alignments.</exception>
    public int AddTurntable(IReadOnlyList<int> bridgeSlots, IReadOnlyList<bool> inverted)
    {
        ArgumentNullException.ThrowIfNull(bridgeSlots);
        ArgumentNullException.ThrowIfNull(inverted);
        if (bridgeSlots.Count == 0) throw new ArgumentException("A turntable needs at least one bridge slot.", nameof(bridgeSlots));
        if (inverted.Count < 2) throw new ArgumentException("A turntable needs at least two alignments.", nameof(inverted));
        foreach (int slot in bridgeSlots)
        {
            CheckSlot(slot, nameof(bridgeSlots));
            if (_bridges.Any(b => b.Contains(slot)) || bridgeSlots.Count(s => s == slot) > 1)
                throw new ArgumentException($"Slot {slot} is on a bridge twice.", nameof(bridgeSlots));
        }
        _bridges.Add([.. bridgeSlots]);
        _inverted.Add([.. inverted]);
        return _bridges.Count - 1;
    }

    /// <summary>Checks the description and builds the track.</summary>
    public ShuntingTrack Build() =>
        new(_slotCount, [.. _edges.Select(e => e.ToArray())], [.. _bridges], [.. _inverted]);

    private ShuntingTrackBuilder Add(int turntable, int alignment, int slotA, int endA, int slotB, int endB)
    {
        CheckSlot(slotA, nameof(slotA));
        CheckSlot(slotB, nameof(slotB));
        CheckEnd(endA, nameof(endA));
        CheckEnd(endB, nameof(endB));
        if (slotA == slotB) throw new ArgumentException($"Slot {slotA} cannot be joined to itself.", nameof(slotB));
        var forward = new ShuntingEdge(slotB, endB, turntable, alignment);
        var backward = new ShuntingEdge(slotA, endA, turntable, alignment);
        var from = _edges[slotA * 2 + endA];
        if (from.Contains(forward)) throw new ArgumentException($"Slot {slotA} end {endA} is already joined to slot {slotB} end {endB}.", nameof(slotB));
        from.Add(forward);
        _edges[slotB * 2 + endB].Add(backward);
        return this;
    }

    private void CheckSlot(int slot, string name)
    {
        if ((uint)slot >= (uint)_slotCount) throw new ArgumentException($"Slot {slot} is outside the track of {_slotCount} slots.", name);
    }

    private static void CheckEnd(int end, string name)
    {
        if (end is not (0 or 1)) throw new ArgumentException($"A slot has ends 0 and 1, not {end}.", name);
    }
}
