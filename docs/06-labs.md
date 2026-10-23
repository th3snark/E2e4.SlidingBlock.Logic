# 6. The WPF laboratories

`E2e4.SlidingBlock.Logic.Labs.Wpf` is a private, unpacked desktop app (Windows, `net10.0-windows`) that makes the bitboards
visible. It uses the library only through its public types.

## Lab 1: standard architectures

A mode selector switches between three puzzles, each with an interactive board, legal-move arrows, and a live inspector.

| Mode | Register shown | Notes |
|------|----------------|-------|
| 15-puzzle | 16 nibbles of the `ulong` | starts from a 30-move scramble; hints use IDA* + Manhattan |
| Klotski | 20 three-bit shape codes (the canonical key) | classic 4x5 layout; hints use BFS |
| Rush Hour | one 3-bit offset per vehicle | custom puzzle, red car must reach offset 4; hints use BFS |

The inspector has three parts: the **packed state register** (bits that changed on the last move flash), the **occupancy bitboard** as a
`0/1` grid, and a **formula trace** showing the exact bitwise expressions of the last move with real operands (for example the nibble
XOR mask, or `(other & target) == 0`).

Buttons: `Random Valid Move`, `Solve Step (BFS Hint)` (applies the next move of an optimal solution, computed off the UI thread and
cached), `Reset Board`.

## Lab 2: polyominoes and hook moves

A 5x5 board (bits `0..24`) with a static wall on square 12:

```
 0  1  2  3  4        L  .  A  .  J
 5  6  7  8  9        L  L  .  J  J
10 11 12 13 14        .  .  ##  .  B        L = Master L-tromino   (group 0)  anchor 0
15 16 17 18 19        .  T  .  .  .        T = Upward T-tetromino (group 1)  anchor 16
20 21 22 23 24        T  T  T  .  .        J = J-tromino          (group 2)  anchor 4
                                           A, B = identical 1x1  (group 3)  anchors 2 and 14
```

| Feature | What it demonstrates |
|---------|----------------------|
| Gold star on each piece's anchor cell | the anchor is `tzcnt(mask)`: the T and J anchor on their top stem, not the empty bounding-box corner |
| Reachable-destination rings | `FindReachableAnchors`; green = 1 step, teal = straight slide, orange = corner hook (dashed trajectory on hover) |
| Multi-segment animation | the piece follows `ReconstructHookPath` cell by cell |
| `Show ValidAnchorsMask[g] Overlay` | green/red badges on all 25 cells for the selected piece; column 0 is red for T and J |
| `Swap Identical 1x1 Pieces` | verifies `GroupAnchors[3]`, `ZobristHash` and `Equals` are unchanged by the swap |
| Selected-piece breakdown | group id, `BaseMask`, anchor, shifted mask, `ValidAnchorsMask`, reachable mask as hex, binary and 5x5 grids |
| Canonical state panel | `GroupAnchors[0..3]`, occupancy, `ZobristHash`, and the `Z[g,old] ^ Z[g,new]` XOR of the last move |

The goal is to bring the master L to anchor 16. A BFS hint solves it in two moves, because hook moves make the board very open.

## Reference tab

An in-app summary of the proofs in this folder and the citations in [references.md](references.md).

## Building

```
dotnet build E2e4.SlidingBlock.Logic.slnx
dotnet run --project src/E2e4.SlidingBlock.Logic.Labs.Wpf
```
