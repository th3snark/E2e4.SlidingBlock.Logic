using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

/// <summary>
/// A railway shunting puzzle (Hordern chapter J) on a <see cref="ShuntingTrack"/>: engines push and pull wagons along a track of unit slots, and the
/// goal is to bring named groups of wagons to given slots. The domain moves a train one slot at a time; what a move costs depends on whether the
/// engine carries on, so solve it with <see cref="PuzzleSolver.SolveDijkstra{TState,TMove}(IWeightedDomain{TState,TMove},TState,CancellationToken)"/>
/// or <see cref="PuzzleSolver.SolveWeightedAStar{TState,TMove}(IWeightedDomain{TState,TMove},TState,Func{TState,long},CancellationToken)"/> and read the
/// result with <see cref="ShuntingCost"/>. Breadth-first search counts single-slot steps only.
/// </summary>
/// <remarks>
/// <para>
/// An engine step moves the engine one slot out of one of its ends. Everything in front of it is pushed one slot ahead (the push stops at the first
/// empty slot and is illegal against a dead end or a slot a piece may not enter). Couplers can be made and broken at will while the train stands,
/// so the engine may also pull any number of the pieces directly behind it. At a switch the move names the branch it takes, so one position can
/// have several moves that differ only in the branch. A train, pushed and pulled together, is at most 32 pieces long. No move leaves two pieces
/// in one of the track's fouling sets.
/// </para>
/// <para>
/// A train never doubles back over a slot it already covers in the same step, so pushes round a closed loop of wagons are not generated.
/// </para>
/// </remarks>
public sealed class ShuntingDomain<TBitboard> : IWeightedDomain<ShuntingState<TBitboard>, ShuntingMove>
    where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>
{
    private const int MaxTrain = 32;
    private const int MaxActiveEdges = 4;

    private readonly ShuntingTrack _track;
    private readonly ShuntingGroup[] _groups;
    private readonly int[] _engines;
    private readonly bool[] _tracked;
    private readonly bool[] _pullable;
    private readonly int[] _unit;
    private readonly bool[] _frontCoupler;
    private readonly int[] _maxCarried;
    private readonly TBitboard[] _allowed;
    private readonly TBitboard[] _fouling;
    private readonly TBitboard[] _goalSlots;
    private readonly bool[] _hasGoal;
    private readonly ulong[] _zSlot;
    private readonly ulong[] _zHead;
    private readonly ulong[] _zAlign;
    private readonly ulong[] _zMomentum;

    /// <summary>Builds the puzzle from a track, the groups of pieces and where they start, and a goal.</summary>
    /// <param name="track">The track.</param>
    /// <param name="groups">The groups, numbered from 0 in this order; at most <see cref="ShuntingState{TBitboard}.MaxGroups"/>.</param>
    /// <param name="goal">What counts as solved.</param>
    /// <param name="metric">How moves are counted.</param>
    /// <param name="startAlignments">The starting alignment of each turntable; null means 0 for all.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The track does not fit the bitboard, a switch has too many branches, a group, start position or goal is invalid, or the start puts two pieces in one fouling set.</exception>
    public ShuntingDomain(ShuntingTrack track, IReadOnlyList<ShuntingGroup> groups, ShuntingGoal goal,
        ShuntingMetric metric = ShuntingMetric.EngineReversals, IReadOnlyList<int>? startAlignments = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(goal);
        _track = track;
        Metric = metric;
        Goal = goal;
        int slots = track.SlotCount;
        if (slots > Grid.BitWidth<TBitboard>()) throw new ArgumentException($"A track of {slots} slots needs a wider bitboard than {typeof(TBitboard).Name}.", nameof(track));
        if (track.TurntableCount > ShuntingState<TBitboard>.MaxTurntables) throw new ArgumentException($"At most {ShuntingState<TBitboard>.MaxTurntables} turntables.", nameof(track));
        for (int t = 0; t < track.TurntableCount; t++)
        {
            if (track.AlignmentCount(t) > 16) throw new ArgumentException($"Turntable {t} has more than 16 alignments.", nameof(track));
        }
        CheckBranching(track);
        if (groups.Count is 0 or > ShuntingState<TBitboard>.MaxGroups) throw new ArgumentException($"A puzzle needs 1 to {ShuntingState<TBitboard>.MaxGroups} groups.", nameof(groups));
        _groups = [.. groups];

        _tracked = new bool[_groups.Length];
        _pullable = new bool[_groups.Length];
        _unit = new int[_groups.Length];
        _frontCoupler = new bool[_groups.Length];
        _maxCarried = new int[_groups.Length];
        _allowed = new TBitboard[_groups.Length];
        var engines = new List<int>();
        for (int g = 0; g < _groups.Length; g++)
        {
            var group = _groups[g] ?? throw new ArgumentException($"Group {g} is null.", nameof(groups));
            if (group.Start is null) throw new ArgumentException($"Group {g} has no start slots.", nameof(groups));
            if (group.IsEngine)
            {
                if (group.Start.Count != 1) throw new ArgumentException($"Engine group {g} must have exactly one piece.", nameof(groups));
                engines.Add(g);
            }
            _tracked[g] = group.IsEngine || group.TrackHeading;
            _pullable[g] = group.CanBePulled;
            _unit[g] = group.Unit;
            _frontCoupler[g] = group.FrontCoupler;
            if (group.Unit < -1) throw new ArgumentException($"Group {g} has unit {group.Unit}; use -1 for none.", nameof(groups));
            if (group.MaxCarried is < 0) throw new ArgumentException($"Group {g} may carry {group.MaxCarried} pieces.", nameof(groups));
            _maxCarried[g] = group.MaxCarried ?? int.MaxValue;
            _allowed[g] = group.Allowed is null ? AllSlots(slots) : MaskOf(group.Allowed, slots, $"Group {g} allows", nameof(groups));
        }
        _engines = [.. engines];
        _fouling = [.. track.FoulingSets.Select(set => MaskOf(set, slots, "A fouling set uses", nameof(track)))];

        var rng = new Random(0x5417);
        ulong Next() => ((ulong)rng.NextInt64() << 1) ^ (ulong)rng.NextInt64();
        _zSlot = new ulong[_groups.Length * slots];
        _zHead = new ulong[slots];
        _zAlign = new ulong[ShuntingState<TBitboard>.MaxTurntables * 16];
        _zMomentum = new ulong[_groups.Length * 2];
        foreach (ulong[] table in new[] { _zSlot, _zHead, _zAlign, _zMomentum })
        {
            for (int i = 0; i < table.Length; i++) table[i] = Next();
        }

        InitialState = BuildStart(startAlignments);
        if (!FoulingClear(InitialState.OccupancyBits)) throw new ArgumentException("The start puts two pieces in one fouling set.", nameof(groups));

        _goalSlots = new TBitboard[_groups.Length];
        _hasGoal = new bool[_groups.Length];
        foreach (var gg in goal.Groups)
        {
            if ((uint)gg.Group >= (uint)_groups.Length) throw new ArgumentException($"The goal names group {gg.Group}, which does not exist.", nameof(goal));
            if (_hasGoal[gg.Group]) throw new ArgumentException($"The goal names group {gg.Group} twice.", nameof(goal));
            _hasGoal[gg.Group] = true;
            _goalSlots[gg.Group] = MaskOf(gg.Slots, slots, $"The goal of group {gg.Group} uses", nameof(goal));
        }
        foreach (var fg in goal.Fronts)
        {
            if ((uint)fg.Slot >= (uint)slots) throw new ArgumentException($"The goal checks slot {fg.Slot}, which is outside the track.", nameof(goal));
            if (fg.Front is not (0 or 1)) throw new ArgumentException($"A front is end 0 or 1, not {fg.Front}.", nameof(goal));
        }

        int turns = 0;
        for (int t = 0; t < track.TurntableCount; t++) turns += track.AlignmentCount(t);
        MaxMoves = checked(_engines.Length * 2 * slots * slots + turns);
    }

    /// <summary>The track the puzzle runs on.</summary>
    public ShuntingTrack Track => _track;

    /// <summary>How moves are counted.</summary>
    public ShuntingMetric Metric { get; }

    /// <summary>What counts as solved.</summary>
    public ShuntingGoal Goal { get; }

    /// <inheritdoc/>
    public ShuntingState<TBitboard> InitialState { get; }

    /// <inheritdoc/>
    public int MaxMoves { get; }

    /// <inheritdoc/>
    public bool IsGoal(in ShuntingState<TBitboard> state)
    {
        for (int g = 0; g < _groups.Length; g++)
        {
            if (_hasGoal[g] && (state.Anchors[g] & ~_goalSlots[g]) != TBitboard.Zero) return false;
        }
        foreach (var fg in Goal.Fronts)
        {
            if (!Grid.Has(state.OccupancyBits, fg.Slot) || (Grid.Has(state.HeadingBits, fg.Slot) ? 1 : 0) != fg.Front) return false;
        }
        return true;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The buffer is smaller than <see cref="MaxMoves"/> and a position needs more room.</exception>
    public int GenerateMoves(in ShuntingState<TBitboard> state, Span<ShuntingMove> moveBuffer)
    {
        Span<int> scratch = stackalloc int[(MaxTrain + 2) * 5];
        int w = MaxTrain + 2;
        var walk = new Walk
        {
            D = this,
            S = state,
            Out = moveBuffer,
            FSlot = scratch[..w],
            FExit = scratch.Slice(w, w),
            FEnter = scratch.Slice(2 * w, w),
            BSlot = scratch.Slice(3 * w, w),
            BTrail = scratch.Slice(4 * w, w),
        };
        foreach (int g in _engines)
        {
            int u = Grid.Lsb(state.Anchors[g]);
            for (int lead = 0; lead < 2; lead++)
            {
                walk.Engine = g;
                walk.Lead = lead;
                walk.FChoices = 0;
                walk.RChoices = 0;
                walk.Visited = Bit(u);
                walk.FSlot[0] = u;
                walk.FExit[0] = lead;
                walk.Front(0);
            }
        }
        for (int t = 0; t < _track.TurntableCount; t++)
        {
            for (int a = 0; a < _track.AlignmentCount(t); a++)
            {
                if (a == state.Alignment(t)) continue;
                var turn = new ShuntingMove(ShuntingMove.TurnMover, (byte)t, (byte)a, 0, 0, 0);
                if (_fouling.Length > 0 && !FoulingClear(ApplyMove(state, turn).OccupancyBits)) continue;
                walk.Add(turn);
            }
        }
        return walk.N;
    }

    /// <inheritdoc/>
    public ShuntingState<TBitboard> ApplyMove(in ShuntingState<TBitboard> state, in ShuntingMove move)
    {
        var next = state;
        if (move.IsTurn)
        {
            Turn(ref next, state, move);
            return next;
        }

        Span<int> from = stackalloc int[2 * MaxTrain];
        Span<int> to = stackalloc int[2 * MaxTrain];
        Span<int> exit = stackalloc int[2 * MaxTrain];
        Span<int> enter = stackalloc int[2 * MaxTrain];
        Span<int> group = stackalloc int[2 * MaxTrain];
        int count = Trace(state, move, from, to, exit, enter);
        for (int i = 0; i < count; i++)
        {
            int g = group[i] = GroupAt(state, from[i]);
            TBitboard both = Bit(from[i]) | Bit(to[i]);
            next.Anchors[g] ^= both;
            next.OccupancyBits ^= both;
            next.Hash ^= _zSlot[g * _track.SlotCount + from[i]] ^ _zSlot[g * _track.SlotCount + to[i]];
        }
        for (int i = 0; i < count; i++)
        {
            if (!Grid.Has(state.HeadingBits, from[i])) continue;
            next.HeadingBits &= ~Bit(from[i]);
            next.Hash ^= _zHead[from[i]];
        }
        for (int i = 0; i < count; i++)
        {
            if (!_tracked[group[i]]) continue;
            int front = Grid.Has(state.HeadingBits, from[i]) ? 1 : 0;
            if (exit[i] == enter[i]) front ^= 1;
            if (front == 0) continue;
            next.HeadingBits |= Bit(to[i]);
            next.Hash ^= _zHead[to[i]];
        }

        if (state.Engine != ShuntingState<TBitboard>.NoEngine) next.Hash ^= _zMomentum[state.Engine * 2 + state.SenseBit];
        next.Engine = move.Mover;
        next.SenseBit = (byte)SenseOf(state, move);
        next.Hash ^= _zMomentum[next.Engine * 2 + next.SenseBit];
        return next;
    }

    /// <inheritdoc/>
    public ulong GetHashCode64(in ShuntingState<TBitboard> state) => state.Hash;

    /// <summary>
    /// The pieces <paramref name="move"/> shifts when played from <paramref name="state"/>, as (from, to) slot pairs. For an engine step these are the
    /// engine, the pieces it pushes and the pieces it pulls, each one slot on; for a turntable turn, the pieces on the bridge when it turns round
    /// (none if the turn only changes the alignment). A caller that follows individual pieces, as a view that animates them does, needs this
    /// because pieces of one group are not told apart by the state.
    /// </summary>
    /// <exception cref="ArgumentException">The move does not belong to this position.</exception>
    public IReadOnlyList<(int From, int To)> MovedPieces(in ShuntingState<TBitboard> state, in ShuntingMove move)
    {
        var pairs = new List<(int, int)>();
        if (move.IsTurn)
        {
            int turntable = move.Turntable;
            if (_track.IsInverted(turntable, state.Alignment(turntable)) == _track.IsInverted(turntable, move.Alignment)) return pairs;
            var bridge = _track.BridgeSlots(turntable);
            for (int i = 0; i < bridge.Count; i++)
            {
                if (Grid.Has(state.OccupancyBits, bridge[i])) pairs.Add((bridge[i], bridge[bridge.Count - 1 - i]));
            }
            return pairs;
        }
        Span<int> from = stackalloc int[2 * MaxTrain];
        Span<int> to = stackalloc int[2 * MaxTrain];
        Span<int> exit = stackalloc int[2 * MaxTrain];
        Span<int> enter = stackalloc int[2 * MaxTrain];
        int count = Trace(state, move, from, to, exit, enter);
        for (int i = 0; i < count; i++) pairs.Add((from[i], to[i]));
        return pairs;
    }

    /// <inheritdoc/>
    public long MoveCost(in ShuntingState<TBitboard> state, in ShuntingMove move)
    {
        if (move.IsTurn) return ShuntingCost.Pack(1, 0, 1);
        bool carriesOn = state.Engine == move.Mover && state.SenseBit == SenseOf(state, move);
        bool atRest = state.Engine == ShuntingState<TBitboard>.NoEngine;
        return Metric switch
        {
            ShuntingMetric.EngineReversals => carriesOn ? ShuntingCost.Pack(0, 0, 1) : ShuntingCost.Pack(1, 1, 1),
            ShuntingMetric.DirectionChanges => carriesOn ? ShuntingCost.Pack(0, 0, 1) : atRest ? ShuntingCost.Pack(0, 1, 1) : ShuntingCost.Pack(1, 1, 1),
            _ => ShuntingCost.Pack(0, carriesOn || atRest ? 0 : 1, 1),
        };
    }

    private static TBitboard Bit(int slot) => TBitboard.One << slot;

    /// <summary>True if no fouling set holds more than one piece of <paramref name="occupancy"/>.</summary>
    private bool FoulingClear(TBitboard occupancy)
    {
        foreach (var mask in _fouling)
        {
            var held = occupancy & mask;
            if ((held & (held - TBitboard.One)) != TBitboard.Zero) return false;
        }
        return true;
    }

    private static TBitboard AllSlots(int slots) => (TBitboard.One << (slots - 1)) | ((TBitboard.One << (slots - 1)) - TBitboard.One);

    private static TBitboard MaskOf(IReadOnlyList<int> slotList, int slots, string what, string paramName)
    {
        var mask = TBitboard.Zero;
        foreach (int s in slotList)
        {
            if ((uint)s >= (uint)slots) throw new ArgumentException($"{what} slot {s}, which is outside the track of {slots} slots.", paramName);
            mask |= Bit(s);
        }
        return mask;
    }

    /// <summary>Every end may have at most <see cref="MaxActiveEdges"/> edges at once: the plain ones plus, per turntable, the most any one alignment adds.</summary>
    private static void CheckBranching(ShuntingTrack track)
    {
        for (int slot = 0; slot < track.SlotCount; slot++)
        {
            for (int end = 0; end < 2; end++)
            {
                int plain = 0;
                var perAlignment = new int[track.TurntableCount, 16];
                foreach (var e in track.EdgesFrom(slot, end))
                {
                    if (e.Turntable < 0) plain++;
                    else perAlignment[e.Turntable, e.Alignment]++;
                }
                int total = plain;
                for (int t = 0; t < track.TurntableCount; t++)
                {
                    int most = 0;
                    for (int a = 0; a < 16; a++) most = Math.Max(most, perAlignment[t, a]);
                    total += most;
                }
                if (total > MaxActiveEdges) throw new ArgumentException($"Slot {slot} end {end} can lead to more than {MaxActiveEdges} slots at once.", nameof(track));
            }
        }
    }

    private ShuntingState<TBitboard> BuildStart(IReadOnlyList<int>? startAlignments)
    {
        var state = new ShuntingState<TBitboard> { Engine = ShuntingState<TBitboard>.NoEngine };
        int slots = _track.SlotCount;
        if (startAlignments is not null)
        {
            if (startAlignments.Count != _track.TurntableCount) throw new ArgumentException("Give one starting alignment per turntable.", nameof(startAlignments));
            for (int t = 0; t < startAlignments.Count; t++)
            {
                if ((uint)startAlignments[t] >= (uint)_track.AlignmentCount(t)) throw new ArgumentException($"Turntable {t} has no alignment {startAlignments[t]}.", nameof(startAlignments));
                state.Alignments |= (ulong)startAlignments[t] << (4 * t);
            }
        }
        for (int t = 0; t < _track.TurntableCount; t++) state.Hash ^= _zAlign[t * 16 + state.Alignment(t)];

        for (int g = 0; g < _groups.Length; g++)
        {
            var group = _groups[g];
            if (group.StartFronts is { } fronts && fronts.Count != group.Start.Count) throw new ArgumentException($"Group {g} needs one front per piece.");
            for (int i = 0; i < group.Start.Count; i++)
            {
                int slot = group.Start[i];
                if ((uint)slot >= (uint)slots) throw new ArgumentException($"Group {g} starts on slot {slot}, which is outside the track.");
                if (Grid.Has(state.OccupancyBits, slot)) throw new ArgumentException($"Two pieces start on slot {slot}.");
                if (!Grid.Has(_allowed[g], slot)) throw new ArgumentException($"Group {g} starts on slot {slot}, which it may not enter.");
                state.Anchors[g] |= Bit(slot);
                state.OccupancyBits |= Bit(slot);
                state.Hash ^= _zSlot[g * slots + slot];
                int front = group.StartFronts?[i] ?? 0;
                if (front is not (0 or 1)) throw new ArgumentException($"A front is end 0 or 1, not {front}.");
                if (front == 1 && _tracked[g])
                {
                    state.HeadingBits |= Bit(slot);
                    state.Hash ^= _zHead[slot];
                }
            }
        }
        return state;
    }

    private bool IsActive(in ShuntingState<TBitboard> state, in ShuntingEdge edge) =>
        edge.Turntable < 0 || state.Alignment(edge.Turntable) == edge.Alignment;

    private int GroupAt(in ShuntingState<TBitboard> state, int slot)
    {
        for (int g = 0; g < _groups.Length; g++)
        {
            if (Grid.Has(state.Anchors[g], slot)) return g;
        }
        return -1;
    }

    /// <summary>The direction of travel relative to the engine's own front: 0 front first, 1 rear first.</summary>
    private int SenseOf(in ShuntingState<TBitboard> state, in ShuntingMove move)
    {
        int front = Grid.Has(state.HeadingBits, Grid.Lsb(state.Anchors[move.Mover])) ? 1 : 0;
        return front == move.Lead ? 0 : 1;
    }

    private ShuntingEdge EdgeAt(in ShuntingState<TBitboard> state, int slot, int end, int choice)
    {
        int c = 0;
        foreach (var e in _track.EdgesFrom(slot, end))
        {
            if (!IsActive(state, e)) continue;
            if (c++ == choice) return e;
        }
        throw new ArgumentException("The move does not belong to this position.");
    }

    /// <summary>Lists, for every piece the move shifts, the slot it leaves and enters and the ends it leaves and enters through.</summary>
    private int Trace(in ShuntingState<TBitboard> state, in ShuntingMove move, Span<int> from, Span<int> to, Span<int> exit, Span<int> enter)
    {
        int u = Grid.Lsb(state.Anchors[move.Mover]);
        int n = 0;
        int cur = u, curExit = move.Lead;
        for (int d = 0; d < move.FrontLength; d++)
        {
            var e = EdgeAt(state, cur, curExit, (int)((move.FrontChoices >> (2 * d)) & 3));
            from[n] = cur; to[n] = e.To; exit[n] = curExit; enter[n] = e.ToEnd; n++;
            cur = e.To;
            curExit = 1 - e.ToEnd;
        }
        cur = u;
        int trail = 1 - move.Lead;
        for (int d = 0; d < move.RearLength; d++)
        {
            var e = EdgeAt(state, cur, trail, (int)((move.RearChoices >> (2 * d)) & 3));
            from[n] = e.To; to[n] = cur; exit[n] = e.ToEnd; enter[n] = trail; n++;
            cur = e.To;
            trail = 1 - e.ToEnd;
        }
        return n;
    }

    private void Turn(ref ShuntingState<TBitboard> next, in ShuntingState<TBitboard> state, in ShuntingMove move)
    {
        int t = move.Turntable, old = state.Alignment(t), target = move.Alignment;
        next.Alignments = (state.Alignments & ~(15UL << (4 * t))) | ((ulong)target << (4 * t));
        next.Hash ^= _zAlign[t * 16 + old] ^ _zAlign[t * 16 + target];

        if (_track.IsInverted(t, old) != _track.IsInverted(t, target))
        {
            var bridge = _track.BridgeSlots(t);
            int slots = _track.SlotCount;
            foreach (int s in bridge)
            {
                int g = GroupAt(state, s);
                if (g < 0) continue;
                next.Anchors[g] ^= Bit(s);
                next.OccupancyBits ^= Bit(s);
                next.Hash ^= _zSlot[g * slots + s];
                if (Grid.Has(state.HeadingBits, s))
                {
                    next.HeadingBits &= ~Bit(s);
                    next.Hash ^= _zHead[s];
                }
            }
            for (int i = 0; i < bridge.Count; i++)
            {
                int s = bridge[i], m = bridge[bridge.Count - 1 - i];
                int g = GroupAt(state, s);
                if (g < 0) continue;
                next.Anchors[g] ^= Bit(m);
                next.OccupancyBits ^= Bit(m);
                next.Hash ^= _zSlot[g * slots + m];
                if (_tracked[g] && !Grid.Has(state.HeadingBits, s))
                {
                    next.HeadingBits |= Bit(m);
                    next.Hash ^= _zHead[m];
                }
            }
        }

        if (state.Engine != ShuntingState<TBitboard>.NoEngine) next.Hash ^= _zMomentum[state.Engine * 2 + state.SenseBit];
        next.Engine = ShuntingState<TBitboard>.NoEngine;
        next.SenseBit = 0;
    }

    /// <summary>The recursive search for trains: first the pushed front, then the pulled rear, writing a move for each way to do both.</summary>
    private ref struct Walk
    {
        public ShuntingDomain<TBitboard> D;
        public ShuntingState<TBitboard> S;
        public Span<ShuntingMove> Out;
        public int N, Engine, Lead, FLen;
        public ulong FChoices, RChoices;
        public TBitboard Visited;
        public Span<int> FSlot, FExit, FEnter, BSlot, BTrail;

        public void Add(ShuntingMove move)
        {
            if (N == Out.Length) throw new InvalidOperationException("The move buffer is too small for this position.");
            Out[N++] = move;
        }

        /// <summary>FSlot[depth] holds a piece that is about to leave through FExit[depth].</summary>
        public void Front(int depth)
        {
            int choice = 0;
            foreach (var e in D._track.EdgesFrom(FSlot[depth], FExit[depth]))
            {
                if (!D.IsActive(S, e)) continue;
                int c = choice++;
                if (Grid.Has(Visited, e.To) || depth + 1 > MaxTrain) continue;
                FSlot[depth + 1] = e.To;
                FEnter[depth + 1] = e.ToEnd;
                FChoices = (FChoices & ~(3UL << (2 * depth))) | ((ulong)c << (2 * depth));
                Visited |= Bit(e.To);
                if (Grid.Has(S.OccupancyBits, e.To))
                {
                    FExit[depth + 1] = 1 - e.ToEnd;
                    Front(depth + 1);
                }
                else
                {
                    FLen = depth + 1;
                    if (FrontIsLegal() && FLen - 1 <= D._maxCarried[Engine])
                    {
                        if (CutAllowed(FSlot[0], 1 - Lead) && FoulingClear(FSlot[0])) Add(new ShuntingMove((byte)Engine, (byte)Lead, (byte)FLen, 0, FChoices, 0));
                        RChoices = 0;
                        BSlot[0] = FSlot[0];
                        BTrail[0] = 1 - Lead;
                        if (CanPull()) Rear(0);
                    }
                }
                Visited &= ~Bit(e.To);
            }
        }

        /// <summary>True if no fouling set holds two pieces once the train has moved into FSlot[FLen] and left <paramref name="vacated"/> empty.</summary>
        private readonly bool FoulingClear(int vacated) =>
            D._fouling.Length == 0 || D.FoulingClear((S.OccupancyBits | Bit(FSlot[FLen])) & ~Bit(vacated));

        private readonly bool FrontIsLegal()
        {
            for (int i = 0; i < FLen; i++)
            {
                if (!Grid.Has(D._allowed[D.GroupAt(S, FSlot[i])], FSlot[i + 1])) return false;
            }
            return true;
        }

        /// <summary>An engine without a front coupler cannot pull what stands behind its front end, which is the case when it drives rear first.</summary>
        private readonly bool CanPull() =>
            D._frontCoupler[Engine] || 1 - Lead != (Grid.Has(S.HeadingBits, FSlot[0]) ? 1 : 0);

        /// <summary>The train may stop after <paramref name="slot"/> unless a piece of the same permanent unit stands directly behind it.</summary>
        private readonly bool CutAllowed(int slot, int trailEnd)
        {
            int unit = D._unit[D.GroupAt(S, slot)];
            if (unit < 0) return true;
            foreach (var e in D._track.EdgesFrom(slot, trailEnd))
            {
                if (D.IsActive(S, e) && Grid.Has(S.OccupancyBits, e.To) && D._unit[D.GroupAt(S, e.To)] == unit) return false;
            }
            return true;
        }

        /// <summary>BSlot[depth] is the last slot of the train; BTrail[depth] is the end of it that faces the pieces behind.</summary>
        private void Rear(int depth)
        {
            int choice = 0;
            foreach (var e in D._track.EdgesFrom(BSlot[depth], BTrail[depth]))
            {
                if (!D.IsActive(S, e)) continue;
                int c = choice++;
                if (!Grid.Has(S.OccupancyBits, e.To) || Grid.Has(Visited, e.To) || depth + 1 > MaxTrain) continue;
                int behind = D.GroupAt(S, e.To);
                if (!D._pullable[behind] || FLen - 1 + depth + 1 > D._maxCarried[Engine]) continue;
                if (!Grid.Has(D._allowed[behind], BSlot[depth])) continue;
                BSlot[depth + 1] = e.To;
                BTrail[depth + 1] = 1 - e.ToEnd;
                RChoices = (RChoices & ~(3UL << (2 * depth))) | ((ulong)c << (2 * depth));
                if (CutAllowed(e.To, 1 - e.ToEnd) && FoulingClear(e.To)) Add(new ShuntingMove((byte)Engine, (byte)Lead, (byte)FLen, (byte)(depth + 1), FChoices, RChoices));
                Visited |= Bit(e.To);
                Rear(depth + 1);
                Visited &= ~Bit(e.To);
            }
        }
    }
}
