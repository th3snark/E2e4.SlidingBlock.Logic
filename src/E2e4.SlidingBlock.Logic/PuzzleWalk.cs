namespace E2e4.SlidingBlock.Logic;

/// <summary>Generic walks over any <see cref="ISlidingBlockDomain{TState,TMove}"/>.</summary>
public static class PuzzleWalk
{
    /// <summary>
    /// Random walk of <paramref name="steps"/> moves from <paramref name="start"/>. Every position it returns is reachable from
    /// <paramref name="start"/> and therefore solvable back to it. A move that returns to the previous position is re-drawn
    /// (a few times) when there is a choice, so the walk does not simply shuffle back and forth. Stops early at a dead end.
    /// </summary>
    public static TState RandomWalk<TState, TMove>(ISlidingBlockDomain<TState, TMove> domain, TState start, int steps, Random rng)
        where TState : struct, IEquatable<TState>
    {
        var buffer = new TMove[domain.MaxMoves];
        var state = start;
        var previous = start;
        bool hasPrevious = false;
        for (int i = 0; i < steps; i++)
        {
            int n = domain.GenerateMoves(state, buffer);
            if (n == 0) break;
            TState next;
            int tries = 0;
            do next = domain.ApplyMove(state, buffer[rng.Next(n)]);
            while (n > 1 && hasPrevious && next.Equals(previous) && ++tries < 32);
            previous = state;
            hasPrevious = true;
            state = next;
        }
        return state;
    }
}
