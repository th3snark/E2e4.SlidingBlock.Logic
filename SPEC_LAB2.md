# SPEC_LAB2: 5×5 Polyomino Bitboard & Zobrist Laboratory (.NET 10 / C# 14 / WPF)

## 1. Purpose & Scope
Build a C# 14 / .NET 10 (`net10.0-windows`) WPF Desktop application and supporting core library (`SlidingPuzzle.Core`) that implements the **5×5 Polyomino Bitboard & Zobrist Laboratory**.

This application demonstrates how a bitboard engine handles **non-rectangular polyominoes (`L`, `T`, `J` shapes)** and **identical-piece symmetry** without bounding-box origin bugs, negative bit shifts, row wrap-around, or array sorting. Users can select polyominoes on a `5x5` grid, inspect their **LSB-Normalized `BaseMask`**, toggle the precomputed **`ValidAnchorsMask[g]`**, execute single-step or **multi-step corner ("hook") slides** discovered via a register-only 64-bit BFS flood-fill, and verify that **`GroupAnchors[g]`** and **Zobrist Hashing** remain 100% invariant when identical pieces are swapped.

> **UI Design Autonomy:** You have full freedom over the visual design, color scheme, layout, and styling of the WPF application, provided all mathematical mechanics, interactive capabilities, and bitboard inspection tools detailed below are implemented.

---

## 2. Target Architecture & Solution Structure
1. **`SlidingPuzzle.Core` (`net10.0` Class Library):**
   - If building inside an existing solution from `SPEC_LAB1`, extend `SlidingPuzzle.Core`; otherwise create `SlidingPuzzle.Core` with the general polyomino bitboard engine, LSB normalizer, `ValidAnchorsMask` precomputer, register-only BFS reachability flood-fill, shortest-hook-path reconstructor, and group-indexed Zobrist hash table.
2. **`SlidingPuzzle.Lab2.Wpf` (`net10.0-windows` WPF Application):**
   - Hosts the interactive `5x5` polyomino board, LSB anchor indicators, `ValidAnchorsMask` diagnostic overlay, multi-step hook-move trajectory preview/animation, symmetry-swap verifier, and multi-register bitboard + Zobrist inspector.
3. **`SlidingPuzzle.Lab2.Tests` (`net10.0` xUnit Test Project):**
   - Automated unit tests verifying LSB non-negative offsets, overhanging polyomino wrap prevention, multi-step hook move discovery, and Zobrist/GroupAnchors symmetry invariance.

---

## 3. Mathematical & Bitwise Engine Specification

### A. LSB Normalization (`tzcnt`) — Why Negative Shifts Never Occur
In non-rectangular polyominoes such as an upward-pointing **T**-tetromino (`.#. / ###`) or **J**-tromino (`..# / ###`), the top-left corner of the 2D bounding box is empty. Tracking pieces by bounding-box corners fails because two interlocking pieces can share the same bounding-box coordinate, or a valid piece's bounding-box origin can sit off-board (`col = -1`).

Instead, normalize every polyomino shape so its **first occupied cell in row-major order**—its Least Significant Set Bit (**LSB**)—sits at bit `0`:
1. Given a piece's occupied coordinates `(row_k, col_k)` sorted in row-major order, let `(row_0, col_0)` be the first occupied cell.
2. For every cell `k` in the piece, compute `(dRow_k, dCol_k) = (row_k - row_0, col_k - col_0)` and the 1D bit offset:
   ```csharp
   int bitOffset = dRow_k * BoardWidth + dCol_k;
   ```
3. **Mathematical Guarantee:** Even when a lower row overhangs to the left (`dCol_k < 0`, as in the bottom-left arm of an upward `T` or `J` piece), that overhang occurs on a strictly lower row (`dRow_k >= 1`). Because every valid piece is narrower than the board width (`|dCol_k| < BoardWidth`), `dRow_k * BoardWidth + dCol_k` is **strictly positive (`> 0`)** for all `k > 0`, and `0` for `k = 0`.
4. Therefore:
   - `ulong BaseMask` has bit `0` set to `1` and all other set bits at positive indices `> 0`.
   - Placing a piece so its first occupied cell sits at board index `anchor` (`0..24`) is **always** a single unsigned left shift:
     ```csharp
     ulong pieceMask = BaseMask[groupId] << anchor;
     ```
   - Extracting the canonical `anchor` from any placed `pieceMask` is a single hardware intrinsic instruction:
     ```csharp
     int anchor = BitOperations.TrailingZeroCount(pieceMask);
     ```

---

### B. Precomputed `ValidAnchorsMask[groupId]` (Zero Runtime Wrap Checks)
When `PuzzleTopology` initializes, precompute a 64-bit mask `ValidAnchorsMask[groupId]` for each equivalence group `g` on the `5x5` board (`Width = 5`, `Height = 5`, `StaticWallMask = 1UL << 12`):
- For each candidate anchor square `s` in `0..24` (`anchorRow = s / 5`, `anchorCol = s % 5`):
  - For each relative cell `(dRow, dCol)` of group `g`:
    - Let `r = anchorRow + dRow` and `c = anchorCol + dCol`.
    - If `r < 0 || r >= 5 || c < 0 || c >= 5`, mark anchor `s` as **Invalid** (and record the diagnostic reason: e.g., `Left Overhang (c < 0)`, `Right Overhang (c >= 5)`, or `Bottom Overflow (r >= 5)`).
    - If `((StaticWallMask >> (r * 5 + c)) & 1UL) != 0`, mark anchor `s` as **Invalid** (reason: `Collides with Static Wall at Bit 12`).
  - If all cells of the polyomino pass, set bit `s` in `ValidAnchorsMask[g]`.

At runtime, testing if bit `nextAnchor` is set in `ValidAnchorsMask[g]` guarantees in O(1) that `BaseMask[g] << nextAnchor` stays completely inside the `5x5` board without row wrap-around and never touches a static wall.

---

### C. Register-Only Bitboard Flood-Fill for Multi-Step "Hook" Moves
In sliding block puzzles, a single turn allows a piece to slide continuously through empty space—including sliding multiple squares in a line or turning a 90-degree corner ("hook move").

Implement the zero-allocation 64-bit register BFS for move generation, plus a parent-square tracker (`Span<sbyte> parentAnchor` of length 25) so the UI can display and animate the exact step-by-step trajectory of any straight or hook move:

```csharp
public static ulong FindReachableAnchors(
    int startAnchor,
    ulong baseMask,
    ulong validAnchorsMask,
    ulong otherOccupancy,
    int boardWidth,
    Span<sbyte> parentMap, // length 25, initialized to -1; records shortest path tree
    out ulong straightMovesMask,
    out ulong hookMovesMask)
```
- **Distinguishing Straight vs. Hook Moves:**
  - Any reachable anchor `dest` whose shortest path from `startAnchor` uses only horizontal deltas (`-1` or `+1`) OR only vertical deltas (`-boardWidth` or `+boardWidth`) is classified in `straightMovesMask`.
  - Any reachable anchor `dest` whose path combines both horizontal and vertical steps (turning a corner) is classified in `hookMovesMask`.

---

### D. Canonical `GroupAnchors` Bitboards & Permutation-Invariant Zobrist Hashing
1. **`ulong GroupAnchors[groupId]`:**
   - Stores one 64-bit mask per equivalence group `g`, where bit `s` is `1` iff *some* piece belonging to group `g` currently has its LSB anchor at cell `s`.
   - When a piece of group `g` moves from `oldAnchor` to `newAnchor`:
     ```csharp
     GroupAnchors[g] ^= (1UL << oldAnchor) | (1UL << newAnchor);
     ```
2. **Group-Indexed Zobrist Table (`ulong[groupCount, 64] ZobristTable`):**
   - Initialized deterministically using a fixed seed (`XorShift64*`).
   - Crucially, the table is indexed by **`[groupId, anchorSquare]`**, **NOT** `[pieceId, anchorSquare]`.
   - Incremental O(1) update on move:
     ```csharp
     ZobristHash ^= ZobristTable[groupId, oldAnchor] ^ ZobristTable[groupId, newAnchor];
     ```
   - Because XOR is commutative and associative, and identical pieces share the same `groupId`, swapping two identical pieces leaves both `GroupAnchors[g]` and `ZobristHash` completely unchanged without ever sorting piece arrays.

---

## 4. Exact Initial `5x5` Board Configuration (`25` Bits, Indices `0..24`)

Configure the laboratory's default `5x5` board (`Width = 5`, `Height = 5`) with **5 pieces across 4 Equivalence Groups** and **1 Static Wall**:

| Piece / Entity | Equivalence Group | Relative Cells `(dRow, dCol)` from LSB | Bit Offsets `5*dRow + dCol` | LSB-Normalized `BaseMask` | Initial `Anchor` (`tzcnt`) | Initial Occupied Bits |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Master L-Tromino** | `Group 0` (`L-Tromino`) | `(0,0), (1,0), (1,1)` | `0, 5, 6` | `0x0000061UL` | `0` (`r0, c0`) | `0, 5, 6` (`0x0000061UL`) |
| **Upward T-Tetromino** | `Group 1` (`Upward T`) | `(0,0), (1,-1), (1,0), (1,1)` *(bounding-box top-left is empty!)* | `0, 4, 5, 6` | `0x0000071UL` | `16` (`r3, c1`) | `16, 20, 21, 22` (`0x0710000UL`) |
| **J-Tromino** | `Group 2` (`J-Tromino`) | `(0,0), (1,-1), (1,0)` *(bounding-box top-left is empty!)* | `0, 4, 5` | `0x0000031UL` | `4` (`r0, c4`) | `4, 8, 9` (`0x0000310UL`) |
| **1×1 Block A** | `Group 3` (`1x1 Single`) | `(0,0)` | `0` | `0x0000001UL` | `2` (`r0, c2`) | `2` (`0x0000004UL`) |
| **1×1 Block B** | `Group 3` (`1x1 Single`) | `(0,0)` | `0` | `0x0000001UL` | `14` (`r2, c4`) | `14` (`0x0004000UL`) |
| **Static Wall** | *(Wall Mask)* | Fixed at center cell `(2,2)` | Bit `12` | `0x0001000UL` | `12` (`r2, c2`) | `12` (`0x0001000UL`) |

- **Initial `GroupAnchors` values:**
  - `GroupAnchors[0] = 1UL << 0 = 0x0000001UL`
  - `GroupAnchors[1] = 1UL << 16 = 0x0010000UL`
  - `GroupAnchors[2] = 1UL << 4 = 0x0000010UL`
  - `GroupAnchors[3] = (1UL << 2) | (1UL << 14) = 0x0004004UL` *(2 bits set for the 2 identical 1×1 blocks)*
- **Initial Combined Piece Occupancy:** `0x0714375UL` (plus Static Wall at `0x0001000UL`).

---

## 5. Required Interactive Features & Inspector Panels

### A. Interactive `5x5` Polyomino Board View
1. **Polyomino Shape Rendering & Bit Indexing:**
   - Renders the `5x5` grid (`cells 0..24`) with bit index and `(row, col)` labels on every square, the Static Wall at cell `12`, and contiguous multi-cell shapes for the `L`, `T`, `J`, and `1x1` pieces.
   - Note that the two `1x1` blocks (`Block A` and `Block B`) belong to the same equivalence class (`Group 3`), so label them (`1x1 [A]` and `1x1 [B]`) in the UI while showing they share `Group 3`.
2. **LSB Anchor Badge (`tzcnt(pieceMask)`):**
   - Clearly marks each piece's LSB Anchor square on the board (e.g., with a star/anchor badge).
   - When the user selects the **Upward T-Tetromino** or **J-Tromino**, also indicate where its empty bounding-box top-left `(row, col - 1)` sits relative to its actual LSB anchor at `(row, col)` to visually demonstrate how LSB normalization avoids empty bounding-box origins.
3. **Interactive Piece Selection & Reachable Destination Highlighting:**
   - Clicking any piece selects it and runs `FindReachableAnchors`.
   - Highlights all reachable single-turn destination anchor cells on the board, visually differentiating:
     - **Straight-Line Reachable Anchors** (orthogonal 1-or-more step slides).
     - **Corner / Hook-Move Reachable Anchors** (multi-step slides that turn at least one corner).
   - Hovering over any reachable destination anchor previews the ghost footprint of the polyomino at that target anchor AND draws the step-by-step anchor trajectory path (`startAnchor -> ... -> targetAnchor`) across the board.
   - Clicking a reachable destination anchor moves (and animates) the piece along its trajectory path to the new anchor.
4. **`Show ValidAnchorsMask[g] Overlay` Toggle:**
   - A toggle switch that overlays the selected piece's precomputed 25-bit `ValidAnchorsMask[g]` directly onto the `5x5` board:
     - Valid anchor cells (`bit == 1`) are marked as valid.
     - Invalid anchor cells (`bit == 0`) are marked as forbidden, and hovering over an invalid cell displays the exact geometric reason it is masked out (e.g., for Upward T or J at Column 0: *"Invalid Anchor: Bottom-left arm `(dRow=+1, dCol=-1)` would overhang left off the board (`col - 1 < 0`) and wrap around to Column 4 of the current row"*).
5. **Symmetry Invariance Test (`Swap Identical 1x1 Pieces` Button):**
   - A dedicated action button that swaps the board positions of `1x1 Block A` and `1x1 Block B` (both in `Group 3`).
   - Executing the swap updates the board labels (`[A]` and `[B]` exchange squares) and displays a verification readout proving that:
     - `GroupAnchors[3]` before swap == `GroupAnchors[3]` after swap.
     - `ZobristHash` before swap == `ZobristHash` after swap.
6. **Additional Board Controls:**
   - `Load Hook-Move Demo Preset` (or `Random Valid Move`): Immediately sets up or highlights a multi-step polyomino corner hook slide (e.g., sliding the `1x1` block or `L`/`T`/`J` piece around the center wall or another block) so the user can test polyomino hook moves with one click.
   - `Undo Move` and `Reset Board`.

### B. Selected Piece Bitboard Inspector Panel
Displays real-time machine registers for the currently selected piece (in both 64-bit hex and 25-bit binary / `5x5` mini-matrix formats):
1. **Piece & Group Metadata:** `PieceId`, `EquivalenceGroupId`, relative `(dRow, dCol)` coordinates, and computed positive bit offsets `5 * dRow + dCol`.
2. **`BaseMask[g]`:** The LSB-normalized template mask (`bit 0` always `1`).
3. **`CurrentAnchor`:** `BitOperations.TrailingZeroCount(pieceMask)` (`0..24`).
4. **`PieceMask = BaseMask[g] << CurrentAnchor`:** The active shifted footprint on the board.
5. **`ValidAnchorsMask[g]`:** The precomputed 25-bit mask of legal in-bounds, wall-free anchors for this shape.
6. **`OtherOccupancy = BoardOccupancy ^ PieceMask`:** The combined footprint of all other pieces.
7. **`ReachableAnchors` Flood-Fill Result:** The output bitmask of `FindReachableAnchors`, broken down into count/bits of `StraightMovesMask` and `HookMovesMask`.

### C. Canonical Equivalence State (`GroupAnchors[0..3]`) & Zobrist Hash Panel
1. **`GroupAnchors[0..3]` Table:**
   - Displays all 4 equivalence groups (`Group 0: L-Tromino`, `Group 1: Upward T`, `Group 2: J-Tromino`, `Group 3: 1x1 Single`), showing each group's 25-bit binary mask, hex literal, set anchor bit indices, and `PopCount` (piece count in group).
2. **Live 64-Bit Zobrist Hash & Incremental XOR Trace:**
   - Displays the current 64-bit `ZobristHash` (`0x...`).
   - Displays a verification badge comparing `IncrementalHash == RecomputedFromScratchHash`.
   - After every move (or symmetry swap), displays the full bitwise XOR derivation with concrete 64-bit hex values:
     ```text
       PreviousHash:                     0x................
     ^ ZobristTable[g, oldAnchor]:       0x................  (Remove old anchor)
     ^ ZobristTable[g, newAnchor]:       0x................  (Add new anchor)
     ------------------------------------------------------
     = New ZobristHash:                  0x................
     ```

---

## 6. Verification & Unit Test Requirements (`SlidingPuzzle.Lab2.Tests`)
Include xUnit tests verifying:
1. **LSB Normalization Non-Negative Offsets:** Confirms that `L`, `Upward T`, and `J` shapes produce strictly non-negative bit offsets relative to their first occupied row-major cell, with `BaseMask` bit `0 == 1` (`0x61UL`, `0x71UL`, `0x31UL`).
2. **`ValidAnchorsMask` Overhang & Wall Filtering:** Confirms that for the `Upward T` (`Group 1`), all Column 0 cells (`0, 5, 10, 15, 20`), all Column 4 cells (`4, 9, 14, 19, 24`), all Row 4 cells (`20..24`), and any anchor whose footprint overlaps the Static Wall at bit `12` (`anchors 6, 7, 8, 12`) are masked to `0` in `ValidAnchorsMask[1]`.
3. **Register-Only BFS Hook Moves:** Confirms that `FindReachableAnchors` discovers multi-step corner hook moves around obstacles, reconstructs the exact intermediate anchor sequence, and never leaks across row boundaries.
4. **Zobrist & `GroupAnchors` Symmetry Invariance:** Confirms that swapping the two `Group 3` `1x1` blocks produces the exact same `GroupAnchors[3]` bitboard (`(1UL << a) | (1UL << b)`), the exact same 64-bit `ZobristHash`, and `stateA.Equals(stateB) == true`.

Run `dotnet build` and `dotnet test` to ensure zero warnings or failures.