using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// When a polyomino puzzle is solved. Shared by <see cref="PolyominoDomain{TBitboard}"/> and <see cref="CompositeDomain{TBitboard}"/>, which differ in
/// how pieces move but not in what they are asked to reach.
/// </summary>
public sealed class PolyominoGoal<TBitboard> where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>
{
    private readonly PolyominoState<TBitboard> _position;
    private readonly ulong _matchedGroups;

    /// <summary>Solved when group <paramref name="groupId"/> has exactly one piece, on <paramref name="anchor"/>.</summary>
    public PolyominoGoal(int groupId, int anchor)
    {
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

    public int Anchor { get; }

    public bool HasPosition { get; }

    /// <summary>The goal position when there is one, otherwise null.</summary>
    public PolyominoState<TBitboard>? Position => HasPosition ? _position : null;

    public bool IsReached(in PolyominoState<TBitboard> state)
    {
        if (!HasPosition) return state.GroupAnchorsOf(GroupId) == TBitboard.One << Anchor;
        if (_matchedGroups == ulong.MaxValue) return state.Equals(_position);
        for (ulong m = _matchedGroups; m != 0; m &= m - 1)
        {
            int g = BitOperations.TrailingZeroCount(m);
            if (g >= GroupAnchorBuffer<TBitboard>.Length) break;
            if (state.GroupAnchorsOf(g) != _position.GroupAnchorsOf(g)) return false;
        }
        return true;
    }
}
