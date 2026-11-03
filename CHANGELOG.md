# Changelog

All notable changes are listed here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
follows [Semantic Versioning](https://semver.org/). While the version is below 1.0.0 the public API may still change between minor versions.

## [Unreleased]

### Added

- Multi-targeting: the library now builds for `net10.0`, `net8.0` and `netstandard2.0` (it was `net10.0` only), like E2e4.Chess.Logic. `netstandard2.0` depends on `System.Memory`; the others have no dependencies. The tests run on `net10.0`, `net8.0` and (on Windows) `net48`, and a second test project runs them against the `netstandard2.0` build on any OS.
- `IBitboard<TSelf>` with the structs `Bitboard64` (64 cells) and `Bitboard128` (128 cells, two `ulong` words, so no `UInt128` is needed on any target). They have the bitwise and shift operators and convert implicitly from `ulong`.

### Changed

- **Breaking:** the bitboard type parameter is constrained to `unmanaged, IBitboard<TBitboard>` instead of `IBinaryInteger<T>, IUnsignedNumber<T>`: write `PolyominoDomain<Bitboard64>` where it was `PolyominoDomain<ulong>`, and `Bitboard128` for `UInt128`. A wrong type argument is now a compile error. The bitboard diagnostics (`Lsb`, `Msb`, `HasBit`, `ToHex`, `ToBinary`, `ToGrid`) are extensions on every `IBitboard<T>`; `BitCount` is `PopCount()`.
- **Breaking:** every `PuzzleSolver.Solve*` method returns a `SolveResult<TMove>` with a `Status` (`Solved`, `Unsolvable`, `NodeLimit`) and an `IsSolved` flag, instead of `SolveResult?` plus an `out SolveStatus` overload. Check `IsSolved` where you compared with `null`.
- `ILaneState<TState>.Capacity` is an instance property instead of a static abstract one.
- `MoveDeltaBuffer`, `LayerOccupancyBuffer<T>` and `CompositeMove.Deltas` are internal, and `CompositeMove.Count` / `DirectionIndex` are get-only properties: read a move through `AsSpan()`.
- On the netstandard targets the Zobrist keys of `PushDomain` and `ShuntingDomain` come from the same seeded `Random` sequence as on .NET 6 and later, so hashes match across targets.
- **Breaking:** `KlotskiState.CanonicalShapeKey` and `SpreadCells`, `NPuzzleState.SlideMask` and `PolyominoState.ComputeZobrist` are internal: they expose a packing that a 1.x may change. Compare Klotski positions with `==`.
- **Breaking:** `FindReachableAnchors` and `ReconstructHookPath` moved from `PolyominoState<TBitboard>` to the static class `PolyominoFloodFill`, with the bitboard type as a method type argument: `PolyominoFloodFill.FindReachableAnchors<Bitboard64>(...)`, or inferred when a mask argument is a bitboard.

- Shunting fouling sets: `ShuntingTrackBuilder.AddFoulingSet`, `ShuntingTrack.FoulingSets` and the `foul` keyword in `ShuntingMap`. At most one slot
  of a set may hold a piece, so a wagon left just past a switch blocks the other leg.
- `ShuntingMetric.DirectionChanges`: Hordern's count of changes of direction. Each engine remembers the sense it last moved in, so setting off from
  rest, handing over to another engine and resuming in the same sense are free; an engine pushed or pulled back by another changes direction too.

### Fixed

- `LaneSlideDomain` throws `ArgumentNullException` for a null vehicle list and `ArgumentOutOfRangeException` for a board of fewer than one column or row.
- `CompositeDomain` rejects null arguments and a `maxPushDepth` below 1 (which silently turned pushing off).
- `PushDomain.IsFloor`, `IsTarget`, `IsAlive` and `PushDistance` throw `ArgumentOutOfRangeException` for a cell off the warehouse, and the cell-count error names its parameter.
- `PuzzleTopologyBuilder` rejects a null group name or piece label and a pressure plate whose masks reach past the board, and an oversized or empty
  board reports the dimension at fault; its methods document their exceptions.
- `KlotskiState.MaxPieces` and `MaxShapes` replace the bare 10 and 7, and the `KlotskiState` summary no longer says 20 bits.
- The Lab 1 Klotski formula trace and `docs/06-labs.md` describe the checks the engine actually makes (anchor column, valid anchors, free cells), not edge masks it does not have.
- `SolveIdaStar` throws `InsufficientExecutionStackException` when a solution is deep enough to overflow the stack, instead of ending the process.
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
