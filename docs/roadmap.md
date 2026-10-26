# Roadmap: what the engine could still cover

Nothing here is promised or scheduled. It is a map of the gaps, found by reading the engine against Edward Hordern's *Sliding Piece Puzzles*
(Oxford University Press, 1986) and against the solvers and puzzle libraries that exist elsewhere. Each gap says what is missing, how it would fit the
existing `ISlidingBlockDomain<TState, TMove>` interface, and roughly how much work it is. "Covered" means the engine's mechanics can play the
puzzle; the layouts themselves belong to the application that uses the engine, never to the engine.

## 1. Hordern's categories

Hordern sorts his puzzles into sliding block puzzles (chapters A to G), Soko (H), railway shunting (J) and puzzles with plungers or levers
where pieces move in groups ([Rob's Puzzle Page](https://robspuzzlepage.com/sliding.htm) summarises the classification).

| Chapter | Content | Coverage | What is missing |
|---------|---------|----------|-----------------|
| A, random arrangement | Same-shape counters, any scramble, fixed goal | Mostly | A solvability test for a scramble (now `NPuzzleDomain.IsSolvable`). Latin-square goals ("each row has distinct symbols") need a goal *predicate*, not a target position. |
| B, uniform pieces | The 15-puzzle family, letter tiles | Mostly | The fast `NPuzzleState` is fixed at 4x4 with one blank: no 8-puzzle, no 24-puzzle, no several blanks. The polyomino path covers small multi-blank boards. |
| C, rectangular pieces | Dad's Puzzle, L'Âne Rouge | Covered | Only the move metric (section 3). |
| D, non-convex pieces | L and T shapes | Covered | The limit of 16 pieces and 16 groups could bind on large boards. |
| E, restricted routes | Lanes, one-way moves, fixed pieces | Partly | Lanes, diodes, partitions and direction masks are there. Missing: knight or jump moves, and counters moving along lines between spots on a graph that is not a grid. |
| F, special shapes and rotation | Odd shapes, turning pieces | Partly | Rotation turns only inside a clear square box. Triangular cells are not supported. |
| G, three dimensions | Pieces slide between layers | Not covered | The two elevation layers are a height overlap, not a third axis. A real domain needs width x height x depth cells and six directions; a 4x4x4 board is exactly 64 cells, so the `ulong` path would fit. |
| H, Soko | Push only | Covered (one metric) | No player-move metric, no pull or reverse play, no several storemen. |
| J, railway shunting | Engine plus wagons on a track graph, coupling | Not covered | A track graph with points, coupled trains that move as one unit, and an engine that both pushes and pulls ([Wikipedia](https://en.wikipedia.org/wiki/Train_shunting_puzzle), [arXiv 1810.07970](https://ar5iv.arxiv.org/html/1810.07970)). Best done as its own state type (wagon order per siding), not a bitboard. |
| Plungers and levers | Groups of pieces move together | Partly | Clusters of up to four pieces exist (`CompositeMove`). Moving a whole row or column at once is not there. |

Panex, the stacking puzzle in which pieces may not drop below a fixed height, needs a per-piece lowest-row limit (a small extension of the valid-anchor
masks). Its huge solutions (31,537 moves at level 10, [Jaap's Puzzle Page](https://www.jaapsch.net/puzzles/panex.htm)) would also make a good test of a
disk-backed search.

## 2. New mechanics and domains

| Priority | Gap | Value | Effort | How it fits |
|----------|-----|-------|--------|-------------|
| 1 | **Tilt / gravity**: every piece slides as far as it can in one direction at once | A whole family of popular puzzles | Low to medium | A domain with at most four moves per position, reusing the glide of the polyomino domain. |
| 2 | **Ricochet Robots, Lunar Lockout, Atomix**: slide until blocked | High; these are PSPACE-complete ([Hesterberg and Kopinsky](https://ar5iv.labs.arxiv.org/html/2402.11440)) | Low | Mostly present as `FrictionlessRaycast` groups. Needs a goal of "any robot of colour c on cell x" (a `PolyominoGoal` variant). |
| 3 | **Bigger and smaller N-puzzles, several blanks** | The standard research benchmarks | Medium | Make `NPuzzleState` size-generic: 5x5 needs 25 tiles of 5 bits, which fits a `UInt128`. |
| 4 | **Soko variants**: player-move metric, pull, Multiban, Hexoban | Medium to high | Medium | Pull is the reverse of `PushDomain`. `sokoenginepy` implements four tilings plus Multiban ([PyPI](https://pypi.org/project/sokoenginepy/)). |
| 5 | **Torus and row-rotation puzzles** (Loopover, the Sixteen Puzzle) | Medium | Low | A permutation domain where a move rotates a row or column one step ([Algorithms 2012](https://mdpi.com/1999-4893/5/1/18)). |
| 6 | **Colour-matching goals** (same-coloured tiles anywhere) | Medium | Low | A "colour class per cell" goal beside `PolyominoGoal`. |
| 7 | **Jump moves** (peg solitaire, Four Frogs) | Medium | Low to medium | Peg solitaire is its own domain (33 cells fit a `ulong`); knight moves are extra delta tables in `GridGeometry`. |
| 8 | **Railway shunting** | A whole Hordern chapter | High | A separate domain with a track graph and ordered wagon lists. |
| 9 | **True 3D** | Low to medium | Medium | A third axis in `GridGeometry`: six deltas, entry masks per face. |
| 10 | **Rolling block** | Low | Low | A small domain: a block that is upright or lying along x or y ([PSPACE-completeness](https://ar5iv.arxiv.org/html/1411.5951)). |
| 11 | **Triangular grids, diagonal lanes** | Low | Medium | Direction deltas that depend on the parity of the cell. |

## 3. Solver and search features

| Priority | Gap | Value | Effort | Notes |
|----------|-----|-------|--------|-------|
| 1 | **Move metrics as a parameter** (one cell, one piece any distance, a whole line) | High: published solution lengths depend on the metric | Medium | L'Âne Rouge is 81 moves when a two-cell slide counts as one, against 85 to 116 under other counts ([aschmied/klotski-solver](https://github.com/aschmied/klotski-solver)). The domains mix metrics today: Klotski single step, polyomino one turn, lane `maxStep`. |
| 2 | **Pattern databases and linear conflict** for the N-puzzle | High | Medium | `NPuzzleState.InverseMap` was built for O(1) lookups but nothing uses it yet. Additive databases make the 24-puzzle feasible ([Felner, Korf and Hanan, JAIR 2004](https://jair.org/index.php/jair/article/view/10391)); linear conflict alone cuts IDA* a long way ([arXiv 1411.6593](https://arxiv.org/pdf/1411.6593)). |
| 3 | **Weighted edges** (an optional `IWeightedDomain.MoveCost`) | Medium | Low to medium | Needed for the metrics above and for Soko pushes against player steps. |
| 4 | **Bidirectional BFS** | High for Klotski, Rush Hour and garages | Low | Needs predecessor moves; every pure-slide domain is reversible and can reuse `GenerateMoves`. |
| 5 | **Memory-lean search**: frontier search, a compact closed list, disk-based BFS | Medium to high | Medium | `PuzzleSolver` keeps every state it meets, so `MaxNodes` is the practical ceiling. |
| 6 | **Symmetry reduction** (mirror boards) | Medium | Low to medium | An optional `Canonicalize(state)`. Many Klotski layouts are left-right symmetric, which roughly halves the space. |
| 7 | **IDA\* transposition table** | Medium | Low | IDA* prunes only the move back to the parent today. |
| 8 | **Parallel search** | Medium | Medium to high | Parallel IDA* at the root, or BFS layers split by hash. The domains are immutable, so they are safe to share. |
| 9 | **Stronger Soko search**: corral pruning, freeze deadlocks | Medium for large warehouses | High | Today there are dead squares and a lower bound only. Festival and FESS solve large benchmark sets that way ([FESS paper](https://webdocs.cs.ualberta.ca/~jonathan/publications/ai_publications/fess.pdf), [Festival](https://festival-solver.site/)). |

## 4. Analysis and generation tooling

| Priority | Gap | Value | Effort | Fit |
|----------|-----|-------|--------|-----|
| 1 | **Whole-state-space enumeration**: distance histogram, farthest positions, reachable count, components | High: puzzle grading, hint quality, checking published figures | Low to medium | A `PuzzleAnalysis.Explore(domain, start)` over the BFS machinery without the goal test. |
| 2 | **Distance tables** by backward search from the goal set | High for instant hints | Medium | An exact distance per state; also gives perfect pattern databases for small domains. |
| 3 | **Hardest-puzzle generator** | Medium to high | Medium | Fogleman enumerated every Rush Hour position and kept the farthest from any goal in their cluster ([michaelfogleman.com/rush](https://michaelfogleman.com/rush)). The layout enumerator would live outside the engine. |
| 4 | **Solution post-processing** | Medium | Low | Shorten or recount a found solution under another metric by replaying it. |

## 5. Other libraries

There is no generic, reusable sliding-block library on NuGet, npm, PyPI or crates.io that this engine could be measured against: what exists solves one
puzzle. The nearest peers are Soko-only (`sokoenginepy`, the Rust crate `soukoban`), Spaans's 2009 BFS/A*/IDA* solver ([spec-cs.pdf](https://www.pvv.ntnu.no/%7Espaans/spec-cs.pdf)) and Fogleman's Rush Hour work. What they have that this engine lacks is pattern databases,
whole-space analysis and generation, and Soko deadlock detection. The engine's range of mechanics (hex grids, elevation layers, one-way cells, partitions, rotation, push and carry) is wider than any of them.

## Suggested order

1. Move metrics and weighted edges, then bidirectional BFS.
2. A state-space explorer and distance tables.
3. A size-generic N-puzzle with linear conflict and pattern databases.
4. The tilt domain and goals for Ricochet Robots.
5. Soko pull and player-move metrics, plus freeze-deadlock pruning.
6. Panex height limits and the torus domain.
7. Railway shunting and true 3D.
