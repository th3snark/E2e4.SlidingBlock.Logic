# Glossary

Terms and acronyms used in this repository, in alphabetical order. Where a term has its own write-up, the link points to it.

| Term | Definition |
|------|------------|
| **A\*** | Best-first graph search that expands the state with the lowest `f = g + h` (cost so far plus a heuristic estimate to the goal). Optimal when the heuristic is admissible. `PuzzleSolver.SolveAStar`. |
| **Admissible heuristic** | A goal-distance estimate that never overestimates the true remaining cost. Required for A\* and IDA\* to return optimal solutions. |
| **Anchor** | The single square that identifies a piece's placement: the first occupied cell of its footprint in row-major order, i.e. `tzcnt(pieceMask)`. See [LSB normalisation](02-lsb-normalisation.md). |
| **AllowedDirections** | Enum (`None`, `Horizontal`, `Vertical`, `Both`) limiting which way a piece may slide. |
| **BaseMask** | A shape's cell pattern normalised so its anchor is bit 0; shifting it left by an anchor places the piece on the board. |
| **BFS (breadth-first search)** | Search that explores all states at distance `d` before any at `d + 1`, so the first goal it finds is a shortest solution. Used for Klotski, Rush Hour and polyominoes. Also the flood fill that computes hook moves. |
| **Bitboard** | A board (or a set of squares) stored as the bits of an integer, one bit per square, so set operations are single instructions. |
| **Bounding box** | The smallest rectangle containing a shape. Its top-left cell can be empty (T, J), which is why it is not used to locate pieces. |
| **Branchless** | Code that avoids conditional jumps, such as the XOR tile swap in the 15-puzzle. |
| **Canonical form / canonical state** | A single representative for a set of equivalent positions, so equivalent positions compare equal. `GroupAnchors` and `CanonicalShapeKey` are canonical. |
| **CanonicalShapeKey** | Klotski's 60-bit key: 3 bits per cell holding the shape class `0..4` of the piece covering it. Independent of which identical piece is where. |
| **Column mask** | Bitboard of one board column (for example `notCol0`) used to stop a left/right shift wrapping onto the next row. |
| **Corner hook** | A single-turn move in which a piece slides several squares and changes direction at least once. |
| **Degrees of freedom** | The number of independent ways a piece can move; it decides which state representation fits. See [representations](01-representations.md). |
| **Domain** | An implementation of `ISlidingBlockDomain`: the rules of one puzzle (initial state, goal test, moves). |
| **Equivalence group** | A set of interchangeable pieces (same shape, directions and goal role) sharing an `EquivalenceGroupId`. |
| **Flood fill** | Spreading outwards from a start until nothing new is reached; here, over anchors, to find every reachable destination. |
| **Frontier** | The set of newly reached anchors that the flood fill expands next. |
| **GroupAnchors** | One bitboard per equivalence group; bit `s` is set when some piece of the group is anchored on square `s`. See [Zobrist and GroupAnchors](04-zobrist-and-group-anchors.md). |
| **Goal** | The position (or condition) that solves a puzzle. Owned by the puzzle that uses the engine and passed to the domain as data. |
| **Heuristic** | An estimate of the remaining cost to the goal, such as Manhattan distance. |
| **Hook move** | See *Corner hook*. |
| **IDA\* (iterative-deepening A\*)** | A\* run as repeated depth-first searches with a growing cost bound. Uses little memory, so it suits the 15-puzzle. `PuzzleSolver.SolveIdaStar`. |
| **InlineArray** | C# 12 attribute for a struct that holds a fixed number of elements inline, with no heap allocation. |
| **Klotski** | Sliding-block puzzle on a 4x5 board: a 2x2 master block must reach the bottom centre among 1x2, 2x1 and 1x1 pieces. |
| **LSB (least-significant bit)** | The lowest-numbered set bit of an integer. For a bitboard it is the first occupied square in row-major order. See [LSB normalisation](02-lsb-normalisation.md). |
| **LSB normalisation** | Shifting a shape right by `tzcnt` so its LSB is bit 0, which guarantees non-negative offsets and a one-instruction anchor lookup. |
| **Manhattan distance** | Sum over tiles of `|row difference| + |column difference|` from the goal square; an admissible heuristic for the 15-puzzle. |
| **Master piece** | The piece that must reach the goal (the 2x2 in Klotski, the red car in Rush Hour, the L-tromino in Lab 2). |
| **N-puzzle** | Family of sliding tile puzzles with one blank; the 15-puzzle is the 4x4 case. |
| **Nibble** | 4 bits, one hex digit. The 15-puzzle stores one tile per nibble. |
| **Occupancy mask** | Bitboard of every square covered by a piece or wall. |
| **Overhang** | A cell of a shape lying left of its anchor on a lower row (`dCol < 0`, `dRow >= 1`). |
| **PDB (pattern database)** | Precomputed table of exact costs for a subset of tiles, used as a strong heuristic. The inverse-map `ulong` gives O(1) lookups. |
| **Polyomino** | A shape made of unit squares joined edge to edge (domino, L, T, J, S, Z, U...). |
| **PSPACE-complete** | Among the hardest problems solvable with polynomial memory. Sliding-block puzzles in general are, so no efficient general solver is expected. |
| **Row-major order** | Numbering squares left to right, top to bottom; bit 0 is the top-left square. |
| **Rush Hour** | Sliding-block puzzle on a 6x6 board where cars and trucks move only along their lane; the red car must reach the exit. |
| **Sentinel wall** | Extra padding squares around a bitboard that stop shifts wrapping. Not used here; `ValidAnchorsMask` replaces them. |
| **Static wall** | A permanently blocked square (for example square 12 in Lab 2). |
| **Topology** | The immutable description of a puzzle: board size, walls, groups and pieces. Built once and shared by all states. |
| **Transposition table** | Cache of already-seen states keyed by hash, so a position reached by different move orders is searched once. |
| **tzcnt (trailing-zero count)** | CPU instruction (`BitOperations.TrailingZeroCount`) giving the index of the lowest set bit; the LSB lookup. |
| **ValidAnchorsMask** | Per-group bitboard of anchors where the whole shape stays on the board and off walls. See [valid anchors and hook moves](03-valid-anchors-and-hook-moves.md). |
| **Vehicle offset** | A lane-sliding vehicle's position along its lane, `0..15`, stored in 4 bits. |
| **WPF (Windows Presentation Foundation)** | The .NET desktop UI framework used by the laboratories app. |
| **XOR (exclusive or)** | Bitwise operation that is its own inverse and is commutative and associative; the basis of cheap incremental updates and Zobrist hashing. |
| **XorShift64\*** | Fast pseudo-random generator (xor-shifts then a multiply) used to fill the Zobrist table from a fixed seed. |
| **Zobrist hashing** | Hash formed by XOR-ing a random key per (piece class, square) fact; a move updates it by XOR-ing out the old key and in the new one. See [Zobrist and GroupAnchors](04-zobrist-and-group-anchors.md). |
