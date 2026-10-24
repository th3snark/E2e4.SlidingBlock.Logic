# SPEC_LAB3: Extended Mechanics Bitboard Sandbox (.NET 10 / C# 14 / WPF)

## 1. Purpose & Scope
Build a C# 14 / .NET 10 (`net10.0-windows`) WPF Desktop application and supporting core class library (`SlidingPuzzle.Core`) that implements the **Extended Mechanics Bitboard Sandbox (Lab 3)**.

This laboratory extends the polyomino bitboard and permutation-invariant Zobrist architecture from Labs 1 and 2 to support three advanced sliding-puzzle mechanics on a `6x6` grid (`36` bits, indices `0..35`):
1. **Polyomino Rotation via Orientation Rings & Swept-Clearance Masks:** Rotating non-rectangular pieces (`90° CW` / `90° CCW`) by cycling across linked `EquivalenceGroup` templates while enforcing physical $2 \times 2$ swept-area collision checks and handling LSB anchor shifts when the bounding-box corner becomes empty.
2. **Coupled Multi-Piece Kinematics (`CompositeMove` — Hollow U-Carrier & Sokoban Pushing):** Moving 1 to 4 pieces atomically in a single turn when a pusher/carrier piece's shifted bitboard footprint displaces a pushable or nested inner piece, updating multiple `GroupAnchors` masks and applying a 4-term Zobrist XOR update in $O(1)$ time.
3. **Frictionless Raycast Sliding (Ricochet Robots / Ice Gliding Physics):** Replacing the continuous BFS flood-fill with a directional ray-marching loop where pieces glide frictionlessly along cardinal rays and can only stop at the final square immediately before a board boundary, static bumper wall, or blocking piece.

> **UI Design Autonomy:** You have full freedom over the visual design, layout, color scheme, typography, and control styling of the WPF application, provided every mathematical model, bitwise operation, interactive control, and diagnostic inspector specified below is implemented.

---

## 2. Target Architecture & Solution Structure
1. **`SlidingPuzzle.Core` (`net10.0` Class Library):**
   - If building inside an existing solution with Labs 1 and 2, extend `SlidingPuzzle.Core`; otherwise create `SlidingPuzzle.Core` containing the topology, `CompositeMove`, rotation ring descriptors, coupled push/carry solver, and raycast move generator.
   - Use C# 14 features (`[InlineArray]`, `field`-backed properties, `ReadOnlySpan<T>`, and `System.Numerics.BitOperations`).
2. **`SlidingPuzzle.Lab3.Wpf` (`net10.0-windows` WPF Application):**
   - Hosts the interactive `6x6` board, mechanic mode selector, rotation/push/raycast visual affordances, diagnostic bitmask overlays, and live `GroupAnchors` + multi-term Zobrist XOR inspector.
3. **`SlidingPuzzle.Lab3.Tests` (`net10.0` xUnit Test Project):**
   - Automated unit tests verifying 4-orientation rotation rings, LSB anchor offset adjustments, swept-box collision blocking, emergent U-carrier coupling/uncoupling, Sokoban push chains, and frictionless raycast stopping conditions.

---

## 3. Mathematical & Bitwise Engine Specification (The Three Extended Mechanics)

All three modes operate on a **`6x6` grid** (`Width = 6`, `Height = 6`, `36` cells indexed `0..35` in row-major order: `bitIndex = row * 6 + col`).

### Mode 1: Polyomino Rotation via Orientation Rings & Swept-Clearance Masks

#### A. Why Rotating an LSB-Normalized Polyomino Shifts Its Anchor
In a bitboard engine without rotation, each fixed orientation is a separate puzzle piece type. To allow rotation while preserving permutation-invariant Zobrist hashing and LSB normalization (`BitOperations.TrailingZeroCount`), treat the 4 orientations ($0^\circ, 90^\circ, 180^\circ, 270^\circ$) of a rotatable shape as **4 distinct `EquivalenceGroup` definitions linked in a circular ring**.

Consider an **L-Tromino** rotating inside a $2 \times 2$ bounding box on a `6x6` board (`Width = 6`). Let $B$ be the top-left square of the $2 \times 2$ bounding box (so the $2 \times 2$ box occupies squares $B, B+1, B+6, B+7$, with relative `SweptBox2x2Mask = (1UL << 0) | (1UL << 1) | (1UL << 6) | (1UL << 7) = 0x000000C3UL`):

| Orientation Group | Occupied Box Cells `(dRow, dCol)` | Board Bits Occupied | First Occupied Bit (`LSB Anchor`) | `BoxOriginDelta` (`B - Anchor`) | LSB-Normalized `BaseMask` (`Width = 6`) | `AnchorDeltaCw` (to next group) | `AnchorDeltaCcw` (to prev group) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **`Group 0` ($0^\circ$, `L`)** | `(0,0), (1,0), (1,1)` | $B+0, B+6, B+7$ | $B + 0$ | `0` | `0x000000C1UL` (`bits 0, 6, 7`) | `0` (`-> Group 1`) | `+1` (`-> Group 3`) |
| **`Group 1` ($90^\circ$ CW, `┌`)** | `(0,0), (0,1), (1,0)` | $B+0, B+1, B+6$ | $B + 0$ | `0` | `0x00000043UL` (`bits 0, 1, 6`) | `0` (`-> Group 2`) | `0` (`-> Group 0`) |
| **`Group 2` ($180^\circ$ CW, `┐`)** | `(0,0), (0,1), (1,1)` | $B+0, B+1, B+7$ | $B + 0$ | `0` | `0x00000083UL` (`bits 0, 1, 7`) | `+1` (`-> Group 3`) | `0` (`-> Group 1`) |
| **`Group 3` ($270^\circ$ CW, `┘`)** | `(0,1), (1,0), (1,1)` *(top-left `(0,0)` is empty!)* | $B+1, B+6, B+7$ | **$B + 1$** | **`-1`** | `0x00000061UL` (`bits 0, 5, 6`) | `-1` (`-> Group 0`) | `-1` (`-> Group 2`) |

Notice **Orientation `Group 3` (`┘`)**: because the top-left cell $(0,0)$ of its $2 \times 2$ bounding box is empty, its first occupied cell in row-major order (LSB) is $(0,1)$, which is $B + 1$.
- Relative to `anchor = B + 1`, its cells sit at bit offsets `0` ($B+1$), `5` ($B+6$), and `6` ($B+7$), giving `BaseMask[3] = 0x00000061UL`.
- Its $2 \times 2$ rotation bounding-box origin $B$ is at `boxOrigin = anchor + BoxOriginDelta = anchor - 1`.
- Rotating $90^\circ$ CW from `Group 2` (`anchor = B`) to `Group 3` (`anchor = B + 1`) shifts the LSB anchor by `AnchorDeltaCw = +1`. Rotating $90^\circ$ CW from `Group 3` (`anchor = B + 1`) back to `Group 0` (`anchor = B`) shifts the LSB anchor by `AnchorDeltaCw = -1`.

#### B. Bitwise Swept-Clearance Rotation Check & Zobrist Transfer
To rotate a piece currently in `currGroup` at `currAnchor` to `nextGroup` (`NextCwGroupId` or `NextCcwGroupId`):
1. Compute the $2 \times 2$ pivot box origin and new anchor:
   ```csharp
   int boxOrigin  = currAnchor + groups[currGroup].BoxOriginDelta;
   int nextAnchor = currAnchor + (clockwise ? groups[currGroup].AnchorDeltaCw : groups[currGroup].AnchorDeltaCcw);
   ```
2. Verify that `nextAnchor` is valid in `ValidAnchorsMask[nextGroup]` AND that no other piece or static wall obstructs the $2 \times 2$ swept rotation box:
   ```csharp
   ulong otherOccupancy = (state.OccupancyMask ^ currentPieceMask) | topology.StaticWallsMask;
   ulong sweptFootprint = SweptBox2x2Mask << boxOrigin; // 0x000000C3UL << boxOrigin
   bool canRotate = ((ValidAnchorsMask[nextGroup] >> nextAnchor) & 1UL) != 0UL
                 && (otherOccupancy & sweptFootprint) == 0UL;
   ```
3. **Atomic Group Transfer & Zobrist Update ($O(1)$):**
   When rotation succeeds, the piece transfers its bit from `GroupAnchors[currGroup]` to `GroupAnchors[nextGroup]`:
   ```csharp
   state.GroupAnchors[currGroup] ^= 1UL << currAnchor;
   state.GroupAnchors[nextGroup] ^= 1UL << nextAnchor;
   state.ZobristHash ^= ZobristTable[currGroup, currAnchor] ^ ZobristTable[nextGroup, nextAnchor];
   ```

#### C. Default Mode 1 Initial Configuration (`6x6` Grid)
- **Rotatable Master L-Tromino (`Piece 0`):** Starts in `Group 0` ($0^\circ$, `BaseMask = 0x000000C1UL`) at `anchor = 7` (`row 1, col 1`), occupying bits `7, 13, 14`. Its $2 \times 2$ swept rotation box covers bits `7, 8, 13, 14` (`0x00006180UL`).
- **Movable `1x1` Blocker A (`Piece 1`, `Group 4`, `BaseMask = 0x1UL`):** Starts at `anchor = 8` (`row 1, col 2`)—**nested directly inside the empty corner of the L-Tromino's $2 \times 2$ swept rotation box!**
  - Initially, attempting to rotate the L-Tromino is **blocked** because `otherOccupancy & sweptFootprint` collides at bit `8` (`0x00000100UL`).
  - Once the user slides `1x1 Blocker A` out of bit `8` (e.g., right to bit `9` or up to bit `2`), bit `8` clears and the `Rotate 90° CW` and `Rotate 90° CCW` actions immediately unlock.
- **Movable `1x1` Blocker B (`Piece 2`, `Group 4`, `BaseMask = 0x1UL`):** Starts at `anchor = 20` (`row 3, col 2`). Shares `Group 4` with Blocker A.
- **Static Wall Square:** Bit `28` (`row 4, col 4`, `StaticWallsMask = 1UL << 28`).

---

### Mode 2: Coupled Multi-Piece Kinematics (Hollow U-Carrier & Sokoban Pushing)

#### A. Data Structures: `RoleFlags` & `CompositeMove`
Define kinematic role flags on `EquivalenceGroup`:
```csharp
[Flags]
public enum RoleFlags : byte
{
    None       = 0,
    Pusher     = 1 << 0, // Can push/carry Pushable pieces when moving into them
    Pushable   = 1 << 1, // Can be displaced by a Pusher or Carrier
    HollowCup  = 1 << 2  // Concave polyomino that naturally carries Pushable blocks inside its pocket
}
```
Define a zero-allocation `CompositeMove` capable of moving up to 4 coupled pieces in a single state transition:
```csharp
public readonly record struct PieceDelta(
    byte PieceId,
    byte OldGroupId,
    byte NewGroupId,
    byte OldAnchor,
    byte NewAnchor);

[System.Runtime.CompilerServices.InlineArray(4)]
public struct MoveDeltaBuffer { private PieceDelta _element0; }

public struct CompositeMove
{
    public int Count;
    public int DirectionDelta; // -1 (Left), +1 (Right), -6 (Up), +6 (Down), or 0 (Rotation)
    public MoveDeltaBuffer Deltas;
}
```

#### B. Emergent Bitboard Coupling (Why No Special "Pocket" Code Is Needed)
Consider a **Hollow U-Pentomino (`3 wide × 2 high`)** open at the top, occupying relative cells `(0,0), (0,2), (1,0), (1,1), (1,2)` on the `6x6` board:
- Bit offsets relative to LSB `(0,0)`: `0, 2, 6, 7, 8` -> `BaseMask = (1UL<<0) | (1UL<<2) | (1UL<<6) | (1UL<<7) | (1UL<<8) = 0x000001C5UL`.
- Its concave interior pocket is at relative cell `(0,1)` (`anchor + 1`), which is **not** set in `BaseMask`.
- Suppose a `Pushable` `1x1` Crate sits inside that pocket at `crateAnchor = uAnchor + 1`:
  1. **Sliding the U-Carrier Left (`delta = -1`), Right (`delta = +1`), or Up (`delta = -6`):**
     Shifting the U-Carrier's bitboard (`uMask << delta` or `uMask >> -delta`) causes the U-Carrier's right wall (`anchor + 2`), left wall (`anchor + 0`), or bottom floor (`anchor + 7`) to **directly overlap** the `1x1` Crate at `uAnchor + 1`!
  2. **Sliding the U-Carrier Down (`delta = +6`):**
     Because the top of the `U` is open at `(0,1)`, shifting the U-Carrier down (`uMask << 6`) moves away from `uAnchor + 1` with **zero bitboard overlap** (`(uMask << 6) & crateMask == 0`)!

Thus, a single unified **Coupled Push/Carry Resolution Algorithm** handles both Sokoban pushing and Hollow U-Carrier transport:
1. When piece `p` (with `RoleFlags.Pusher` or `RoleFlags.HollowCup`) attempts a 1-step orthogonal move by `delta` (`-1, +1, -6, +6`):
2. Check if `nextAnchor = p.Anchor + delta` is valid in `ValidAnchorsMask[p.Group]`.
3. Compute `shiftedPrimaryMask = BaseMask[p.Group] << nextAnchor`.
4. Find all other pieces `q` whose current footprint overlaps `shiftedPrimaryMask`:
   - If no pieces overlap, emit a standard 1-piece `CompositeMove` (`Count = 1`).
   - If any overlapping piece `q` does **not** have `RoleFlags.Pushable`, the move is **Blocked**.
   - If all overlapping pieces `q` have `RoleFlags.Pushable`:
     - For each pushed piece `q`, verify that `qNextAnchor = q.Anchor + delta` is valid in `ValidAnchorsMask[q.Group]`.
     - Form the combined shifted footprint `ulong clusterShiftedMask = shiftedPrimaryMask | (BaseMask[q.Group] << qNextAnchor)`.
     - Verify that `clusterShiftedMask` does not collide with `StaticWallsMask` or any remaining unpushed piece on the board (`(unpushedOccupancy & clusterShiftedMask) == 0UL`).
     - If clear, emit a single atomic `CompositeMove` (`Count = 2`, containing `PieceDelta` entries for both `p` and `q`).
5. **Atomic Multi-Piece State & Zobrist Update:**
   Iterating through `i = 0 .. move.Count - 1` updates `GroupAnchors` and XORs all terms into `ZobristHash`:
   ```csharp
   foreach (var d in move.AsSpan())
   {
       state.GroupAnchors[d.OldGroupId] ^= 1UL << d.OldAnchor;
       state.GroupAnchors[d.NewGroupId] ^= 1UL << d.NewAnchor;
       state.ZobristHash ^= ZobristTable[d.OldGroupId, d.OldAnchor]
                          ^ ZobristTable[d.NewGroupId, d.NewAnchor];
   }
   ```

#### C. Default Mode 2 Initial Configuration (`6x6` Grid)
- **Hollow U-Carrier (`Piece 0`, `Group 0`):** `BaseMask = 0x000001C5UL`, `RoleFlags = Pusher | HollowCup`, placed at `anchor = 13` (`row 2, col 1`, occupying bits `13, 15, 19, 20, 21`). Its open interior pocket is at bit `14` (`row 2, col 2`).
- **Pushable `1x1` Crate A (`Piece 1`, `Group 1`):** `BaseMask = 0x1UL`, `RoleFlags = Pushable`, placed at `anchor = 14` (`row 2, col 2` — **inside the U-Carrier's pocket**).
- **Pushable `1x1` Crate B (`Piece 2`, `Group 1`):** `BaseMask = 0x1UL`, `RoleFlags = Pushable`, placed at `anchor = 8` (`row 1, col 2` — directly above the pocket). Shares `Group 1` with Crate A.
- **Sokoban Pusher Avatar (`Piece 3`, `Group 2`):** `BaseMask = 0x1UL`, `RoleFlags = Pusher`, placed at `anchor = 2` (`row 0, col 2` — directly above Crate B).
- **Static Wall Squares:** Bits `18` (`row 3, col 0`) and `23` (`row 3, col 5`).

---

### Mode 3: Frictionless Raycast Sliding (Ricochet Robots / Ice Physics)

#### A. Ray-Marching Algorithm (`FindRaycastDestinations`)
In `FrictionlessRaycast` mode, pieces glide across low-friction ice. Once pushed in one of the 4 cardinal directions (`Left = -1`, `Right = +1`, `Up = -6`, `Down = +6`), a piece cannot stop on intermediate empty squares—it continues sliding in that direction until the very next step would either:
1. Violate `ValidAnchorsMask[g]` (hitting a board edge or a static bumper wall), OR
2. Collide with another piece on the board (`(otherOccupancy & (BaseMask[g] << nextAnchor)) != 0UL`).

Implement a dedicated ray-marching query that returns the up-to-4 terminal destinations along with rich diagnostic metadata for the inspector:
```csharp
public readonly record struct RaycastMoveResult(
    int DirectionDelta,       // -1, +1, -6, +6
    string DirectionName,     // "Left", "Right", "Up", "Down"
    int StartAnchor,
    int TerminalAnchor,       // Final legal resting anchor
    int StepCount,            // Number of cells glided (>= 1)
    ulong SweptPathCellsMask, // Bitmask of all cells traversed along the ray
    string StopReason,        // e.g., "Board Edge", "Static Bumper Wall", or "Piece Collision"
    ulong BlockingBitsMask    // Exact bit(s) that halted the further step (for UI highlighting)
);
```

#### B. Default Mode 3 Initial Configuration (`6x6` Grid)
- **Ice Glider (`1x1` Target Puck, `Piece 0`, `Group 0`):** `BaseMask = 0x1UL`, placed at `anchor = 0` (`row 0, col 0`).
- **Ice Slider Bar (`1x2` Vertical Block, `Piece 1`, `Group 1`):** `BaseMask = (1UL<<0) | (1UL<<6) = 0x00000041UL`, placed at `anchor = 19` (`row 3, col 1`, occupying bits `19, 25`).
- **Ice L-Block (`L-Tromino`, `Piece 2`, `Group 2`):** `BaseMask = 0x000000C1UL`, placed at `anchor = 15` (`row 2, col 3`, occupying bits `15, 21, 22`).
- **Static Ice Bumper Walls:** Bits `4` (`row 0, col 4`), `12` (`row 2, col 0`), and `33` (`row 5, col 3`) -> `StaticWallsMask = (1UL << 4) | (1UL << 12) | (1UL << 33) = 0x200001010UL`.
- **Target Goal Square:** Bit `20` (`row 3, col 2`)—reaching `anchor = 20` with the `1x1` Ice Glider requires using the movable polyominoes as intermediate ricochet stoppers!

---

## 4. Required Interactive Features & Inspector Panels

### A. Top Mode & Control Bar
1. **Mechanic Mode Selector:** Switch seamlessly between:
   - `Mode 1: Orientation Ring & Swept-Clearance Rotation (6x6)`
   - `Mode 2: Coupled Kinematics — Hollow U-Carrier & Sokoban Push (6x6)`
   - `Mode 3: Frictionless Ice Raycast — Ricochet Physics (6x6)`
2. **Action & Solver Controls:**
   - `Toggle Diagnostic Mask Overlay` (shows the $2 \times 2$ `SweptClearanceMask` in Mode 1, the coupled `ClusterFootprintMask` in Mode 2, or the raycast trajectory + red blocking-bumper bits in Mode 3).
   - `Step Optimal / Solve (BFS)`: Runs the generic BFS solver across the current mode's state space (in Mode 3, solves for getting the `1x1` Ice Glider to the Goal square at bit `20`).
   - `Undo Move` and `Reset Mode`.

### B. Interactive `6x6` Spatial Board View
1. **Grid & Piece Rendering:**
   - Displays all 36 squares (`0..35`) with bit indices and `(row, col)` labels, static wall/bumper squares, and merged polygon outlines for multi-cell shapes (`L-Tromino` in all 4 orientations, `Hollow U-Carrier`, `1x2` bar, and `1x1` crates/gliders).
   - Marks each piece's current **LSB Anchor (`tzcnt`)** with a badge.
2. **Mode-Specific Interactive Affordances:**
   - **In Mode 1 (Rotation):**
     - Selecting the L-Tromino shows its reachable orthogonal slide anchors AND displays two rotation controls (`Rotate 90° CW` and `Rotate 90° CCW`).
     - When rotation is blocked (e.g., while `1x1 Blocker A` is at bit `8`), display the $2 \times 2$ `SweptClearanceMask` overlay and highlight the exact obstructing bit (`Bit 8`) in red with an explanation banner.
     - When rotation is legal, clicking `Rotate 90° CW` or `Rotate 90° CCW` rotates the piece, updates its `EquivalenceGroupId` (`0 <-> 1 <-> 2 <-> 3`), and visually demonstrates the LSB anchor shifting by `+1` / `-1` when entering/leaving `Group 3` (`┘`).
   - **In Mode 2 (Coupled Kinematics):**
     - Selecting the Hollow U-Carrier or Sokoban Pusher highlights all legal 1-step directions.
     - Hovering over a direction arrow previews whether the move is a **Single-Piece Slide** (e.g., sliding the U-Carrier **Down**, leaving Crate A behind in the pocket!) or a **Coupled 2-Piece `CompositeMove`** (e.g., sliding the U-Carrier **Left**, **Right**, or **Up** while carrying Crate A, or pushing Crate B with the Pusher Avatar).
     - Clicking the arrow animates all pieces in the `CompositeMove` simultaneously.
   - **In Mode 3 (Frictionless Raycast):**
     - Selecting any piece runs `FindRaycastDestinations` for all 4 cardinal directions and draws laser ray lines from the piece to its terminal stopping squares, highlighting the stopping square and marking the bumper/piece bit that halted the ray.
     - Clicking a terminal square glides the piece smoothly along the ray to its stopping point.

### C. Live Machine Register & Bitboard Inspector Panel
1. **Selected Piece & Mechanic Breakdown:**
   - Shows `PieceId`, `EquivalenceGroupId`, `RoleFlags`, `BaseMask[g]`, `CurrentAnchor = tzcnt(mask)`, `PieceMask = BaseMask[g] << CurrentAnchor`, and `ValidAnchorsMask[g]`.
   - **Mode 1 Extra Readout:** Shows `OrientationAngle` (`0°/90°/180°/270°`), `BoxOrigin = Anchor + BoxOriginDelta`, `SweptFootprint = 0xC3UL << BoxOrigin`, and `CollisionBits = OtherOccupancy & SweptFootprint`.
   - **Mode 2 Extra Readout:** Shows the active or last executed `CompositeMove` delta table (`PieceId`, `OldGroup -> NewGroup`, `OldAnchor -> NewAnchor`, `ClusterFootprintMask`).
   - **Mode 3 Extra Readout:** Shows the 4 directional `RaycastMoveResult` entries (`Direction`, `StepsGlided`, `TerminalAnchor`, `StopReason`, `BlockingBitsMask`).
2. **Canonical `GroupAnchors` & Multi-Term Zobrist XOR Trace:**
   - Displays the 36-bit binary/hex `GroupAnchors[g]` bitboards for all groups in the active mode.
   - Displays the current 64-bit `ZobristHash` (`0x...`) alongside a live verification check (`IncrementalHash == FullRecomputedHash`).
   - Displays the complete bitwise XOR equation for the last executed transition:
     - **2-Term XOR** for standard slides, ice glides, or single-piece rotations (`ZobristTable[oldGroup, oldAnchor] ^ ZobristTable[newGroup, newAnchor]`).
     - **4-Term XOR** for coupled 2-piece pushes/carries (`ZobristTable[g1, old1] ^ ZobristTable[g1, new1] ^ ZobristTable[g2, old2] ^ ZobristTable[g2, new2]`).

---

## 5. Verification & Unit Test Requirements (`SlidingPuzzle.Lab3.Tests`)
Include xUnit tests verifying:
1. **Mode 1 (Rotation Ring & LSB Anchor Shift):**
   - Confirms that in the initial state (`L-Tromino` at `anchor = 7`, `1x1 Blocker A` at `anchor = 8`), both CW and CCW rotations are rejected because `(otherOccupancy & (0xC3UL << 7)) == (1UL << 8) != 0`.
   - Confirms that after moving `1x1 Blocker A` from bit `8` to bit `9`, rotating CW 4 times cycles through `(Group 0, Anchor 7) -> (Group 1, Anchor 7) -> (Group 2, Anchor 7) -> (Group 3, Anchor 8) -> (Group 0, Anchor 7)`, returning to the exact initial `GroupAnchors` and `ZobristHash`.
2. **Mode 2 (Emergent U-Carrier Coupling & Uncoupling + 4-Term Zobrist):**
   - Confirms that from the initial state (`U-Carrier` at `anchor = 13`, `Crate A` inside pocket at `anchor = 14`), moving the U-Carrier **Left (`-1`)**, **Right (`+1`)**, or **Up (`-6`)** produces a `CompositeMove` with `Count == 2` that moves both the U-Carrier and Crate A together.
   - Confirms that moving the U-Carrier **Down (`+6`)** produces a `CompositeMove` with `Count == 1` that moves the U-Carrier to `anchor = 19` while leaving Crate A behind at `anchor = 14`.
   - Confirms that the incremental 4-term Zobrist hash update after a coupled move matches a full from-scratch Zobrist hash recomputation.
3. **Mode 3 (Frictionless Raycast Sliding):**
   - Confirms that in the initial state, the `1x1` Ice Glider at `anchor = 0` slides Right (`+1`) for 3 steps and stops at `TerminalAnchor = 3` (`StopReason = Static Bumper Wall` at bit `4`), and slides Down (`+6`) for 1 step and stops at `TerminalAnchor = 6` (`StopReason = Static Bumper Wall` at bit `12`).
   - Confirms that intermediate squares (`1, 2`) are **not** emitted as legal moves for the Right slide.

Run `dotnet build` and `dotnet test` to ensure zero compiler warnings or test failures.