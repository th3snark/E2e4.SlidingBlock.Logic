namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// The bookkeeping every interactive puzzle needs, independent of which puzzle it is: the current position, the position the game
/// started from, an undo stack and a cached solver plan used for hints. It knows the moves of its
/// <see cref="ISlidingBlockDomain{TState,TMove}"/> but nothing about the puzzle's goal, rules beyond that, or appearance.
/// </summary>
public sealed class PlaySession<TState, TMove>
    where TState : struct, IEquatable<TState>
    where TMove : struct, IEquatable<TMove>
{
    private readonly Stack<TState> _history = new();
    private readonly Queue<TMove> _plan = new();

    public PlaySession(ISlidingBlockDomain<TState, TMove> domain, TState start)
    {
        Domain = domain;
        State = Start = start;
    }

    public ISlidingBlockDomain<TState, TMove> Domain { get; }

    /// <summary>The position the game began from; <see cref="Reset"/> returns here.</summary>
    public TState Start { get; private set; }

    public TState State { get; private set; }

    /// <summary>Moves played since the game began or was reset, net of undone ones.</summary>
    public int MoveCount => _history.Count;

    public bool CanUndo => _history.Count > 0;
    public bool IsSolved => Domain.IsGoal(State);

    /// <summary>Starts a new game from <paramref name="start"/>, forgetting history and any cached hint plan.</summary>
    public void Begin(TState start)
    {
        State = Start = start;
        _history.Clear();
        _plan.Clear();
    }

    /// <summary>Returns to the position this game started from.</summary>
    public void Reset() => Begin(Start);

    /// <summary>Plays a legal move. The cached hint plan survives only if the move is the one it expected.</summary>
    public void Apply(TMove move)
    {
        _history.Push(State);
        State = Domain.ApplyMove(State, move);
        if (_plan.Count > 0 && _plan.Peek().Equals(move)) _plan.Dequeue(); else _plan.Clear();
    }

    /// <summary>Takes back the last move; false if there is none.</summary>
    public bool Undo()
    {
        if (_history.Count == 0) return false;
        State = _history.Pop();
        _plan.Clear();
        return true;
    }

    /// <summary>
    /// The next move on a solution from the current position, or null if the position is solved or the search was cancelled or
    /// exhausted. <paramref name="solve"/> supplies the algorithm and heuristic (the puzzle's choice) and runs on a worker thread; the
    /// rest of the path is cached, so following the hints costs one search.
    /// </summary>
    public async Task<TMove?> HintAsync(Func<TState, CancellationToken, SolveResult<TMove>?> solve, CancellationToken ct = default)
    {
        if (IsSolved) return null;
        if (_plan.Count == 0)
        {
            var start = State;
            var result = await Task.Run(() => solve(start, ct), ct);
            if (result is null || result.Moves.Count == 0 || !start.Equals(State)) return null;
            foreach (var m in result.Moves) _plan.Enqueue(m);
        }
        return _plan.Peek();
    }
}
