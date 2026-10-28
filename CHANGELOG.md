# Changelog

All notable changes are listed here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
follows [Semantic Versioning](https://semver.org/). While the version is below 1.0.0 the public API may still change between minor versions.

## [Unreleased]

### Added

- Shunting fouling sets: `ShuntingTrackBuilder.AddFoulingSet`, `ShuntingTrack.FoulingSets` and the `foul` keyword in `ShuntingMap`. At most one slot
  of a set may hold a piece, so a wagon left just past a switch blocks the other leg.
- `ShuntingMetric.DirectionChanges`: Hordern's count of changes of direction. Each engine remembers the sense it last moved in, so setting off from
  rest, handing over to another engine and resuming in the same sense are free; an engine pushed or pulled back by another changes direction too.

### Fixed

- `PolyominoState<TBitboard>.FindReachableAnchors` and `ReconstructHookPath` throw `ArgumentOutOfRangeException` for a start anchor outside the board
  instead of indexing out of range.
- `PlaySession<TState, TMove>` no longer requires `TMove : IEquatable<TMove>`, which it never used.
- `ToGrid` separates rows with `
` on every platform, not `Environment.NewLine`.
- The `ShuntingMap` documentation said a piece without a `front` line faces end 1; it faces end 0, as `ShuntingGroup.StartFronts` says.

## [0.1.0] - 2026-10-27

The first release of the package.

### Added

- Bitboard state structs and rules for the 4x4 N-puzzle (`NPuzzleState`, `NPuzzleDomain`, with Manhattan distance and a solvability test), Klotski
  (`KlotskiState`, `KlotskiDomain`), lane-sliding puzzles of up to 64 vehicles on 128 cells (`LaneSlideDomain<TState, TBitboard>`), Soko pushing
  (`PushDomain`) and polyominoes (`PolyominoDomain<TBitboard>`, `CompositeDomain<TBitboard>`).
- Railway shunting (Hordern chapter J): `ShuntingTrack`, `ShuntingDomain<TBitboard>` and `ShuntingMap`, with switches, wyes, turntables, clearance masks, engine-run costs, and per-group rules (pushed-only wagons, permanent units, engines without a front coupler, carrying limits). `ShuntingMap` accepts several `goal` lines, which add up.
- Polyomino mechanics: hex grids, two elevation layers, one-way cells, partitions, pressure plates, three slide physics (single step, flood fill, glide
  until blocked), rotation rings, pushing, carrying and magnetic clusters.
- `PuzzleSolver`: breadth-first search, A* and IDA*, plus Dijkstra and weighted A* for domains with move costs (`IWeightedDomain`), with cancellation, a node limit and a `SolveStatus` that says whether a search found nothing
  because there is no solution or because it hit the limit.
- `PuzzleWalk` (random walks for solvable shuffles) and `PlaySession<TState, TMove>` (undo, redo and reset).
- `E2e4.SlidingBlock.Logic.Text`: `TopologyMap`, `LaneDiagram` and `PushMap` read boards, vehicles and warehouses from diagrams with the symbols the
  caller chooses.
- Documentation in `docs/`, including a [roadmap](docs/roadmap.md) of what the engine does not cover yet.
- A WPF application with three laboratories (not packed) that shows the bitboards and the solvers working.
