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

    /// <summary>Starts a new game from <paramref name="start"/>, forgetting history.</summary>
    public void Begin(TState start)
    {
        State = Start = start;
        _history.Clear();
    }

    /// <summary>Returns to the position this game started from.</summary>
    public void Reset() => Begin(Start);

    /// <summary>Plays a legal move.</summary>
    public void Apply(TMove move)
    {
        _history.Push(State);
        State = Domain.ApplyMove(State, move);
    }

    /// <summary>Takes back the last move; false if there is none.</summary>
    public bool Undo()
    {
        if (_history.Count == 0) return false;
        State = _history.Pop();
        return true;
    }
}
