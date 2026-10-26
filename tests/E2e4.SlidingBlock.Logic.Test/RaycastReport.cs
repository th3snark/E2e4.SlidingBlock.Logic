namespace E2e4.SlidingBlock.Logic.Test;

/// <summary>One glide of a piece in one direction, described in words.</summary>
public readonly record struct RaycastMoveResult(int DirectionDelta, string DirectionName, int StartAnchor, int TerminalAnchor, int StepCount,
    ulong SweptPathCellsMask, string StopReason, ulong BlockingBitsMask);

/// <summary>Names what stopped each glide the engine traced, so the tests can say it in words.</summary>
public static class RaycastReport
{
    private static readonly string[] Names = ["Left", "Right", "Up", "Down"];

    /// <summary>The glide of <paramref name="pieceId"/> in every direction its group allows; a glide of zero steps means the piece cannot move that way.</summary>
    public static IReadOnlyList<RaycastMoveResult> For(PolyominoDomain<ulong> domain, in PolyominoState<ulong> state, int pieceId)
    {
        var topo = domain.Topology;
        Span<RaycastTrace<ulong>> traces = stackalloc RaycastTrace<ulong>[6];
        int n = domain.TraceRaycast(state, pieceId, traces);
        var list = new List<RaycastMoveResult>(n);
        for (int i = 0; i < n; i++)
        {
            var t = traces[i];
            string reason = t.BlockingBitsMask == 0 ? "Board Edge"
                : (t.BlockingBitsMask & topo.StaticWallsMask) != 0 ? "Static Bumper Wall"
                : (t.BlockingBitsMask & topo.ForbiddenEntryCells(t.Direction)) != 0 ? "Partition"
                : "Piece Collision";
            list.Add(new RaycastMoveResult(topo.GetDirectionDeltas()[t.Direction], Names[t.Direction], t.StartAnchor, t.TerminalAnchor, t.StepCount,
                t.SweptCellsMask, reason, t.BlockingBitsMask));
        }
        return list;
    }
}
