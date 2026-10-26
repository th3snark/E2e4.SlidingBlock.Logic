# Changelog

All notable changes are listed here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
follows [Semantic Versioning](https://semver.org/). While the version is below 1.0.0 the public API may still change between minor versions.

## [Unreleased]

The first release of the package.

### Added

- Bitboard state structs and rules for the 4x4 N-puzzle (`NPuzzleState`, `NPuzzleDomain`, with Manhattan distance and a solvability test), Klotski
  (`KlotskiState`, `KlotskiDomain`), lane-sliding puzzles of up to 64 vehicles on 128 cells (`LaneSlideDomain<TState, TBitboard>`), Soko pushing
  (`PushDomain`) and polyominoes (`PolyominoDomain<TBitboard>`, `CompositeDomain<TBitboard>`).
- Polyomino mechanics: hex grids, two elevation layers, one-way cells, partitions, pressure plates, three slide physics (single step, flood fill, glide
  until blocked), rotation rings, pushing, carrying and magnetic clusters.
- `PuzzleSolver`: breadth-first search, A* and IDA*, with cancellation, a node limit and a `SolveStatus` that says whether a search found nothing
  because there is no solution or because it hit the limit.
- `PuzzleWalk` (random walks for solvable shuffles) and `PlaySession<TState, TMove>` (undo, redo and reset).
- `E2e4.SlidingBlock.Logic.Text`: `TopologyMap`, `LaneDiagram` and `PushMap` read boards, vehicles and warehouses from diagrams with the symbols the
  caller chooses.
- Documentation in `docs/`, including a [roadmap](docs/roadmap.md) of what the engine does not cover yet.
- A WPF application with three laboratories (not packed) that shows the bitboards and the solvers working.
