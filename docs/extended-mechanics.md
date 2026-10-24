# Extended polyomino mechanics

The polyomino types are generic over the bitboard: `PuzzleTopology<TBitboard>`, `PolyominoState<TBitboard>`, `PolyominoDomain<TBitboard>`
with `TBitboard` = `ulong` (up to 64 cells) or `UInt128` (up to 128 cells). `Aliases.cs` keeps the short `ulong` names (`PuzzleTopology`,
`PolyominoState`, ...) for 64-bit boards; link it into a project that uses them.

| Feature | Where | Idea |
|---------|-------|------|
| Hex axial grids | `GridGeometry`, `PuzzleTopology.GetDirectionDeltas()` | Six deltas `-1, +1, -W, +W, -W+1, +W-1`; each direction has an entry mask so a shift never wraps a row |
| Elevation layers | `EquivalenceGroup.ElevationMask`, `PolyominoState.LayerOccupancy` | Bit 0 ground, bit 1 upper; a piece only collides with the layers it occupies |
| Diodes | `DirectionalDiode`, `PuzzleTopology.ValidAnchorsByDir` | Per-direction valid-anchor masks; the flood fill ANDs the mask of the direction it steps in |
| Pressure plates | `PressurePlateRule`, `PolyominoDomain.ActiveDynamicWalls` | Closed gates join the blockers; a move that closes a gate on a piece is rejected |
| Ice / single step | `SlidePhysics`, `PolyominoDomain.TraceRaycast` | A glide emits only its terminal anchor |
| Rotation | `PuzzleTopologyBuilder.AddRotatableGroup`, `CompositeDomain` | A ring of groups, one per orientation; the swept `N x N` box must be clear; the piece moves between groups |
| Push, carry, magnetic | `RoleFlags`, `CompositeMove`, `CompositeDomain` | A moving cluster of up to 4 pieces; every `PieceDelta` XORs its own Zobrist pair |

`ElevationMask`, `SlidePhysics` and `Roles` default to the classic behaviour (`0b01`, flood fill, none).
A hollow carrier moving towards its open mouth lifts the block in its pocket (the shifted floor overlaps it); moving away leaves it behind.
Zobrist keys now have 128 columns, so hash values for `ulong` boards differ from earlier versions.
