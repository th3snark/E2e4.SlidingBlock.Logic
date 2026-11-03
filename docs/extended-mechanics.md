# Extended polyomino mechanics

The polyomino types are generic over the bitboard: `PuzzleTopology<TBitboard>`, `PolyominoState<TBitboard>`, `PolyominoDomain<TBitboard>`
with `TBitboard` = `Bitboard64` (up to 64 cells) or `Bitboard128` (up to 128 cells); both convert implicitly from `ulong`. Write the type argument out (`PolyominoDomain<Bitboard64>`), or add your own
`global using PolyominoDomain = E2e4.SlidingBlock.Logic.PolyominoDomain<E2e4.SlidingBlock.Logic.Bitboard64>;` for 64-bit boards.

| Feature | Where | Idea |
|---------|-------|------|
| Hex axial grids | `GridGeometry`, `PuzzleTopology.GetDirectionDeltas()` | Six deltas `-1, +1, -W, +W, -W+1, +W-1`; each direction has an entry mask so a shift never wraps a row |
| Elevation layers | `EquivalenceGroup.ElevationMask`, `PolyominoState.OccupancyMask` | Bit 0 ground, bit 1 upper; a piece only collides with the layers it occupies |
| Diodes | `DirectionalDiode`, `PuzzleTopology.ValidAnchorsFor(group, direction)` | Per-direction valid-anchor masks; the flood fill ANDs the mask of the direction it steps in |
| Partitions | `PuzzleTopologyBuilder.AddPartition`, `Partition`, `PuzzleTopology.Partitions` | An immovable edge between two cells (Hordern's B41, B42); folded into the same per-direction masks as diodes, so a move generator pays one extra AND per step |
| Pressure plates | `PressurePlateRule`, `PolyominoDomain.ActiveDynamicWalls` | Closed gates join the blockers; a move that closes a gate on a piece is rejected |
| Ice / single step | `SlidePhysics`, `PolyominoDomain.TraceRaycast` | A glide emits only its terminal anchor |
| Rotation | `PuzzleTopologyBuilder.AddRotatableGroup`, `CompositeDomain` | A ring of groups, one per orientation; the swept `N x N` box must be clear; the piece moves between groups |
| Push, carry, magnetic | `RoleFlags`, `CompositeMove`, `CompositeDomain` | A moving cluster of up to 4 pieces; every `PieceDelta` XORs its own Zobrist pair |

`ElevationMask`, `SlidePhysics` and `Roles` default to the classic behaviour (`0b01`, flood fill, none).
A hollow carrier moving towards its open mouth lifts the block in its pocket (the shifted floor overlaps it); moving away leaves it behind.
Zobrist keys now have 128 columns, so hash values for `ulong` boards differ from earlier versions.
A partition is stored once, in a canonical form (the neighbour has the higher cell index), and declared with a `GridDirection` from either side. It forbids entering the far cell for the step that would cross it, in both directions, so it holds in the middle of a flood fill, a glide or a push. A rotation needs the whole `N x N` box free of partitions as well as of pieces. A glide that a partition stops reports the cell across it as its blocking bit (`PuzzleTopology.ForbiddenEntryCells`).
