# 6. The WPF laboratories

`E2e4.SlidingBlock.Logic.Labs.Wpf` is a private, unpacked desktop app (Windows, `net10.0-windows`) that makes the bitboards
visible. It uses the library only through its public types. The puzzle layouts, goals and group colours it plays are defined in the Lab itself (`LabPuzzles.cs`), not in the library.

## Lab 1: standard architectures

A mode selector switches between three puzzles, each with an interactive board, legal-move arrows, and a live inspector.

| Mode | Register shown | Notes |
|------|----------------|-------|
| 15-puzzle | 16 nibbles of the `ulong` | starts from a 30-move scramble; hints use IDA* + Manhattan |
| Klotski | 20 three-bit shape codes (the canonical key) | classic 4x5 layout; hints use BFS |
| Rush Hour | one 3-bit offset per vehicle | the 8-vehicle layout from the brief (`LabPuzzles.CreateRushHour`), red car must reach offset 4; hints use BFS |

The Klotski register uses the shape classes `1 = 1x1`, `2 = 1x2 vertical`, `3 = 2x1 horizontal`, `4 = 2x2` (the legend order in
`LabPuzzles.CreateKlotski`).

The inspector has these parts:

- the **packed state register** in hex, binary and per-group form (cell/vehicle label, bits, decoded value); for the 15-puzzle the
  companion **inverse register** (tile -> cell, with `BlankCell = inverse & 0xF`) is shown too;
- a **mutation delta** list of the groups the last move changed, as `before -> after`, next to the flash on the changed bits;
- the **occupancy bitboard** as a `0/1` grid with bit indices, plus the hovered or last-moved piece's mask and `otherPieces = occ ^ pieceMask`;
- a **formula trace** showing the exact bitwise expressions of a move with real operands (nibble XOR mask; Klotski edge-mask and
  shifted-mask checks; Rush Hour offset extraction, neighbour-bit test and clear-and-OR update). Hovering a legal-move arrow previews
  the trace without playing the move.

Hovering a block or cell outlines its cells on the board, the matching register groups and the occupancy bits; hovering a register
group does the reverse. Every cell is labelled with its bit index and `(row, col)`.

Buttons: `Random Valid Move`, `Step Optimal (BFS Hint)` (applies the next move of an optimal solution, computed off the UI thread and
cached), `Undo Move`, `Scramble (Solvable)` (a random walk from the initial state), `Reset Board`.

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
| Dashed box on a selected T or J | the empty bounding-box origin, one column left of the real anchor |
| `Show ValidAnchorsMask[g] Overlay` | green/red badges on all 25 cells for the selected piece; hovering a cell explains why its anchor is valid or masked out (left/right overhang, bottom overflow, wall) |
| `Swap Identical 1x1 Pieces` | verifies `GroupAnchors[3]`, `ZobristHash` and `Equals` are unchanged by the swap |
| `Load Hook-Move Demo Preset` | puts block A above the wall and clears the right side, so anchor 17 needs a 4-step hook round bit 12 |
| `Random Valid Move`, `Undo Move`, `Reset Board` | undo replays the trajectory backwards and shows the XOR that restores the hash |
| Selected-piece breakdown | ids, `(dRow,dCol)` offsets, `BaseMask`, anchor, `PieceMask`, `ValidAnchorsMask`, `OtherOccupancy`, reachable / straight / hook masks as hex, binary and 5x5 grids (`FindReachableAnchors` with its parent map) |
| Canonical state panel | `GroupAnchors[0..3]` with set anchors and `PopCount`, occupancy, `ZobristHash`, an incremental-versus-recomputed hash badge, and the full `Z[g,old] ^ Z[g,new]` derivation of the last action |

The goal is to bring the master L to anchor 16. A BFS hint solves it in two moves, because hook moves make the board very open.

## Reference tab

An in-app summary of the proofs in this folder and the citations in [references.md](references.md).

## Building

```
dotnet build E2e4.SlidingBlock.Logic.slnx
dotnet run --project src/E2e4.SlidingBlock.Logic.Labs.Wpf
```
