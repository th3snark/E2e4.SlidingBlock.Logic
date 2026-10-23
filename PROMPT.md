# Project Brief: High-Performance Sliding Block Puzzle Engine & WPF Bitboard Laboratories (.NET 10 / C# 14)

## 1. Objective
Build a complete, production-grade .NET 10 solution (`E2e4.SlidingBlock.Logic.slnx`) consisting of:
1. **`E2e4.SlidingBlock.Logic` (`net10.0` Class Library):** A zero-allocation, bitboard-driven object model and solver engine supporting the three canonical families of sliding block puzzles, plus general non-rectangular polyominoes (`L`, `T`, `J`, `S`, `Z`, `U`).
2. **`E2e4.SlidingBlock.Logic.Labs.Wpf` (`net10.0-windows` WPF Application):** A modern dark-themed Windows Desktop application hosting the two interactive bitboard and memory-inspector laboratories detailed below.
3. **`E2e4.SlidingBlock.Logic.Test` (`net10.0` xUnit Test Project):** Unit tests verifying LSB normalization, row wrap-around prevention, multi-step hook-move BFS flood-fill, and Zobrist/GroupAnchors symmetry invariance.

Use **C# 14** features throughout (including `field`-backed properties for clean MVVM view models, `extension` blocks for bitboard diagnostics, `[InlineArray]` for fixed-size value-type buffers inside structs, `ReadOnlySpan<T>`, and `System.Numerics.BitOperations` hardware intrinsics `TrailingZeroCount`, `PopCount`, and `LeadingZeroCount`).

---

## 2. Research Findings, Mathematical Justifications & References

### A. Why Three Specialized Representations Exist
Sliding block puzzles are PSPACE-complete in the general case (Hearn & Demaine, 2005), meaning solver throughput depends heavily on state size, move-generation branching, and transposition-table hash efficiency. Engine implementations converge on three specialized 64-bit representations based on piece degrees of freedom:
1. **Uniform N-Puzzles (4x4 15-Puzzle) — Nibble-Packed `ulong`:**
   - **Justification:** 16 squares holding values `0..15` require exactly 4 bits per square (`16 * 4 = 64 bits`). The entire state fits in a single CPU register (`ulong`). Sliding a tile into the blank (`0x0`) is a branchless nibble XOR swap. High-speed IDA* solvers also maintain an inverse `ulong` mapping `tile -> square` for O(1) Pattern Database (PDB) lookups.
2. **Axis-Constrained Puzzles (6x6 Rush Hour) — 1D Offset Packing + Precomputed Bitboards:**
   - **Justification:** Cars (`1x2`) and trucks (`1x3`) cannot switch lanes or rotate; each has only 1 degree of freedom (`offset 0..4` along its fixed row or column), requiring 3 bits per vehicle (`<= 16` vehicles = 48 bits in a `ulong`). Full 36-bit occupancy is reconstructed in O(N) via a precomputed lookup table `VehicleMasks[vehicleIdx, offset]`, and move legality tests a single bit `FrontNeighborBit[vehicleIdx, offset]`.
3. **Unrestricted 2D Rectangular & Polyomino Puzzles (Klotski & General Polyominoes):**
   - **Justification:** Pieces move in 2D and often include multiple identical pieces. Naive piece-ID arrays fail because swapping `k` identical pieces multiplies the search space by `k!` (e.g., `4! * 4! = 576x` in standard Klotski). Furthermore, non-rectangular polyominoes (`T`, `J`, `L`) introduce three geometric hazards that break naive bounding-box bitboards:
     - **Hazard 1 (Empty Bounding-Box Origins):** An upward-pointing `T` (`.#. / ###`) or `J` (`..# / ###`) has an empty top-left bounding-box cell. Tracking bounding-box origins allows two interlocking pieces to share the same origin coordinate or places valid origins off-board.
     - **Hazard 2 (Asymmetric Row Wrap-Around):** A polyomino can overhang to the left on a lower row relative to its top cell, wrapping across row boundaries on a 1D bitboard even when the anchor cell is away from the left edge.
     - **Hazard 3 (Cell-Encoding Overflow):** Standard Klotski encodes 20 cells x 3 bits = 60 bits by cell shape class. Once a puzzle has more than 7 distinct polyomino shape/orientation classes or exceeds 21 cells, cell-by-cell shape encoding overflows 64 bits.

### B. Mathematical Proof of LSB Normalization (`tzcnt`)
To solve Hazard 1 and Hazard 2 without signed offset arrays, normalize every polyomino template's 64-bit mask so its **first occupied cell in row-major order** (Least Significant Set Bit, `LSB`) is at bit `0`:
- `BaseMask = rawMask >> BitOperations.TrailingZeroCount(rawMask)`
- Even if a polyomino overhangs to the left on a lower row (`deltaCol < 0`), that overhang must occur on a strictly lower row (`deltaRow >= 1`). Because every valid piece is narrower than the board width (`|deltaCol| < BoardWidth`), the 1D bit offset is:
  `deltaBit = (deltaRow * BoardWidth) + deltaCol > 0`
- **Result:** Every occupied bit in `BaseMask` is guaranteed to sit at a non-negative bit index `>= 0` (with bit `0` always set to `1`). Shifting a piece to any board square `anchor` is **always** a single unsigned left shift (`BaseMask << anchor`), and extracting a piece's canonical anchor from its current board footprint is a single hardware instruction: `int anchor = BitOperations.TrailingZeroCount(pieceMask);`.

### C. Eliminating Sentinel Walls via `ValidAnchorsMask[groupId]`
Instead of padding bitboards with sentinel wall columns, precompute `ulong ValidAnchorsMask[groupId]` when initializing `PuzzleTopology`:
- For each square `s` in `0..(Width * Height - 1)`, test in 2D `(row, col)` coordinates whether placing every cell of the polyomino relative to anchor `s` remains strictly inside `[0..Height-1, 0..Width-1]` and avoids static wall squares.
- At runtime, `((ValidAnchorsMask[g] >> nextAnchor) & 1UL) != 0` guarantees in a single bit-test that `BaseMask[g] << nextAnchor` will never wrap across rows or hit a wall.

### D. Register-Only BFS Flood-Fill for Multi-Step "Hook" Moves
Under the single-piece sliding metric, a polyomino can slide multiple steps—including turning corners ("hook moves")—in a single turn. Because a piece's placement is uniquely identified by its single LSB `anchor` bit (`0..63`), all reachable single-turn destinations are computed with zero heap allocations using a 64-bit register BFS (`frontier`, `visitedOrInvalid`, `reachableNewAnchors`), initializing `visitedOrInvalid = ~ValidAnchorsMask[g] | (1UL << startAnchor)`.

### E. Permutation-Invariant Zobrist Hashing & Lossless `GroupAnchors`
To eliminate the `k!` identical-piece symmetry explosion:
1. Group identical pieces (same `BaseMask`, same `AllowedDirections`, same goal role) under a shared `EquivalenceGroupId` (`0..G-1`).
2. Index the Zobrist table by **`ZobristTable[groupId, anchorSquare]`** instead of `[pieceId, anchorSquare]`. Moving any piece in `groupId` from `oldAnchor` to `newAnchor` updates the hash in O(1) via:
   `Hash ^= ZobristTable[groupId, oldAnchor] ^ ZobristTable[groupId, newAnchor];`
3. Store the lossless canonical state as one bitboard per equivalence group: **`GroupAnchors[groupId]`**, where bit `s` is `1` iff some piece of `groupId` currently has its LSB anchor at square `s`. Updating on move is `GroupAnchors[g] ^= (1UL << oldAnchor) | (1UL << newAnchor)`. Swapping two identical pieces leaves both `GroupAnchors[g]` and `Hash` 100% invariant without sorting.

### F. Key References (Embed in Documentation & About/Reference Panel)
- **Zobrist, A. L. (1970).** *A New Hashing Method with Application for Game Playing.* Technical Report #88, Computer Sciences Department, University of Wisconsin. (Foundation for incremental XOR state hashing by piece-type equivalence class).
- **Korf, R. E., & Felner, A. (2002).** *Disjoint Pattern Database Heuristics.* Artificial Intelligence, 134(1-2), 9-22. (Nibble-packed 15-puzzle representation and O(1) inverse-state lookup).
- **Hearn, R. A., & Demaine, E. D. (2005).** *PSPACE-Completeness of Sliding-Block Puzzles and Other Problems through the Nondeterministic Constraint Logic Model of Computation.* Theoretical Computer Science, 343(1-2), 72-96.
- **Spaans, R. (2009) & Fogleman, M. (2018).** *Solving Sliding-Block Puzzles / Rush Hour & Klotski Bitboard Encodings.* (Canonical cell-shape encoding, 1D lane-offset packing, and bit-shift collision detection).

---

## 3. Core Object Model Architecture (`E2e4.SlidingBlock.Logic`)

Design a clean separation between **Immutable Puzzle Topology** (reference types created once per puzzle) and **Value-Type Search States** (`readonly record struct` / `struct` implementing `IEquatable<TState>`).

### A. Common Abstractions & Topology
- `enum AllowedDirections : byte { None = 0, Horizontal = 1, Vertical = 2, Both = 3 }`
- `sealed record EquivalenceGroup(int GroupId, string Name, string ColorHex, ulong BaseMask, ulong ValidAnchorsMask, AllowedDirections Directions, IReadOnlyList<(int dRow, int dCol)> RelativeCells)`
- `sealed record PieceTemplate(int PieceId, int GroupId, string Label, int InitialAnchor)`
- `sealed class PuzzleTopology`:
  - Properties: `int Width`, `int Height`, `ulong StaticWallsMask`, `IReadOnlyList<EquivalenceGroup> Groups`, `IReadOnlyList<PieceTemplate> Pieces`, `ulong[,] ZobristTable` (seeded deterministically via `XorShift64*` so hashes are reproducible).
  - Factory methods to construct topologies from ASCII board maps or relative coordinate lists, automatically performing **LSB Normalization** (`tzcnt`) and precomputing **`ValidAnchorsMask[g]`**.
- `interface ISlidingBlockDomain<TState, TMove> where TState : struct, IEquatable<TState>`:
  - `TState InitialState { get; }`
  - `bool IsGoal(in TState state);`
  - `int GenerateMoves(in TState state, Span<TMove> moveBuffer);`
  - `TState ApplyMove(in TState state, in TMove move);`
  - `ulong GetHashCode64(in TState state);`
- `sealed class PuzzleSolver`: Generic BFS shortest-path solver and A* / IDA* solver over `ISlidingBlockDomain<TState, TMove>` so any puzzle in either lab can also be auto-solved or stepped through optimally.

### B. Specialized State Structs
1. **`NPuzzleState` (`readonly struct`, 8 bytes):**
   - `public ulong PackedNibbles { get; }` (4 bits per square, `0` = blank).
   - Branchless nibble extraction, blank-index lookup, Manhattan distance, and bitwise XOR tile swap.
2. **`KlotskiState` (`readonly struct`):**
   - Tracks `[InlineArray(10)]` of piece bitmasks or anchor bytes, combined 20-bit `ulong OccupancyMask`, and the 60-bit **`ulong CanonicalShapeKey`** (3 bits per cell encoding shape class `0..4`).
3. **`RushHourState` (`readonly struct`, 8 bytes):**
   - `public ulong PackedOffsets { get; }` (3 bits per vehicle lane offset) + `RushHourTopology` holding `ulong[vehicleIdx, offset] VehicleMasks` and `FrontNeighborBit` / `BackNeighborBit`.
4. **`PolyominoState` (`struct`, `IEquatable<PolyominoState>`):**
   - `[InlineArray(8)] public struct GroupAnchorBuffer { private ulong _element0; }` -> stores canonical `GroupAnchors[groupId]`.
   - `[InlineArray(16)] public struct PieceAnchorBuffer { private byte _element0; }` -> stores active individual piece anchors for UI selection/animation while `Equals` and `GetHashCode` rely purely on `ZobristHash` and `GroupAnchors`!
   - `public ulong OccupancyMask { get; }`
   - `public ulong ZobristHash { get; }`
   - `public static ulong FindReachableAnchors(int startAnchor, ulong baseMask, ulong validAnchorsMask, ulong otherOccupancy, int boardWidth)` plus a companion method `ReconstructHookPath(int startAnchor, int targetAnchor, ...)` that returns the exact step-by-step cell trajectory (`IReadOnlyList<int>`) for smooth UI corner-slide animations.

---

## 4. WPF Desktop Application Specification (`E2e4.SlidingBlock.Logic.Labs.Wpf`)

Build a polished, dark-themed WPF application (inspired by hardware register debuggers / JetBrains Darcula: deep slate `#0F172A` / `#1E293B` backgrounds, crisp monospace bit/hex displays in `Consolas` / `Cascadia Mono`, vibrant semantic piece colors, vector arrows/stars, and smooth `CompositionTarget.Rendering` or storyboarded `180ms` slide animations).

Structure the MainWindow with a top header bar and a `TabControl` hosting three tabs:

### Tab 1: Lab 1 — Standard Sliding Block Architectures Explorer
Allows real-time comparison of the three classic representations:
- **Mode Selector Bar:** Radio buttons / segmented toggle for:
  1. `15-Puzzle (4x4 Nibble-Packed uint64)` — initialized to `0xFEDCBA9876543210` (or solvable scramble).
  2. `Klotski (4x5 Pennant Setup: Bitboards + 60-bit Canonical Shape Key)` — 1x `2x2` master block, 4x `1x2` vertical blocks, 1x `2x1` horizontal block, 4x `1x1` blocks.
  3. `Rush Hour (6x6 1D Lane-Offset Packing + 36-bit Occupancy Lookup)` — Red target car (`2x1`, Row 2) + blocking cars and trucks.
- **Left Panel (Interactive 2D Board Canvas):**
  - Renders the grid, coordinate indices, piece shapes, and clickable directional move arrows for all currently legal moves.
  - Clicking a movable block or arrow slides the block and updates the machine inspector.
  - Action buttons: `Random Valid Move`, `Solve Step (BFS Hint)`, `Reset Board`.
- **Right Panel (Live 64-Bit Register & Bitwise Inspector):**
  - **64-Bit Packed State Register:** Shows the full `0x...` hex value and color-coded bit groups (16x 4-bit nibbles for 15-Puzzle, 20x 3-bit cell shape codes for Klotski, or 3-bit vehicle offsets for Rush Hour). Bits that mutated on the last move flash-highlight.
  - **Binary Occupancy Bitboard Grid:** Live `0`/`1` matrix mirroring board occupancy.
  - **Live Bitwise Move Formula Trace:** Displays the exact bitwise expression evaluated for the last move (e.g., `(otherOccupancy & (pieceMask << shift)) == 0` or nibble XOR mask) with actual hex operands.

### Tab 2: Lab 2 — Non-Rectangular Polyomino & Hook-Move Laboratory
Demonstrates LSB Normalization, `ValidAnchorsMask`, multi-step corner ("hook") moves, and permutation-invariant Zobrist hashing on a **5x5 grid (25 bits, indices `0..24`)**:
- **Initial 5x5 Board Setup:**
  - `Group 0 (Master L-Tromino, Gold)`: 3 cells `(0,0), (1,0), (1,1)` -> `BaseMask = 0x0000061`, placed at `anchor = 0` (bits `0, 5, 6`).
  - `Group 1 (Upward T-Tetromino, Cyan)`: 4 cells `(0,0), (1,-1), (1,0), (1,1)` (note: top-left bounding box cell is empty!) -> `BaseMask = 0x0000071`, placed at `anchor = 16` (row 3, col 1 -> bits `16, 20, 21, 22`).
  - `Group 2 (J-Tromino, Purple)`: 3 cells `(0,0), (1,-1), (1,0)` -> `BaseMask = 0x0000031`, placed at `anchor = 4` (row 0, col 4 -> bits `4, 8, 9`).
  - `Group 3 (Two Identical 1x1 Blocks, Emerald)`: `BaseMask = 0x0000001`, Block A at `anchor = 2` (bit `2`) and Block B at `anchor = 14` (bit `14`). Both share `EquivalenceGroupId = 3`.
  - `Static Wall`: Center square at index `12` (`row 2, col 2`), baked into `ValidAnchorsMask[g]`.
- **Left Panel (5x5 Polyomino Board Canvas):**
  - Renders non-rectangular merged polyomino polygons with clean inner/outer borders and bit indices `0..24`.
  - Renders a **Golden Star Badge** on each piece's LSB Anchor cell (`tzcnt(pieceMask)`), visually proving that the Upward T and J pieces anchor on their top occupied stem rather than their empty bounding-box corner.
  - Selecting a piece highlights all reachable single-turn destination anchors computed by `FindReachableAnchors`—distinguishing **1-step orthogonal slides** from **multi-step corner/hook moves** (drawing the dashed corner trajectory path). Clicking a destination anchor animates the piece along the multi-segment hook path.
  - Toggle switch: **`Show ValidAnchorsMask[g] Overlay`** — overlays green/red badges on all 25 cells for the selected piece, visually explaining why Column 0 is forbidden for the Upward T and J pieces (`1*W - 1` leftward overhang on the row below).
  - Symmetry Test Button: **`Swap Identical 1x1 Pieces (Test Symmetry)`** — swaps the board positions of Block A and Block B in Group 3 and displays a verification banner proving that `GroupAnchors[3]` and `ZobristHash` remain 100% identical.
- **Right Panel (Multi-Register Polyomino Inspector):**
  - **Selected Piece Bitboard Breakdown:** Shows `EquivalenceGroupId`, `BaseMask[g]`, `CurrentAnchor = tzcnt(mask)`, shifted `BaseMask[g] << anchor`, `ValidAnchorsMask[g]`, and `ReachableAnchors` flood-fill mask.
  - **Canonical Equivalence State (`GroupAnchors[0..3]` & Zobrist XOR Trace):** Live 25-bit binary/hex displays for each `GroupAnchors[g]` plus the 64-bit `ZobristHash` and the exact `ZobristTable[g, oldAnchor] ^ ZobristTable[g, newAnchor]` XOR calculation performed on the last move.

### Tab 3: Architecture & Research Reference Guide
An in-app formatted reference view summarizing the mathematical proofs (LSB non-negative offset proof, `ValidAnchorsMask` boundary elimination, Zobrist symmetry invariance) and full academic citations so the application serves as a self-contained engineering reference.

---

## 5. Execution & Verification Steps
1. Scaffold the solution (`dotnet new sln`, `E2e4.SlidingBlock.Logic`, `E2e4.SlidingBlock.Logic.Labs.Wpf`, `E2e4.SlidingBlock.Logic.Test`) targeting `.NET 10`.
2. Implement `E2e4.SlidingBlock.Logic` and comprehensive unit tests in `E2e4.SlidingBlock.Logic.Test` covering:
   - LSB normalization non-negative bit offsets for `L`, `T`, and `J` polyominoes.
   - `ValidAnchorsMask` preventing left/right row wrap-around for overhanging `T` and `J` shapes.
   - `FindReachableAnchors` discovering multi-step corner hook moves while rejecting blocked corners.
   - Zobrist hash and `GroupAnchors` bitboard equality when swapping identical pieces in the same equivalence group.
3. Implement `E2e4.SlidingBlock.Logic.Labs.Wpf` with custom WPF vector/canvas rendering, smooth multi-segment animations, and live bit-register inspectors.
4. Run `dotnet build` and `dotnet test` and fix any compiler warnings or test failures before finishing.