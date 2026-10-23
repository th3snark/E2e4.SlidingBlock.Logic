# E2e4.SlidingBlock.Logic

<img src="assets/icon.png" alt="E2e4.SlidingBlock.Logic icon" width="96" height="96" align="right">

A small, dependency-free sliding block puzzle engine for .NET: zero-allocation, bitboard-driven state structs and generic solvers
for the N-puzzle, Klotski, Rush Hour and general polyominoes. It targets `net10.0`. Install from NuGet:
`E2e4.SlidingBlock.Logic`.

Every puzzle is an `ISlidingBlockDomain<TState, TMove>` (initial state, goal test, move generation, move application and a 64-bit state hash),
and `PuzzleSolver` solves any of them with BFS, A* or IDA*.

```csharp
var start = NPuzzleDomain.Scramble(25, new Random(3));
var domain = new NPuzzleDomain(start);

SolveResult<NPuzzleMove>? result = new PuzzleSolver().SolveIdaStar(domain, start, NPuzzleDomain.Manhattan);
Console.WriteLine(result?.Moves.Count);

var rushHour = RushHourDomain.CreateDefault();
Console.WriteLine(new PuzzleSolver().SolveBfs(rushHour, rushHour.InitialState)?.Moves.Count);
```

## What is in it

- `NPuzzleState`: the 4x4 sliding tile puzzle packed into 16 nibbles of a `ulong`, with a branchless XOR tile swap and Manhattan distance.
- `KlotskiState` and `KlotskiDomain`: Klotski with a canonical shape key, so interchangeable pieces are the same position.
- `RushHourState` and `RushHourDomain`: vehicles as lane offsets packed into a `ulong`.
- `PolyominoState` and `PolyominoDomain`: non-rectangular pieces (L, T, J, S, Z, U) on a bitboard, with multi-step corner-hook slides and a
  permutation-invariant Zobrist hash.
- `PuzzleTopology` and `PuzzleTopologyBuilder`: build a board from an ASCII map or relative cell lists, with LSB normalisation and precomputed
  valid-anchor masks.
- `PuzzleSolver`: generic BFS, A* and IDA*.

`E2e4.SlidingBlock.Logic.Labs.Wpf` in the repository is a WPF application with two laboratories that show the bitboards and the solvers
working. It is not part of the package.

## Build and test

```
dotnet test E2e4.SlidingBlock.Logic.slnx
```

See [SECURITY.md](https://github.com/th3snark/E2e4.SlidingBlock.Logic/blob/master/SECURITY.md) to report a vulnerability.

## Licence

MIT, see [LICENSE](https://github.com/th3snark/E2e4.SlidingBlock.Logic/blob/master/LICENSE). No dependencies.
