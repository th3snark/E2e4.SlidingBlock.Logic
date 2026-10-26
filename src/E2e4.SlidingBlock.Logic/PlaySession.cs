namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// The bookkeeping every interactive puzzle needs, independent of which puzzle it is: the current position, the position the game
/// started from, and an undo stack. It knows the moves of its
/// <see cref="ISlidingBlockDomain{TState,TMove}"/> but nothing about the puzzle's goal, rules beyond that, or appearance.
/// </summary>
public sealed class PlaySession<TState, TMove>
    where TState : struct, IEquatable<TState>
    where TMove : struct, IEquatable<TMove>
{
    private readonly Stack<TState> _history = new();
    private readonly Stack<TState> _undone = new();

    /// <summary>Starts a session at <paramref name="start"/> on <paramref name="domain"/>.</summary>
    public PlaySession(ISlidingBlockDomain<TState, TMove> domain, TState start)
    {
        ArgumentNullException.ThrowIfNull(domain);
        Domain = domain;
        State = Start = start;
    }

    /// <summary>The rules the session plays by.</summary>
    public ISlidingBlockDomain<TState, TMove> Domain { get; }

    /// <summary>The position the game began from; <see cref="Reset"/> returns here.</summary>
    public TState Start { get; private set; }

    /// <summary>The current position.</summary>
    public TState State { get; private set; }

    /// <summary>Moves played since the game began or was reset, net of undone ones.</summary>
    public int MoveCount => _history.Count;

    /// <summary>True if there is a move to take back.</summary>
    public bool CanUndo => _history.Count > 0;

    /// <summary>True if a move has been taken back and no new move has been played since.</summary>
    public bool CanRedo => _undone.Count > 0;

    /// <summary>True if the current position is a goal of the domain.</summary>
    public bool IsSolved => Domain.IsGoal(State);

    /// <summary>Starts a new game from <paramref name="start"/>, forgetting history.</summary>
    public void Begin(TState start)
    {
        State = Start = start;
        _history.Clear();
        _undone.Clear();
    }

    /// <summary>Returns to the position this game started from.</summary>
    public void Reset() => Begin(Start);

    /// <summary>
    /// Plays a move the caller has taken from <see cref="ISlidingBlockDomain{TState,TMove}.GenerateMoves"/>. The session does not check that
    /// the move is legal here, and not thread-safe: use one session per thread.
    /// </summary>
    public void Apply(TMove move)
    {
        _history.Push(State);
        _undone.Clear();
        State = Domain.ApplyMove(State, move);
    }

    /// <summary>Takes back the last move; false if there is none.</summary>
    public bool Undo()
    {
        if (_history.Count == 0) return false;
        _undone.Push(State);
        State = _history.Pop();
        return true;
    }

    /// <summary>Replays the move last taken back; false if there is none.</summary>
    public bool Redo()
    {
        if (_undone.Count == 0) return false;
        _history.Push(State);
        State = _undone.Pop();
        return true;
    }
}
