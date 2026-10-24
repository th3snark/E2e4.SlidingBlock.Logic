# SPEC_EXT1: Six Bitwise Architectural Extensions for `E2e4.SlidingBlock.Logic` (.NET 10 / C# 14)

## 1. Objective & Integration Rules
Extend the existing `PuzzleTopology` and `PolyominoState` object models in the **`E2e4.SlidingBlock.Logic`** project (`.NET 10` / `C# 14`) with six targeted bitwise extensions that unlock rotating polyominoes, Sokoban pushing, nested hollow-carrier blocks, frictionless ice/ricochet raycasting, multi-level bridges/overpasses, pressure-plate gates/diodes, hexagonal axial grids, and 128-bit boards (`UInt128`)—while maintaining $O(1)$ bitwise collision checks and permutation-invariant Zobrist hashing.

### Codebase Integration Mandate
1. **Inspect First:** Before writing code, inspect all existing files in `E2e4.SlidingBlock.Logic` and its test project(s) to match existing namespaces, naming conventions, and public signatures.
2. **Zero Regressions / Backward Compatibility:** Keep existing `ulong` callers and laboratory applications compiling without breaking changes by providing default values on new properties (e.g., `ElevationMask = 0b01`, `SlidePhysics = ContinuousFloodFill`, `GridGeometry = Orthogonal4Way`) and retaining non-generic `PuzzleTopology` / `PolyominoState` entry points (or `using` / wrapper types aliased to `<ulong>`).
3. **Zero Heap Allocations in Hot Paths:** Use `readonly record struct`, `[InlineArray]`, `Span<T>` / `ReadOnlySpan<T>`, and hardware intrinsics via `IBinaryInteger<TBitboard>.TrailingZeroCount` and `PopCount`.

---

## 2. Extension 1: Generic `TBitboard` (`ulong` / `UInt128`) & Hexagonal Axial Geometry

### A. Generic Bitboard Parameterization
Parameterize the core topology and state types over `.NET 10` generic math so the JIT emits specialized register code for both 64-bit (`ulong`, up to 64 cells) and 128-bit (`UInt128`, up to 128 cells) boards:
```csharp
public sealed class PuzzleTopology<TBitboard>
    where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>

public struct PolyominoState<TBitboard> : IEquatable<PolyominoState<TBitboard>>
    where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>
```
- Replace hardcoded `1UL << index` with `TBitboard.One << index`.
- Replace `BitOperations.TrailingZeroCount(mask)` with `int.CreateChecked(TBitboard.TrailingZeroCount(mask))`.
- Replace `mask &= mask - 1UL` with `mask &= mask - TBitboard.One`.

### B. Grid Geometry & Axial Hex Support
Add `GridGeometry` to `PuzzleTopology<TBitboard>`:
```csharp
public enum GridGeometry : byte
{
    Orthogonal4Way = 0, // Cardinal deltas: [-1, +1, -W, +W]
    HexagonalAxial = 1  // Axial (q, r) skewed row-major deltas: [-1, +1, -W, +W, -W + 1, +W - 1]
}
```
- Expose `ReadOnlySpan<int> GetDirectionDeltas()` on `PuzzleTopology<TBitboard>` returning the 4 orthogonal deltas or 6 axial hex deltas:
  - `Dir 0 (West / -q): -1`
  - `Dir 1 (East / +q): +1`
  - `Dir 2 (North / -r): -Width`
  - `Dir 3 (South / +r): +Width`
  - `Dir 4 (NorthEast / +q, -r): -Width + 1` *(Hex only)*
  - `Dir 5 (SouthWest / -q, +r): +Width - 1` *(Hex only)*
- LSB normalization (`bitOffset = dRow * Width + dCol`) remains strictly non-negative (`>= 0`) for both orthogonal and axial hexagonal grids whenever `|dCol| < Width`.

---

## 3. Extension 2: Orientation Rings & Swept-Clearance Masks (Polyomino Rotation)

Treat each fixed rotation ($0^\circ, 90^\circ, 180^\circ, 270^\circ$) of a rotatable piece as a distinct `EquivalenceGroup<TBitboard>` linked in a circular ring.

### A. `EquivalenceGroup<TBitboard>` Rotation Metadata
Add the following properties to `EquivalenceGroup<TBitboard>`:
- `bool CanRotate { get; init; }` (default `false`)
- `int NextCwGroupId { get; init; }` (`-1` if `CanRotate == false`)
- `int NextCcwGroupId { get; init; }` (`-1` if `CanRotate == false`)
- `int BoxOriginDelta { get; init; }` — offset from the piece's LSB `Anchor` to the top-left square $B$ of its rotation bounding box (`boxOrigin = currAnchor + BoxOriginDelta`). Necessary when the top-left cell of the rotation box is empty (e.g., the $270^\circ$ `┘` L-tromino where LSB is at $B + 1$, so `BoxOriginDelta = -1`).
- `int AnchorDeltaCw { get; init; }` — signed LSB anchor delta when rotating $90^\circ$ CW (`nextAnchor = currAnchor + AnchorDeltaCw`).
- `int AnchorDeltaCcw { get; init; }` — signed LSB anchor delta when rotating $90^\circ$ CCW (`nextAnchor = currAnchor + AnchorDeltaCcw`).
- `TBitboard SweptClearanceMask { get; init; }` — origin-normalized mask of all cells swept during rotation relative to `boxOrigin` (e.g., `(1<<0) | (1<<1) | (1<<Width) | (1<<(Width+1))` for a $2 \times 2$ rotation box).

### B. $O(1)$ Bitwise Rotation Check & Group Transfer
1. Given a piece in `currGroup` at `currAnchor`:
   ```csharp
   int boxOrigin  = currAnchor + group.BoxOriginDelta;
   int nextGroup  = clockwise ? group.NextCwGroupId : group.NextCcwGroupId;
   int nextAnchor = currAnchor + (clockwise ? group.AnchorDeltaCw : group.AnchorDeltaCcw);
   ```
2. Rotation is legal iff:
   - `nextAnchor >= 0` and bit `nextAnchor` is set in `ValidAnchorsMask[nextGroup]`.
   - `((otherLayerOccupancy | activeDynamicWalls) & (group.SweptClearanceMask << boxOrigin)) == TBitboard.Zero`.
3. Applying the rotation transfers the piece's anchor bit between groups and updates the Zobrist hash in $O(1)$:
   ```csharp
   state.GroupAnchors[currGroup] ^= TBitboard.One << currAnchor;
   state.GroupAnchors[nextGroup] ^= TBitboard.One << nextAnchor;
   state.ZobristHash ^= ZobristTable[currGroup, currAnchor] ^ ZobristTable[nextGroup, nextAnchor];
   ```

---

## 4. Extension 3: Coupled Kinematics — Push, Carry & Magnetic Clusters (`CompositeMove`)

Support multi-piece atomic state transitions for **Sokoban pushing**, **nested hollow-carrier frames (U/O blocks)**, and **magnetic/latched blocks**.

### A. `RoleFlags` & `CompositeMove` Structs
```csharp
[Flags]
public enum RoleFlags : byte
{
    None          = 0,
    Pusher        = 1 << 0, // Can push adjacent Pushable pieces in direction of travel
    Pushable      = 1 << 1, // Can be displaced by a Pusher or HollowCarrier
    HollowCarrier = 1 << 2, // Concave frame that carries Pushable pieces trapped in its pocket
    Magnetic      = 1 << 3  // Moves in lockstep with orthogonally touching Magnetic pieces
}

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
    public byte Count;          // 1..4 pieces moved atomically
    public sbyte DirectionIndex; // -1 for Rotation, 0..5 for directional slide
    public MoveDeltaBuffer Deltas;

    public ReadOnlySpan<PieceDelta> AsSpan() => ((ReadOnlySpan<PieceDelta>)Deltas)[..Count];
}
```

### B. Unified Bitwise Push & Carrier Resolution
When piece `p` (with `Pusher`, `HollowCarrier`, or `Magnetic`) attempts to step by `delta`:
1. Start a worklist/cluster with `p`. If `p` has `Magnetic`, flood-fill any orthogonally adjacent pieces that also have `Magnetic` into the moving cluster.
2. Compute the shifted footprint of the moving cluster: `shiftedClusterMask`.
3. Check if `shiftedClusterMask` overlaps any non-cluster piece `q` on the same elevation layer(s):
   - Note: For a `HollowCarrier` (like a U-pentomino with a $1 \times 1$ block inside its open pocket), shifting in a closed direction automatically overlaps the inner block's bit, whereas shifting away from the open mouth produces zero overlap and leaves the inner block behind!
   - If an overlapping piece `q` has `RoleFlags.Pushable` (and `p` has `Pusher` or `HollowCarrier`), add `q` to the moving cluster (up to `MaxClusterSize = 4`), check `ValidAnchorsByDir[q.Group, dir]` at `q.Anchor + delta`, and repeat step 2 to support chained pushes if configured (`MaxPushDepth`).
   - If any overlapping piece is not `Pushable`, or any piece in the cluster violates `ValidAnchorsByDir` or hits an active wall/unpushed piece, reject the move.
4. Emit a single `CompositeMove` containing a `PieceDelta` for every piece in the cluster. Applying `CompositeMove` updates `GroupAnchors` and XORs `ZobristTable[d.OldGroupId, d.OldAnchor] ^ ZobristTable[d.NewGroupId, d.NewAnchor]` for each delta `d` in `move.AsSpan()`.

---

## 5. Extension 4: Frictionless Raycast & Slide Physics (`SlidePhysics`)

Add `SlidePhysics` to `EquivalenceGroup<TBitboard>`:
```csharp
public enum SlidePhysics : byte
{
    SingleStep          = 0, // Moves 1 cell per turn (required for coupled push chains)
    ContinuousFloodFill = 1, // Standard Klotski/Polyomino multi-step + hook moves via register BFS
    FrictionlessRaycast = 2  // Ice / Ricochet Robots: glides along ray until blocked; only terminal square is legal
}
```

### Bitwise Raycast Move Generator
When `group.SlidePhysics == SlidePhysics.FrictionlessRaycast`:
- For each direction `dir` (`delta = deltas[dir]`):
  ```csharp
  int curr = startAnchor;
  while (true)
  {
      int next = curr + delta;
      if ((uint)next >= (uint)cellCount) break;
      if (((ValidAnchorsByDir[groupId, dir] >> next) & TBitboard.One) == TBitboard.Zero) break;

      TBitboard nextFootprint = group.BaseMask << next;
      if (((otherLayerOccupancy | activeDynamicWalls) & nextFootprint) != TBitboard.Zero) break;

      curr = next;
  }
  if (curr != startAnchor)
  {
      // Emit ONLY 'curr' as the legal destination for direction 'dir'
  }
  ```
- Also expose a diagnostic helper `TraceRaycast(in PolyominoState<TBitboard> state, int pieceId, Span<RaycastTrace<TBitboard>> results)` that records `StartAnchor`, `TerminalAnchor`, `StepCount`, `SweptCellsMask`, and `BlockingBitsMask` for UI visualization.

---

## 6. Extension 5: Multi-Plane Collision Layers (Bridges, Overpasses & 3D Grids)

Support overlapping vertical layers (e.g., low blocks sliding underneath elevated bridges, versus tall blocks occupying both layers).

### A. Layer Masks & Occupancy Storage
- Add `byte ElevationMask { get; init; } = 0b01;` to `EquivalenceGroup<TBitboard>` (where bit `0` = Ground Layer `0`, bit `1` = Upper Layer `1`, `0b11` = Full-Height Tall Block).
- In `PolyominoState<TBitboard>`, store a 2-element inline buffer of layer occupancies alongside the combined occupancy:
  ```csharp
  [System.Runtime.CompilerServices.InlineArray(2)]
  public struct LayerOccupancyBuffer<T> where T : unmanaged { private T _layer0; }

  public LayerOccupancyBuffer<TBitboard> LayerOccupancy;
  public readonly TBitboard TotalOccupancy => LayerOccupancy[0] | LayerOccupancy[1];
  ```
- Helper method on `PolyominoState<TBitboard>`:
  ```csharp
  public readonly TBitboard GetRelevantOccupancy(byte elevationMask, TBitboard selfMask)
  {
      TBitboard occ = TBitboard.Zero;
      if ((elevationMask & 0b01) != 0) occ |= LayerOccupancy[0];
      if ((elevationMask & 0b10) != 0) occ |= LayerOccupancy[1];
      return occ ^ selfMask;
  }
  ```
- **Zero Impact on Canonical Hashing:** Because `GroupAnchors[g]` and `ZobristTable[g, anchor]` are indexed by `EquivalenceGroupId`, a ground-level piece (`Group A`, `ElevationMask = 0b01`) and an upper-level bridge (`Group B`, `ElevationMask = 0b10`) can simultaneously occupy the exact same `(row, col)` anchor square without colliding in `LayerOccupancy`, `GroupAnchors`, or `ZobristHash`.

---

## 7. Extension 6: Stateful Board Cells (Pressure Plates, Dynamic Gates & Directional Diodes)

### A. Directional Diodes (One-Way Ratchets) via `ValidAnchorsByDir`
- In `PuzzleTopology<TBitboard>`, allow defining directional cell entry/exit restrictions:
  ```csharp
  public readonly record struct DirectionalDiode(int CellIndex, byte AllowedEntryDirectionsMask);
  ```
- Precompute `TBitboard[,] ValidAnchorsByDir` of dimensions `[GroupCount, DirectionCount]` (in addition to omnidirectional `ValidAnchorsMask[groupId]`):
  - Bit `nextAnchor` is `1` in `ValidAnchorsByDir[g, dir]` iff `nextAnchor` is valid in `ValidAnchorsMask[g]` AND moving group `g` into `nextAnchor` along direction `dir` does not enter any diode cell from a forbidden direction.

### B. Pressure Plates & Retractable Gates
- Add a list of `PressurePlateRule<TBitboard>` to `PuzzleTopology<TBitboard>`:
  ```csharp
  public readonly record struct PressurePlateRule<TBitboard>(
      TBitboard TriggerCellsMask,
      TBitboard GateCellsMask,
      byte TriggerElevationMask = 0b01,
      bool NormallyClosed = true); // If true: gate is solid wall UNTIL trigger is occupied
  ```
- At the start of `GenerateMoves(in PolyominoState<TBitboard> state, ...)`, compute `TBitboard activeDynamicWalls` branchlessly:
  ```csharp
  TBitboard activeDynamicWalls = TBitboard.Zero;
  foreach (ref readonly var rule in topology.PressurePlates)
  {
      TBitboard triggerOcc = state.GetRelevantOccupancy(rule.TriggerElevationMask, TBitboard.Zero);
      bool isPressed = (triggerOcc & rule.TriggerCellsMask) != TBitboard.Zero;
      bool gateClosed = rule.NormallyClosed ? !isPressed : isPressed;
      if (gateClosed)
          activeDynamicWalls |= rule.GateCellsMask;
  }
  ```
- Note: Do **not** allow a move where a piece steps *off* a pressure plate if the resulting newly-closed `GateCellsMask` would crush/overlap any piece on the board at the end of the move (`(postMoveOccupancy & postMoveActiveGates) != TBitboard.Zero`).

---

## 8. Required Unit Tests (`E2e4.SlidingBlock.Logic.Tests`)

Add a dedicated test suite (`ExtendedMechanicsTests.cs`) verifying all six extensions:
1. **`UInt128` & Hexagonal Axial Topology:**
   - Construct a `10x10` (`100`-cell) `PuzzleTopology<UInt128>` and verify LSB normalization, `ValidAnchorsMask`, move generation, and Zobrist hashing beyond bit 63.
   - Construct a hexagonal axial board (`GridGeometry.HexagonalAxial`) and verify 6-direction movement along `[-1, +1, -W, +W, -W + 1, +W - 1]` without row wrap-around.
2. **Orientation Rings & Swept Clearance:**
   - Verify 4-orientation L-tromino rotation cycle (`0° -> 90° -> 180° -> 270° -> 0°`), confirming LSB anchor shifts (`AnchorDeltaCw = +1` into $270^\circ$ and `-1` back to $0^\circ$), swept-box blocking when an obstacle occupies the empty corner of the $2 \times 2$ box, and exact Zobrist hash restoration after a full $360^\circ$ rotation.
3. **Coupled Kinematics (Sokoban Push & Hollow U-Carrier):**
   - Verify that a `HollowCarrier` U-pentomino with a `Pushable` $1 \times 1$ block in its pocket moves both pieces atomically (`CompositeMove.Count == 2`) when sliding Left, Right, or into the pocket floor, and leaves the $1 \times 1$ block behind (`CompositeMove.Count == 1`) when sliding away from the open mouth.
   - Verify that incremental multi-piece Zobrist XOR updates match from-scratch hash recomputation.
4. **Frictionless Raycast Sliding:**
   - Verify that an ice-gliding polyomino emits only terminal stopping anchors before walls or other pieces, and never emits intermediate empty squares.
5. **Multi-Plane Elevation Layers:**
   - Verify that a ground-level piece (`ElevationMask = 0b01`) slides directly underneath an elevated bridge piece (`ElevationMask = 0b10`) occupying the same cells, while a tall piece (`ElevationMask = 0b11`) is blocked by both.
6. **Pressure Plates & Directional Diodes:**
   - Verify that a closed dynamic gate blocks movement until a second piece occupies `TriggerCellsMask`, and that stepping off the trigger is illegal while another piece sits inside `GateCellsMask`.
   - Verify that a directional diode permits sliding East across a cell but blocks sliding West.

Run `dotnet build` and `dotnet test` across the entire solution and resolve all warnings or test failures.