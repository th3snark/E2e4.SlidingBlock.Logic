# Railway shunting

Hordern's chapter J: engines push and pull wagons along a track with switches, loops, wyes and turntables, and the aim is to bring wagons to given
places in as few engine runs (or turntable turns) as possible. `ShuntingDomain<TBitboard>` plays these puzzles; the layouts of the book belong to the
application, as for every other domain.

## The track

`ShuntingTrack` cuts the rails into unit **slots**, each holding at most one piece, numbered from 0. A slot has two ends, 0 and 1.
`ShuntingTrackBuilder.Connect(a, endA, b, endB)` joins two ends, in both directions:

- Ends of different numbers (`Connect(0, 1, 1, 0)`) make straight track.
- An end with several connections is a **switch**. A train leaving through it can take any branch; each branch is a separate move.
- Joining two ends of the same number (`Connect(0, 1, 1, 1)`) is the chord of a **wye**. A train that takes it comes out facing the other way round.
- A **turntable** has a bridge (one or more slots) and several alignments. `ConnectWhen` makes a join exist only in one alignment. An alignment can
  be marked as turned round by 180 degrees: turning to or from it reverses the pieces on the bridge and flips the way they face.
- A **fouling set** (`AddFoulingSet`) lists slots that overlap on the ground, of which at most one may hold a piece. The usual case is the first slot
  of each leg past a switch: a wagon left there blocks the other leg, so a train cannot drop it and push the rest down the other leg in one run.
  Without fouling sets a piece one slot past a switch is out of the way, which makes some puzzles easier than the real ones.

The number of slots is bounded by the bitboard: 64 with `Bitboard64`, 128 with `Bitboard128`.

## Pieces and moves

A **group** is a set of interchangeable pieces (the identical wagons of one kind, or a single engine). States are compared by which slots each group
holds, so swapping two identical wagons is not a move and not a different position.

One move is one engine moving one slot out of one end:

- everything in front is pushed one slot ahead; the push stops at the first empty slot, and is illegal against a dead end or into a slot a piece may
  not enter (a siding that admits wagons but not engines is a clearance mask on the engine group);
- couplers can be made or broken at will while the train stands, so the engine may also pull any number of the pieces directly behind it.

A train is at most 32 pieces long, and a train never covers a slot twice in one step, so a push round a closed loop of wagons is not generated.

Engines always track which way they face. Wagon groups do so only when asked (`TrackHeading`), which keeps symmetrical wagons from making false
distinctions after a trip over a wye or turntable.

## Cost

A Hordern move is a whole run of an engine in one direction, not a single slot, so the domain keeps single-slot moves and puts the counting in the cost
(`IWeightedDomain.MoveCost`). Momentum, which is the engine that moved last and whether it drove front first or rear first, is part of the state;
without it the same position reached by a continuing run and by a fresh start would be one and the same node.

The cost is three counts packed into a `long` so that comparing costs compares them in order: **primary** (the moves Hordern counts), **secondary**
(engine starts and reversals), and **steps** (slots travelled, which breaks ties towards the shortest physical route). `ShuntingCost` packs and
unpacks them.

| Metric | Engine step, same engine and sense | Engine step otherwise | Turntable turn |
|--------|------------------------------------|-----------------------|----------------|
| `EngineReversals` | (0, 0, 1) | (1, 1, 1) | (1, 0, 1) |
| `DirectionChanges` | (n, 0, 1) | (n, 1, 1) | (1, 0, 1) |
| `TurntableRotations` | (0, 0, 1) | (0, 1, 1); (0, 0, 1) when all engines are at rest | (1, 0, 1) |

A turntable turn also leaves every engine at rest. Under `DirectionChanges`, n is the number of engines the step sends the other way (see below).

`DirectionChanges` is Hordern's own count in chapter J ("every time the engine changes direction"). The state remembers, for each engine, the sense
it last moved in since it was at rest, whether it drove or was pushed or pulled by another engine. A step costs one for every engine it sends the
other way: the engine that drives, and any engine in its train. Setting off from rest is free (so the book's numbers are one less than
`EngineReversals` gives for a single engine), and so are handing over to another engine and resuming in the same sense. A turntable turn leaves
every engine at rest.

## Optional rules

Hordern's variations are properties of a group, set on `ShuntingGroup` (or by keyword in the text format):

| Property | Text keyword | Effect |
|----------|--------------|--------|
| `CanBePulled = false` | `pushonly W` | A wagon without couplers: it can be pushed, never pulled. |
| `Unit = n` | `joined 123` | Groups with the same unit are permanently joined; an engine never pulls part of a unit and leaves the rest behind. |
| `FrontCoupler = false` | `nofrontcoupler E` | An engine with no front coupler cannot pull anything while it drives rear first. |
| `MaxCarried = n` | `carries E 1` | An engine pushes and pulls at most `n` pieces besides itself in one step. |

A turntable turn that would split a permanent unit between the bridge and the track is not checked.

## Solving

Use `PuzzleSolver.SolveDijkstra` (or `SolveWeightedAStar` with a lower bound on the remaining cost). The cost of a result is `SolveResult.Cost`:
`ShuntingCost.Primary(result.Cost)` is the number of moves in Hordern's count. The breadth-first and A* solvers still work but count single slots.

## Reading a puzzle from text

`ShuntingMap.Parse` reads a small line-based description (`track`, `join`, `turntable`, `foul`, `start`, `goal`, `front`, `heading`, `allow`, `faces`, `align`);
see its documentation for the grammar. The symbols of the pieces are parameters.

```
track 8
join 0.1 1.0
join 1.1 2.0
join 2.1 3.0
join 3.1 4.0
join 2.1 5.0     ; a siding forks off slot 2
join 5.1 6.0
join 6.1 7.0
start EAB.....
goal ....A...
```

## Not covered

Pieces longer than one slot (a long engine is modelled as coupled unit pieces), pushes round a closed loop, turns that split a unit, an admissible heuristic for the weighted
A*, and the book's own layouts. Whether the details of each Hordern puzzle (which engine may enter which siding, what a move is) match this model is
checked against the book where the puzzle is implemented, not here.
