# E2e4.SlidingBlock.Logic

<img src="assets/icon.png" alt="E2e4.SlidingBlock.Logic icon" width="96" height="96" align="right">

A small, dependency-free sliding block puzzle engine for .NET: zero-allocation, bitboard-driven state structs and generic solvers
for the N-puzzle, Klotski, Rush Hour and general polyominoes. It targets `net10.0`. Install from NuGet:
`E2e4.SlidingBlock.Logic`.

Every puzzle is an `ISlidingBlockDomain<TState, TMove>` (initial state, goal test, move generation, move application and a 64-bit state hash),
and `PuzzleSolver` solves any of them with BFS, A* or IDA*.

```csharp
// The engine does not know where the blank belongs: the puzzle supplies its goal position.
var goal = new NPuzzleState(0xFEDCBA9876543210UL);
var domain = new NPuzzleDomain(goal, goal);
var start = PuzzleWalk.RandomWalk(domain, goal, 25, new Random(3));

SolveResult<NPuzzleMove>? result = new PuzzleSolver().SolveIdaStar(domain, start, domain.ManhattanDistance);
Console.WriteLine(result?.Moves.Count);

// Rush Hour: the vehicles are read off an ASCII diagram, and the red car (the first symbol) has to reach the far end of its row.
var symbols = LaneVehicle.SymbolsOf(rows, leadingLabels: "X");
var rushHour = LaneSlide.RushHour(rows, symbols);
Console.WriteLine(new PuzzleSolver().SolveBfs(rushHour, rushHour.InitialState)?.Moves.Count);
```

## What is in it

- `NPuzzleState`: the 4x4 sliding tile puzzle packed into 16 nibbles of a `ulong`, with a branchless XOR tile swap. The goal position is a constructor argument, and the Manhattan distance is measured against it.
- `KlotskiState` and `KlotskiDomain`: Klotski with a canonical shape key, so interchangeable pieces are the same position.
- `LaneState16`, `LaneState64` and `LaneSlideDomain<TState, TBitboard>`: vehicles as lane offsets packed 4 bits each (up to 64 vehicles on 128 cells); Rush Hour moves one cell, garage puzzles slide any distance.
- `PushState` and `PushDomain`: Soko warehouse pushing, where the storeman's walking is free, with dead squares and a heuristic.
- `PolyominoState` and `PolyominoDomain`: non-rectangular pieces (L, T, J, S, Z, U) on a bitboard, with multi-step corner-hook slides and a
  permutation-invariant Zobrist hash.
- `PuzzleTopology` and `PuzzleTopologyBuilder`: build a board from an ASCII map or relative cell lists, with LSB normalisation and precomputed
  valid-anchor masks.
- `PuzzleSolver`: generic BFS, A* and IDA*.
- `PuzzleWalk` and `PlaySession<TState, TMove>`: a generic random walk (for solvable shuffles) and the undo / reset bookkeeping
  every interactive puzzle needs.

The engine contains no puzzle: no layouts, no goal positions, no colours. A puzzle (the sibling `E2e4.SlidingBlock.Puzzles` app, or the
Labs) defines its layout and goal and hands them to the domain as data.

`E2e4.SlidingBlock.Logic.Labs.Wpf` in the repository is a WPF application with two laboratories that show the bitboards and the solvers
working. It is not part of the package.

## Documentation

The research and design notes behind the object model (representations, LSB normalisation, valid anchors and hook moves, Zobrist
hashing, with diagrams) are in [docs](https://github.com/th3snark/E2e4.SlidingBlock.Logic/blob/master/docs/README.md).

## Build and test

```
dotnet test E2e4.SlidingBlock.Logic.slnx
```

See [SECURITY.md](https://github.com/th3snark/E2e4.SlidingBlock.Logic/blob/master/SECURITY.md) to report a vulnerability.

## Licence

MIT, see [LICENSE](https://github.com/th3snark/E2e4.SlidingBlock.Logic/blob/master/LICENSE). No dependencies.
