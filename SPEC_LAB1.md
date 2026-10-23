# SPEC_LAB1: Sliding Block Bitboard & State Register Laboratory (.NET 10 / C# 14 / WPF)

## 1. Purpose & Scope
Build a C# 14 / .NET 10 (`net10.0-windows`) WPF Desktop application and supporting core class library (`SlidingPuzzle.Core`) that implements the **Sliding Block Bitboard & State Register Laboratory**.

The goal of this application is functional and educational: it allows the user to interactively manipulate the three canonical families of sliding block puzzles on a 2D board while inspecting how every spatial state and legal move translates into **64-bit packed registers (`ulong`)**, **occupancy bitboards**, and **branchless bitwise collision checks** in real time.

> **UI Design Autonomy:** You have full freedom over the visual design, layout, color palette, typography, and control styling of the WPF interface, provided every functional capability, data structure, state representation, and interactive behavior specified below is implemented.

---

## 2. Target Architecture & Solution Structure
1. **`SlidingPuzzle.Core` (`net10.0` Class Library):**
   - Contains the zero-allocation value-type state models (`readonly struct` / `struct`), topology definitions, bitwise move generators, and a generic BFS shortest-path solver (`PuzzleSolver`) for all three puzzle families.
   - Uses `System.Numerics.BitOperations` (`TrailingZeroCount`, `PopCount`) and C# 14 features.
2. **`SlidingPuzzle.Lab1.Wpf` (`net10.0-windows` WPF Application):**
   - Hosts the interactive board view, mode switcher, live 64-bit state register breakdown, occupancy bitboard matrix, and live bitwise move formula trace.
3. **`SlidingPuzzle.Lab1.Tests` (`net10.0` xUnit Test Project):**
   - Automated unit tests verifying state packing/unpacking, move generation, bitwise collision checks, and solver correctness across all three puzzle families.

---

## 3. Core Object Models & Mathematical Mechanics (The Three Modes)

The application must support switching at runtime between three distinct puzzle architectures.

### Mode 1: Uniform N-Puzzle (`4x4` 15-Puzzle — Nibble-Packed `ulong`)
- **Board Geometry:** `4x4` grid (`16` cells, indexed `0..15` in row-major order: `index = row * 4 + col`).
- **State Representation (`NPuzzleState` — 8-byte `readonly struct`):**
  - Stored entirely inside a single `ulong PackedNibbles`.
  - Nibble `i` (bits `i * 4` through `i * 4 + 3`) stores the tile ID (`1..15`, or `0` for the empty space) currently occupying cell `i`:
    ```csharp
    public int GetTileAt(int cellIndex) => (int)((PackedNibbles >> (cellIndex * 4)) & 0xFUL);
    ```
  - Also maintain or compute on demand the **Inverse State (`ulong InversePackedNibbles`)** where nibble `tileId` stores the `cellIndex` (`0..15`) occupied by `tileId` (used for O(1) blank location lookup `BlankCell = (int)(InversePackedNibbles & 0xFUL)` and O(1) Pattern Database / Manhattan distance evaluation).
- **Occupancy Bitboard (`ulong OccupancyMask`):**
  - 16-bit mask where bit `i` is `1` if cell `i` contains a tile (`1..15`), and `0` if cell `i` is the blank space (`0`).
- **Bitwise Move Execution:**
  - Precompute a lookup table of valid orthogonal neighbor indices for each of the 16 cells.
  - Sliding a tile from `srcCell` into `blankCell` is a branchless XOR nibble swap:
    ```csharp
    ulong tile = (PackedNibbles >> (srcCell * 4)) & 0xFUL;
    ulong xorMask = (tile << (srcCell * 4)) | (tile << (blankCell * 4));
    ulong newState = PackedNibbles ^ xorMask;
    ```
- **Default Initial State:**
  - Initialize to a valid, solvable state (for example, solved except for the last few moves, or a canonical solvable configuration with `0` at cell `15` where tiles at cells `11` and `14` can immediately slide into `15`), plus a `Scramble (Solvable)` action.

---

### Mode 2: Unrestricted 2D Rectangular Blocks (`4x5` Klotski — Bitboards + 60-Bit Canonical Shape Key)
- **Board Geometry:** `4x5` grid (`20` cells, indexed `0..19` in row-major order: `index = row * 4 + col`).
- **Piece Shape Equivalence Classes (3-Bit Codes):**
  - Because Klotski contains multiple identical blocks, tracking piece IDs in the visited set causes a `4! * 4! = 576x` symmetry explosion. Define 5 canonical shape classes (`0..4`):
    - `0` (`0b000`): Empty cell
    - `1` (`0b001`): `1x1` Single block
    - `2` (`0b010`): `1x2` Vertical block (`1` wide × `2` high)
    - `3` (`0b011`): `2x1` Horizontal block (`2` wide × `1` high)
    - `4` (`0b100`): `2x2` Master block (`2` wide × `2` high)
- **Default Initial State (Classic "Pennant / Huarong Dao" 10-Piece Setup):**
  - Piece `0` (`2x2` Master, Class `4`): cells `(0,1),(0,2),(1,1),(1,2)` -> bits `1, 2, 5, 6` (`0x00066UL`)
  - Piece `1` (`1x2` Vertical, Class `2`): cells `(0,0),(1,0)` -> bits `0, 4` (`0x00011UL`)
  - Piece `2` (`1x2` Vertical, Class `2`): cells `(0,3),(1,3)` -> bits `3, 7` (`0x00088UL`)
  - Piece `3` (`1x2` Vertical, Class `2`): cells `(2,0),(3,0)` -> bits `8, 12` (`0x01100UL`)
  - Piece `4` (`1x2` Vertical, Class `2`): cells `(2,3),(3,3)` -> bits `11, 15` (`0x08800UL`)
  - Piece `5` (`2x1` Horizontal, Class `3`): cells `(2,1),(2,2)` -> bits `9, 10` (`0x00600UL`)
  - Piece `6` (`1x1` Single, Class `1`): cell `(3,1)` -> bit `13` (`0x02000UL`)
  - Piece `7` (`1x1` Single, Class `1`): cell `(3,2)` -> bit `14` (`0x04000UL`)
  - Piece `8` (`1x1` Single, Class `1`): cell `(4,0)` -> bit `16` (`0x10000UL`)
  - Piece `9` (`1x1` Single, Class `1`): cell `(4,3)` -> bit `19` (`0x80000UL`)
  - Empty cells (Class `0`): `(4,1)` and `(4,2)` -> bits `17, 18`
  - Combined initial 20-bit `OccupancyMask = 0x9FFFFUL`.
- **State Representation (`KlotskiState`):**
  - **Active Move-Gen State:** Stores the 10 individual 20-bit `ulong PieceMasks[10]` and the combined 20-bit `ulong OccupancyMask`.
  - **60-Bit Canonical Shape Key (`ulong CanonicalKey`):** Packs all 20 cells at 3 bits per cell (`20 * 3 = 60 bits` inside a single `ulong`), where bits `[cell * 3 .. cell * 3 + 2]` store the 3-bit shape class (`0..4`) covering `cell`. Swapping any two identical `1x2` or `1x1` blocks produces the exact same 60-bit integer.
- **Bitwise Collision & Boundary Masks:**
  - Define static boundary masks for the `4x5` board:
    - `LeftColMask   = 0b0001_0001_0001_0001_0001UL` (`0x11111UL` — prevents left-shift wrap from col 0)
    - `RightColMask  = 0b1000_1000_1000_1000_1000UL` (`0x88888UL` — prevents right-shift wrap from col 3)
    - `TopRowMask    = 0x0000FUL` (bits `0..3`)
    - `BottomRowMask = 0xF0000UL` (bits `16..19`)
  - To test if piece `i` (`mask = PieceMasks[i]`) can slide 1 step in a direction:
    ```csharp
    ulong otherPieces = OccupancyMask ^ mask;
    bool canMoveLeft  = (mask & LeftColMask) == 0   && (otherPieces & (mask >> 1)) == 0;
    bool canMoveRight = (mask & RightColMask) == 0  && (otherPieces & (mask << 1)) == 0;
    bool canMoveUp    = (mask & TopRowMask) == 0    && (otherPieces & (mask >> 4)) == 0;
    bool canMoveDown  = (mask & BottomRowMask) == 0 && (otherPieces & (mask << 4)) == 0;
    ```

---

### Mode 3: Axis-Constrained Vehicles (`6x6` Rush Hour — 1D Lane-Offset Packing + Bitboard Lookup)
- **Board Geometry:** `6x6` grid (`36` cells, indexed `0..35` in row-major order: `index = row * 6 + col`).
- **Degrees of Freedom & State Packing (`RushHourState` — 8-byte `readonly struct`):**
  - Each vehicle `v` (`0 .. VehicleCount - 1`, up to 16 vehicles) has a fixed orientation (`Horizontal` or `Vertical`), a fixed lane (`Row` or `Col` in `0..5`), and a fixed length (`2` for cars, `3` for trucks).
  - The only dynamic variable for vehicle `v` is its 1D lane offset (`0..4` for length 2; `0..3` for length 3), which fits in **3 bits** (`0b000 .. 0b100`).
  - `ulong PackedOffsets` stores vehicle `v`'s offset at bits `[v * 3 .. v * 3 + 2]`:
    ```csharp
    public int GetOffset(int v) => (int)((PackedOffsets >> (v * 3)) & 0b111UL);
    ```
- **Precomputed Bitboard Lookup Tables (in `RushHourTopology`):**
  - `ulong VehicleMasks[v, offset]`: The 36-bit board footprint of vehicle `v` when placed at `offset`.
  - `ulong FrontNeighborBit[v, offset]`: The single-bit mask (`1UL << cell`) immediately ahead of vehicle `v` (`offset + length`), or `0UL` if against the board edge (`offset + length == 6`).
  - `ulong BackNeighborBit[v, offset]`: The single-bit mask (`1UL << cell`) immediately behind vehicle `v` (`offset - 1`), or `0UL` if at the start edge (`offset == 0`).
- **Bitwise Occupancy Reconstruction & Move Check:**
  ```csharp
  // Reconstruct 36-bit occupancy in O(N):
  ulong occupancy = 0UL;
  for (int v = 0; v < vehicleCount; v++)
      occupancy |= VehicleMasks[v, GetOffset(v)];

  // O(1) single-bit collision test:
  bool canAdvance = FrontNeighborBit[v, offset] != 0UL && (occupancy & FrontNeighborBit[v, offset]) == 0UL;
  bool canRetreat = BackNeighborBit[v, offset]  != 0UL && (occupancy & BackNeighborBit[v, offset])  == 0UL;
  ```
- **Default Initial State (8-Vehicle `6x6` Setup):**
  - `v0` (Target Red Car, Horizontal `2x1`, Row `2`, Initial Offset `1` -> cells `13, 14`)
  - `v1` (Vertical Truck `1x3`, Col `3`, Initial Offset `1` -> cells `9, 15, 21`)
  - `v2` (Horizontal Car `2x1`, Row `0`, Initial Offset `0` -> cells `0, 1`)
  - `v3` (Vertical Car `1x2`, Col `0`, Initial Offset `3` -> cells `18, 24`)
  - `v4` (Horizontal Truck `3x1`, Row `5`, Initial Offset `2` -> cells `32, 33, 34`)
  - `v5` (Vertical Car `1x2`, Col `5`, Initial Offset `0` -> cells `5, 11`)
  - `v6` (Horizontal Car `2x1`, Row `4`, Initial Offset `1` -> cells `25, 26`)
  - `v7` (Vertical Car `1x2`, Col `2`, Initial Offset `0` -> cells `2, 8`)

---

## 4. Required Interactive Features & Inspector Panels

### A. Top Control & Mode Bar
1. **Architecture Mode Selector:** Instant switching between:
   - `15-Puzzle: Nibble-Packed uint64 (4x4)`
   - `Klotski: Bitboards + 60-Bit Canonical Key (4x5)`
   - `Rush Hour: 1D Offset Packing + 36-Bit Lookup (6x6)`
2. **Board Action Controls:**
   - `Random Valid Move`: Selects and executes a random legal move in the current mode.
   - `Step Optimal / Hint (BFS)`: Runs the core BFS solver from the current state (or steps 1 move along the shortest path toward the goal/next state).
   - `Undo Move`: Reverts to the previous state in the move history.
   - `Reset Board`: Restores the current mode's initial configuration.

### B. Interactive 2D Spatial Board
1. **Grid & Cell Indexing:** Displays the 2D board (`4x4`, `4x5`, or `6x6`) with every cell labeled with its bit/nibble index (`0..15`, `0..19`, or `0..35`) and `(row, col)` coordinates.
2. **Legal Move Affordances:**
   - Automatically detects all pieces/tiles that have at least one legal slide from the current state.
   - Displays clear directional indicators (e.g., clickable directional arrows on the block, or clicking a block/adjacent empty space) so the user can execute any legal move directly on the board.
   - If a piece can slide in multiple directions (e.g., a `1x1` block in Klotski next to two empty cells, or a Rush Hour car that can slide both forward and backward), allow the user to choose the specific direction.
3. **Bidirectional Hover Inspection:**
   - Hovering over a block or cell on the 2D board highlights its corresponding nibble/bit-group in the 64-Bit State Register panel and its bits in the Occupancy Bitboard matrix.
   - Hovering over a bit-group in the 64-Bit State Register highlights the corresponding cell or vehicle on the 2D board.

### C. Live 64-Bit State Register Inspector
1. **Full 64-Bit Hex & Binary Readout:**
   - Displays the raw 64-bit integer value (`0x...`) representing the current packed state (`PackedNibbles` for 15-Puzzle, `CanonicalKey` for Klotski, `PackedOffsets` for Rush Hour), along with its byte footprint (`8 bytes`).
2. **Segmented Bit-Group Breakdown:**
   - Renders the 64-bit register broken down into its semantic bit fields:
     - **15-Puzzle:** 16 groups of 4-bit nibbles (`Nibble 15 .. Nibble 0`), showing cell index, 4-bit binary, hex character, and tile value. Also display the companion 64-bit `InversePackedNibbles` (`tile -> cell`).
     - **Klotski:** 20 groups of 3-bit shape codes (`Cell 19 .. Cell 0`), showing cell index, 3-bit binary (`000..100`), and shape class name (`Empty`, `1x1`, `1x2V`, `2x1H`, `2x2`).
     - **Rush Hour:** 8 groups of 3-bit lane offsets (`Vehicle 7 .. Vehicle 0`), showing vehicle ID, fixed lane/axis, 3-bit binary (`000..100`), and integer offset (`0..4`).
3. **Mutation Delta Highlighting:**
   - Whenever a move occurs, visually distinguish/highlight the exact nibbles or 3-bit groups that changed compared to the previous state, showing `Before -> After` values.

### D. Live Binary Occupancy Bitboard Panel
1. **2D Bit Matrix (`1` = Occupied, `0` = Empty):**
   - Renders the live `ulong OccupancyMask` both as a 64-bit hex literal (`0x...`) and as a 2D binary matrix matching the board dimensions (`4x4`, `4x5`, or `6x6`) with bit indices.
2. **Selected / Active Piece Mask Overlay:**
   - When a piece is hovered or moved in Klotski or Rush Hour, displays that individual piece's 64-bit mask (`PieceMasks[i]` or `VehicleMasks[v, offset]`) alongside `otherPieces = OccupancyMask ^ pieceMask`.

### E. Live Bitwise Move & Collision Formula Trace
Whenever a move is executed (or when a legal move is hovered for preview), display a step-by-step trace of the exact machine-level bitwise operations with the actual current hex/binary values substituted in:
- **15-Puzzle Trace:** Shows source/blank nibble positions, `tile` extraction shift, `xorMask` construction, and `oldState ^ xorMask = newState`.
- **Klotski Trace:** Shows `otherPieces = OccupancyMask ^ pieceMask`, boundary mask check `(pieceMask & edgeMask) == 0`, shifted footprint `shiftedMask = pieceMask << shift` (or `>> shift`), collision check `(otherPieces & shiftedMask) == 0`, and the updated 60-bit `CanonicalKey`.
- **Rush Hour Trace:** Shows 3-bit offset extraction `(PackedOffsets >> (v * 3)) & 0b111UL`, lookup of `FrontNeighborBit[v, offset]` or `BackNeighborBit[v, offset]`, single-bit collision check `(OccupancyMask & neighborBit) == 0`, and the bit-clear + bit-OR update to `PackedOffsets`.

---

## 5. Verification & Unit Test Requirements (`SlidingPuzzle.Lab1.Tests`)
Include xUnit tests verifying:
1. **15-Puzzle:** Nibble packing/unpacking round-trips for all 16 squares, inverse state consistency (`state.GetTileAt(inverse.GetSquareOf(t)) == t`), and XOR move generation.
2. **Klotski:** Initial `OccupancyMask == 0x9FFFFUL`, boundary wrap prevention on columns `0` and `3`, and proof that swapping any two identical `1x1` or `1x2` blocks yields the exact same 60-bit `CanonicalKey`.
3. **Rush Hour:** 3-bit offset packing/unpacking, 36-bit occupancy reconstruction from `VehicleMasks`, and legal move generation for the 8-vehicle setup.

Run `dotnet build` and `dotnet test` to ensure zero warnings or failures.