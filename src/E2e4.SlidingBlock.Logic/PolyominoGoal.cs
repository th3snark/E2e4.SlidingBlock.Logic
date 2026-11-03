using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// When a polyomino puzzle is solved. Shared by <see cref="PolyominoDomain{TBitboard}"/> and <see cref="CompositeDomain{TBitboard}"/>, which differ in
/// how pieces move but not in what they are asked to reach.
/// </summary>
public sealed class PolyominoGoal<TBitboard> where TBitboard : unmanaged, IBitboard<TBitboard>
{
    private readonly PolyominoState<TBitboard> _position;
    private readonly ulong _matchedGroups;

    /// <summary>Solved when group <paramref name="groupId"/> has exactly one piece, on <paramref name="anchor"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="groupId"/> is negative or not below <see cref="PolyominoState{TBitboard}.MaxGroups"/>, or <paramref name="anchor"/> is not a
    /// bit of the bitboard.
    /// </exception>
    public PolyominoGoal(int groupId, int anchor)
    {
        if ((uint)groupId >= PolyominoState<TBitboard>.MaxGroups)
            throw new ArgumentOutOfRangeException(nameof(groupId), groupId, $"A position holds at most {PolyominoState<TBitboard>.MaxGroups} groups.");
        if ((uint)anchor >= (uint)Bit<TBitboard>.Width)
            throw new ArgumentOutOfRangeException(nameof(anchor), anchor, $"The anchor is not one of the {Bit<TBitboard>.Width} cells of the bitboard.");
        GroupId = groupId;
        Anchor = anchor;
    }

    /// <summary>
    /// Solved when the position equals <paramref name="position"/>. Pieces of one group are interchangeable, so only which group sits where
    /// matters (see <see cref="PolyominoState{TBitboard}"/>). Bit g of <paramref name="matchedGroups"/> says whether group g has to match;
    /// groups left out may end up anywhere. Build the position with <see cref="PolyominoState{TBitboard}.FromTopology"/> from a topology
    /// that has the same groups, in the same order, as the one it is played on.
    /// </summary>
    public PolyominoGoal(PolyominoState<TBitboard> position, ulong matchedGroups = ulong.MaxValue)
    {
        GroupId = Anchor = -1;
        _position = position;
        _matchedGroups = matchedGroups;
        HasPosition = true;
    }

    /// <summary>Group of the single-group goal; -1 when the goal is a whole position.</summary>
    public int GroupId { get; }

    /// <summary>Anchor of the single-group goal; -1 when the goal is a whole position.</summary>
    public int Anchor { get; }

    /// <summary>True if the goal is a whole position rather than one group on one anchor.</summary>
    public bool HasPosition { get; }

    /// <summary>The goal position when there is one, otherwise null.</summary>
    public PolyominoState<TBitboard>? Position => HasPosition ? _position : null;

    /// <summary>Throws when a single-group goal names a group or a cell that <paramref name="topology"/> does not have.</summary>
    internal void CheckFits(PuzzleTopology<TBitboard> topology, string paramName)
    {
        if (HasPosition) return;
        if (GroupId >= topology.Groups.Count)
            throw new ArgumentException($"The goal names group {GroupId}, but the topology has {topology.Groups.Count} groups.", paramName);
        if (Anchor >= topology.CellCount)
            throw new ArgumentException($"The goal anchor {Anchor} is not a cell of the {topology.CellCount}-cell board.", paramName);
    }

    /// <summary>True if <paramref name="state"/> meets the goal.</summary>
    public bool IsReached(in PolyominoState<TBitboard> state)
    {
        if (!HasPosition) return state.GroupAnchorsOf(GroupId).Equals(Bit<TBitboard>.One.ShiftLeft(Anchor));
        if (_matchedGroups == ulong.MaxValue) return state.Equals(_position);
        for (ulong m = _matchedGroups; m != 0; m &= m - 1)
        {
            int g = BitOps.TrailingZeroCount(m);
            if (g >= GroupAnchorBuffer<TBitboard>.Length) break;
            if (!state.GroupAnchorsOf(g).Equals(_position.GroupAnchorsOf(g))) return false;
        }
        return true;
    }
}
