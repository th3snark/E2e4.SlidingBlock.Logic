# 2. LSB normalisation

## 2.1 The problem

A natural way to place a piece on a bitboard is to track the top-left corner of its bounding box. That breaks for shapes whose
top-left bounding-box cell is empty:

```
Upward T            J
. # .               . . #
# # #               # # #
^ bounding-box      ^ bounding-box
  origin is empty     origin is empty
```

Two interlocking pieces can then share the same origin, and a valid placement can have its origin off the board.

## 2.2 The rule

Normalise every shape so that its **first occupied cell in row-major order** (the least-significant set bit, LSB) is at bit `0`:

```
BaseMask = rawMask >> BitOperations.TrailingZeroCount(rawMask)
```

That cell is the piece's **anchor**. For the upward T it is the top stem, not the empty corner.

```
Board width W = 5, T placed with its anchor on square 16 (row 3, col 1)

 row 3:  . [A] . . .      A = anchor = tzcnt(mask) = 16
 row 4:  # # # . .        bits 20, 21, 22

 BaseMask = bits {0, 4, 5, 6} = 0x71      (stem at 0, then the row below at W-1, W, W+1)
 mask     = BaseMask << 16 = bits {16, 20, 21, 22}
```

Worked values on a 5-wide board (all covered by tests):

| Piece | Relative cells `(dRow, dCol)` | `BaseMask` |
|-------|-------------------------------|-----------|
| L-tromino | `(0,0) (1,0) (1,1)` | `0x61` |
| Upward T | `(0,0) (1,-1) (1,0) (1,1)` | `0x71` |
| J-tromino | `(0,0) (1,-1) (1,0)` | `0x31` |
| 1x1 | `(0,0)` | `0x01` |

## 2.3 Proof that every offset is non-negative

Let the anchor be the cell `(0,0)` after normalisation and let `(dRow, dCol)` be any other cell of the piece.

1. The anchor is first in row-major order, so either `dRow = 0` and `dCol > 0`, or `dRow >= 1`.
2. If `dRow = 0`, the bit offset is `dCol > 0`.
3. If `dRow >= 1`, the cell may overhang to the left (`dCol < 0`). Every piece is narrower than the board, so `|dCol| <= W - 1`, hence
   `dBit = dRow * W + dCol >= W - (W - 1) = 1 > 0`.

So every occupied bit of `BaseMask` has an index `>= 0`, with bit `0` always set. Consequences:

- Moving a piece to any square is **one unsigned left shift**: `BaseMask << anchor`. No signed offset arrays are needed.
- Recovering a piece's anchor from its footprint is **one instruction**: `int anchor = BitOperations.TrailingZeroCount(pieceMask)`.

## 2.4 In the code

`PuzzleTopologyBuilder.Normalize(cells, boardColumns)` lays the cells in their bounding box, shifts by `tzcnt`, and also returns the cells
relative to the anchor (`EquivalenceGroup.RelativeCells`). `BaseMask` shifted by an anchor is only meaningful when the anchor is
valid, which is what [`ValidAnchorsMask`](03-valid-anchors-and-hook-moves.md) guarantees.
