using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

/// <summary>Immutable puzzle description: board size, walls, equivalence groups, pieces, board cells with state and Zobrist keys.</summary>
public sealed class PuzzleTopology<TBitboard> where TBitboard : unmanaged, IBitboard<TBitboard>
{
    internal const ulong ZobristSeed = 0x9E3779B97F4A7C15UL;

    /// <summary>Columns of <see cref="ZobristTable"/>: enough for a <c>UInt128</c> board.</summary>
    internal const int ZobristColumns = 128;

    internal PuzzleTopology(int columns, int rows, GridGeometry geometry, TBitboard walls, EquivalenceGroup<TBitboard>[] groups,
        PieceTemplate[] pieces, DirectionalDiode[] diodes, PressurePlateRule<TBitboard>[] plates, Partition[] partitions)
    {
        Columns = columns;
        Rows = rows;
        Geometry = geometry;
        StaticWallsMask = walls;
        GroupTable = groups;
        PieceTable = pieces;
        DiodeTable = diodes;
        PlateTable = plates;
        PartitionTable = partitions;
        _deltas = new int[Grid.DirectionCount(geometry)];
        EntryTable = new TBitboard[_deltas.Length];
        for (int d = 0; d < _deltas.Length; d++)
        {
            _deltas[d] = Grid.Delta(columns, d);
            EntryTable[d] = Grid.EntryMask<TBitboard>(d, columns);
        }
        ZobristTable = BuildZobrist(groups.Length);
        Groups = Array.AsReadOnly(groups);
        Pieces = Array.AsReadOnly(pieces);
        Diodes = Array.AsReadOnly(diodes);
        PressurePlates = Array.AsReadOnly(plates);
        Partitions = Array.AsReadOnly(partitions);
        _forbiddenEntry = BuildForbiddenEntry();
        _validByDir = BuildValidAnchorsByDir();
    }

    private readonly int[] _deltas;
    private readonly TBitboard[] _forbiddenEntry;
    private readonly TBitboard[] _validByDir;

    // The engine's hot paths index these arrays directly: going through the IReadOnlyList properties allocates an enumerator per foreach.
    internal EquivalenceGroup<TBitboard>[] GroupTable { get; }
    internal PieceTemplate[] PieceTable { get; }
    internal DirectionalDiode[] DiodeTable { get; }
    internal PressurePlateRule<TBitboard>[] PlateTable { get; }
    internal Partition[] PartitionTable { get; }

    /// <summary>True when some cell cannot be entered from every side (a diode or a partition), so moves have to use <see cref="ValidAnchorsFor"/>.</summary>
    internal bool HasEntryRules => DiodeTable.Length > 0 || PartitionTable.Length > 0;

    /// <summary>Per direction, the cells a step in that direction may enter without wrapping a row.</summary>
    internal TBitboard[] EntryTable { get; }

    internal ReadOnlySpan<int> DeltaTable => _deltas;

    /// <summary>Columns of the board.</summary>
    public int Columns { get; }

    /// <summary>Rows of the board.</summary>
    public int Rows { get; }

    /// <summary>How the cells touch: four neighbours or six.</summary>
    public GridGeometry Geometry { get; }

    /// <summary>Cells on the board: <see cref="Columns"/> times <see cref="Rows"/>.</summary>
    public int CellCount => Columns * Rows;

    /// <summary>The number of directions a step can take: 4 on an orthogonal board, 6 on a hex one.</summary>
    public int DirectionCount => _deltas.Length;

    /// <summary>Bit c is set when cell c is a wall.</summary>
    public TBitboard StaticWallsMask { get; }

    /// <summary>The shape classes, indexed by group id. Read-only.</summary>
    public IReadOnlyList<EquivalenceGroup<TBitboard>> Groups { get; }

    /// <summary>The pieces, indexed by piece id. Read-only.</summary>
    public IReadOnlyList<PieceTemplate> Pieces { get; }

    /// <summary>The one-way cells. Read-only.</summary>
    public IReadOnlyList<DirectionalDiode> Diodes { get; }

    /// <summary>The pressure-plate rules. Read-only.</summary>
    public IReadOnlyList<PressurePlateRule<TBitboard>> PressurePlates { get; }

    /// <summary>The immovable partitions between cells, each in its canonical form (see <see cref="Partition"/>). Read-only.</summary>
    public IReadOnlyList<Partition> Partitions { get; }

    /// <summary>
    /// Bit s is set iff a step in <paramref name="direction"/> may never enter cell s: it is a diode that refuses that direction, or the step
    /// would cross a partition. Which cell a step comes from is fixed by the cell and the direction, so this is exact for partitions.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a direction of the board's geometry.</exception>
    public TBitboard ForbiddenEntryCells(GridDirection direction) => _forbiddenEntry[CheckDirection(direction)];

    /// <summary>As <see cref="ForbiddenEntryCells"/> for a direction index the caller has already checked.</summary>
    internal TBitboard ForbiddenEntry(int direction) => _forbiddenEntry[direction];

    // Indexed [group, cell]; the hot paths read it directly.
    internal ulong[,] ZobristTable { get; }

    /// <summary>The Zobrist key of a piece of group <paramref name="groupId"/> anchored on <paramref name="cell"/>; a position's hash is the XOR of its pieces' keys.</summary>
    public ulong ZobristKey(int groupId, int cell) => ZobristTable[groupId, cell];

    /// <summary>
    /// Bit s is set iff s is a valid anchor of the group and moving the group into s along <paramref name="direction"/> does not enter a diode
    /// cell from a forbidden side or cross a partition. Equal to the group's <see cref="EquivalenceGroup{TBitboard}.ValidAnchorsMask"/> on
    /// boards with neither.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="groupId"/> names no group, or <paramref name="direction"/> is not a direction of the board's geometry.</exception>
    public TBitboard ValidAnchorsFor(int groupId, GridDirection direction)
    {
        if ((uint)groupId >= (uint)GroupTable.Length) throw new ArgumentOutOfRangeException(nameof(groupId), groupId, "No such group.");
        return ValidAnchors(groupId, CheckDirection(direction));
    }

    /// <summary>As <see cref="ValidAnchorsFor"/> for a group and direction index the caller has already checked.</summary>
    internal TBitboard ValidAnchors(int groupId, int direction) => _validByDir[groupId * _deltas.Length + direction];

    private int CheckDirection(GridDirection direction)
    {
        if ((uint)direction >= (uint)_deltas.Length)
            throw new ArgumentOutOfRangeException(nameof(direction), direction, $"A {Geometry} board has {_deltas.Length} directions.");
        return (int)direction;
    }

    /// <summary>One mask per direction for <paramref name="groupId"/> (see <see cref="ValidAnchorsFor"/>).</summary>
    internal ReadOnlySpan<TBitboard> ValidByDirRow(int groupId) => _validByDir.AsSpan(groupId * _deltas.Length, _deltas.Length);

    /// <summary>Cell index change of a one-step move per direction: -1, +1, -W, +W (and -W + 1, +W - 1 on a hex board).</summary>
    public ReadOnlySpan<int> GetDirectionDeltas() => _deltas;

    /// <summary>The cells piece <paramref name="pieceId"/> covers if its <em>starting</em> shape is anchored on <paramref name="anchor"/> (a rotated piece has another shape).</summary>
    public TBitboard PieceMask(int pieceId, int anchor) => Groups[Pieces[pieceId].GroupId].BaseMask.ShiftLeft(anchor);

    /// <summary>Starts describing a board of <paramref name="columns"/> x <paramref name="rows"/> cells.</summary>
    public static PuzzleTopologyBuilder<TBitboard> CreateBuilder(int columns, int rows, GridGeometry geometry = GridGeometry.Orthogonal4Way) =>
        new(columns, rows, geometry);

    private static ulong[,] BuildZobrist(int groups)
    {
        var table = new ulong[Math.Max(groups, 1), ZobristColumns];
        ulong state = ZobristSeed;
        for (int g = 0; g < table.GetLength(0); g++)
            for (int s = 0; s < ZobristColumns; s++)
            {
                // XorShift64*
                state ^= state >> 12;
                state ^= state << 25;
                state ^= state >> 27;
                table[g, s] = state * 0x2545F4914F6CDD1DUL;
            }
        return table;
    }

    /// <summary>Per direction, the cells no step that way may enter: refused by a diode, or on the far side of a partition.</summary>
    private TBitboard[] BuildForbiddenEntry()
    {
        var forbidden = new TBitboard[_deltas.Length];
        foreach (var diode in DiodeTable)
            for (int d = 0; d < forbidden.Length; d++)
                if (((int)diode.AllowedEntryDirections >> d & 1) == 0) forbidden[d] = forbidden[d].Or(Bit<TBitboard>.One.ShiftLeft(diode.CellIndex));
        foreach (var partition in PartitionTable)
        {
            int d = (int)partition.Direction;
            forbidden[d] = forbidden[d].Or(Bit<TBitboard>.One.ShiftLeft(partition.Cell + _deltas[d]));
            forbidden[d ^ 1] = forbidden[d ^ 1].Or(Bit<TBitboard>.One.ShiftLeft(partition.Cell));
        }
        return forbidden;
    }

    /// <summary>True when a partition lies between two cells of the square box of <paramref name="size"/> cells whose top-left cell is <paramref name="origin"/>.</summary>
    internal bool HasPartitionWithin(int origin, int size)
    {
        int row0 = origin / Columns, col0 = origin % Columns;
        foreach (var partition in PartitionTable)
        {
            int other = partition.Cell + _deltas[(int)partition.Direction];
            if (Inside(partition.Cell) && Inside(other)) return true;
        }
        return false;

        bool Inside(int cell) => (uint)(cell / Columns - row0) < (uint)size && (uint)(cell % Columns - col0) < (uint)size;
    }

    private TBitboard[] BuildValidAnchorsByDir()
    {
        int dirs = _deltas.Length, cells = CellCount;
        var table = new TBitboard[Math.Max(GroupTable.Length, 1) * dirs];
        for (int g = 0; g < GroupTable.Length; g++)
        {
            var group = GroupTable[g];
            for (int d = 0; d < dirs; d++)
            {
                TBitboard valid = group.ValidAnchorsMask;
                if (!_forbiddenEntry[d].IsEmpty)
                {
                    foreach (int n in Grid.Bits(group.ValidAnchorsMask))
                    {
                        int prev = n - _deltas[d];
                        TBitboard before = (uint)prev < (uint)cells ? group.BaseMask.ShiftLeft(prev) : Bit<TBitboard>.Zero;
                        TBitboard entered = group.BaseMask.ShiftLeft(n).AndNot(before);
                        if (entered.Intersects(_forbiddenEntry[d])) valid = valid.AndNot(Bit<TBitboard>.One.ShiftLeft(n));
                    }
                }
                table[g * dirs + d] = valid;
            }
        }
        return table;
    }
}

/// <summary>Describes a board, its shape classes and its pieces, and builds the immutable <see cref="PuzzleTopology{TBitboard}"/>.</summary>
public sealed class PuzzleTopologyBuilder<TBitboard> where TBitboard : unmanaged, IBitboard<TBitboard>
{
    private sealed record GroupDef(string Name, IReadOnlyList<(int dRow, int dCol)> Cells, TBitboard Base, AllowedDirections Dirs,
        byte Elevation, SlidePhysics Physics, RoleFlags Roles)
    {
        public int RingStart { get; init; } = -1;
        public int RingLength { get; init; }
        public int RingIndex { get; init; }
        public int BoxSize { get; init; }
        public int[] RingOffsets { get; init; } = [];
    }

    private readonly int _columns, _rows;
    private readonly GridGeometry _geometry;
    private TBitboard _walls;
    private readonly List<GroupDef> _groups = [];
    private readonly List<PieceTemplate> _pieces = [];
    private readonly List<DirectionalDiode> _diodes = [];
    private readonly List<PressurePlateRule<TBitboard>> _plates = [];
    private readonly List<Partition> _partitions = [];

    /// <summary>
    /// Starts describing a board of <paramref name="columns"/> x <paramref name="rows"/> cells.
    /// </summary>
    /// <param name="columns">The number of columns.</param>
    /// <param name="rows">The number of rows.</param>
    /// <param name="geometry">How the cells touch.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="columns"/> or <paramref name="rows"/> is below 1, or the board has more cells than the bitboard has bits.</exception>
    public PuzzleTopologyBuilder(int columns, int rows, GridGeometry geometry = GridGeometry.Orthogonal4Way)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        if ((long)columns * rows > Grid.BitWidth<TBitboard>())
            throw new ArgumentOutOfRangeException(nameof(rows), $"A board of {columns} x {rows} cells does not fit in {Grid.BitWidth<TBitboard>()} bits.");
        _columns = columns;
        _rows = rows;
        _geometry = geometry;
    }

    /// <summary>Makes cell (<paramref name="row"/>, <paramref name="col"/>) a wall.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The cell is not on the board.</exception>
    public PuzzleTopologyBuilder<TBitboard> AddWall(int row, int col)
    {
        CheckCell(row, col);
        _walls = _walls.Or(Bit<TBitboard>.One.ShiftLeft(row * _columns + col));
        return this;
    }

    /// <summary>
    /// Makes cell (<paramref name="row"/>, <paramref name="col"/>) one-way: a piece may enter it only by a step in one of
    /// <paramref name="allowedEntryDirections"/>. Directions the board's geometry does not have are ignored.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The cell is not on the board.</exception>
    public PuzzleTopologyBuilder<TBitboard> AddDiode(int row, int col, GridDirections allowedEntryDirections)
    {
        CheckCell(row, col);
        _diodes.Add(new DirectionalDiode(row * _columns + col, allowedEntryDirections));
        return this;
    }

    /// <summary>
    /// Puts an immovable partition on the edge of cell (<paramref name="row"/>, <paramref name="col"/>) in <paramref name="direction"/>:
    /// no piece crosses it either way. Declaring the same edge from either side adds it once.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The cell is not on the board, or <paramref name="direction"/> is not a direction of the geometry.</exception>
    /// <exception cref="ArgumentException">The edge lies on the border of the board.</exception>
    public PuzzleTopologyBuilder<TBitboard> AddPartition(int row, int col, GridDirection direction)
    {
        CheckCell(row, col);
        int d = (int)direction;
        if ((uint)d >= (uint)Grid.DirectionCount(_geometry))
            throw new ArgumentOutOfRangeException(nameof(direction), direction, $"A {_geometry} board has {Grid.DirectionCount(_geometry)} directions.");
        int cell = row * _columns + col;
        if (!Grid.StepStaysOnBoard(cell, d, _columns, _columns * _rows))
            throw new ArgumentException("A partition lies between two cells of the board.", nameof(direction));
        int delta = Grid.Delta(_columns, d);
        var partition = delta > 0 ? new Partition(cell, direction) : new Partition(cell + delta, (GridDirection)(d ^ 1));
        if (!_partitions.Contains(partition)) _partitions.Add(partition);
        return this;
    }

    private void CheckCell(int row, int col)
    {
        if ((uint)row >= (uint)_rows) throw new ArgumentOutOfRangeException(nameof(row), "The cell is not on the board.");
        if ((uint)col >= (uint)_columns) throw new ArgumentOutOfRangeException(nameof(col), "The cell is not on the board.");
    }

    /// <summary>Adds a plate: while a piece on an elevation in <paramref name="triggerElevationMask"/> covers a trigger cell, the gate cells open (or, if not <paramref name="normallyClosed"/>, close).</summary>
    /// <exception cref="ArgumentException">A mask has a bit set outside the board.</exception>
    public PuzzleTopologyBuilder<TBitboard> AddPressurePlate(TBitboard triggerCellsMask, TBitboard gateCellsMask, byte triggerElevationMask = 0b01,
        bool normallyClosed = true)
    {
        TBitboard board = Bit<TBitboard>.All.ShiftRight(Grid.BitWidth<TBitboard>() - _columns * _rows);
        if (!triggerCellsMask.AndNot(board).IsEmpty) throw new ArgumentException("The trigger cells are not all on the board.", nameof(triggerCellsMask));
        if (!gateCellsMask.AndNot(board).IsEmpty) throw new ArgumentException("The gate cells are not all on the board.", nameof(gateCellsMask));
        _plates.Add(new PressurePlateRule<TBitboard>(triggerCellsMask, gateCellsMask, triggerElevationMask, normallyClosed));
        return this;
    }

    /// <summary>Adds a shape class from the cells it covers (row and column offsets) and returns its group id.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="cells"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="cells"/> is empty, or the shape is wider than the board or does not fit in the bitboard.</exception>
    public int AddGroup(string name, IEnumerable<(int dRow, int dCol)> cells, AllowedDirections directions = AllowedDirections.Both,
        byte elevationMask = 0b01, SlidePhysics physics = SlidePhysics.ContinuousFloodFill, RoleFlags roles = RoleFlags.None)
    {
        ArgumentNullException.ThrowIfNull(name);
        var (baseMask, rel) = Normalize(cells, _columns);
        _groups.Add(new GroupDef(name, rel, baseMask, directions, elevationMask, physics, roles));
        return _groups.Count - 1;
    }

    /// <summary>
    /// Adds the distinct 90-degree orientations of a shape as a ring of groups that rotate into each other inside the square box
    /// around the shape's bounding box (anchored at its top-left). Returns the group ids in clockwise order, starting with the shape as given.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="cells"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="cells"/> is empty, or the rotation box does not fit on the board.</exception>
    public IReadOnlyList<int> AddRotatableGroup(string name, IEnumerable<(int dRow, int dCol)> cells, AllowedDirections directions = AllowedDirections.Both,
        byte elevationMask = 0b01, SlidePhysics physics = SlidePhysics.ContinuousFloodFill, RoleFlags roles = RoleFlags.None)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(cells);
        var start = Canonical(cells);
        int minR = start.Min(c => c.dRow), minC = start.Min(c => c.dCol);
        start = start.Select(c => (c.dRow - minR, c.dCol - minC)).ToList();
        int n = Math.Max(start.Max(c => c.dRow), start.Max(c => c.dCol)) + 1;
        if (n > _columns || n > _rows) throw new ArgumentException("The rotation box does not fit on the board.", nameof(cells));

        var orientations = new List<List<(int dRow, int dCol)>>();
        var current = start;
        while (true)
        {
            var sorted = Canonical(current);
            if (orientations.Count > 0 && orientations[0].SequenceEqual(sorted)) break;
            orientations.Add(sorted);
            current = sorted.Select(c => (c.dCol, n - 1 - c.dRow)).ToList();
        }

        int first = _groups.Count, len = orientations.Count;
        var offsets = orientations.Select(o => o[0].dRow * _columns + o[0].dCol).ToArray();
        var ids = new List<int>();
        for (int i = 0; i < len; i++)
        {
            var (baseMask, rel) = Normalize(orientations[i], _columns);
            _groups.Add(new GroupDef(name, rel, baseMask, directions, elevationMask, physics, roles)
            {
                RingStart = first,
                RingLength = len,
                RingIndex = i,
                BoxSize = n,
                RingOffsets = offsets,
            });
            ids.Add(first + i);
        }
        return ids;
    }

    /// <summary>Places a piece of group <paramref name="groupId"/> with its anchor on <paramref name="initialAnchor"/> and returns its piece id.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="groupId"/> names no added group.</exception>
    public int AddPiece(int groupId, string label, int initialAnchor)
    {
        ArgumentNullException.ThrowIfNull(label);
        if ((uint)groupId >= (uint)_groups.Count) throw new ArgumentOutOfRangeException(nameof(groupId), "No such group has been added.");
        _pieces.Add(new PieceTemplate(_pieces.Count, groupId, label, initialAnchor));
        return _pieces.Count - 1;
    }

    /// <summary>Checks the description and returns the immutable topology.</summary>
    /// <exception cref="InvalidOperationException">A piece starts on an invalid anchor or overlaps a wall or another piece.</exception>
    public PuzzleTopology<TBitboard> Build()
    {
        var groups = new List<EquivalenceGroup<TBitboard>>();
        for (int g = 0; g < _groups.Count; g++)
        {
            var d = _groups[g];
            var group = new EquivalenceGroup<TBitboard>(g, d.Name, d.Base, ComputeValidAnchors(d.Cells), d.Dirs, d.Cells)
            {
                ElevationMask = d.Elevation,
                SlidePhysics = d.Physics,
                Roles = d.Roles,
            };
            if (d.RingStart >= 0 && d.RingLength > 1)
            {
                int i = d.RingIndex, len = d.RingLength;
                int next = d.RingStart + (i + 1) % len, prev = d.RingStart + (i + len - 1) % len;
                TBitboard box = Bit<TBitboard>.Zero;
                for (int r = 0; r < d.BoxSize; r++)
                    for (int c = 0; c < d.BoxSize; c++) box = box.Or(Bit<TBitboard>.One.ShiftLeft(r * _columns + c));
                group = group with
                {
                    CanRotate = true,
                    NextCwGroupId = next,
                    NextCcwGroupId = prev,
                    BoxOriginDelta = -d.RingOffsets[i],
                    AnchorDeltaCw = d.RingOffsets[next - d.RingStart] - d.RingOffsets[i],
                    AnchorDeltaCcw = d.RingOffsets[prev - d.RingStart] - d.RingOffsets[i],
                    RotationBoxSize = d.BoxSize,
                    SweptClearanceMask = box,
                };
            }
            groups.Add(group);
        }

        var layers = new TBitboard[2];
        layers[0] = layers[1] = _walls;
        foreach (var p in _pieces)
        {
            var g = groups[p.GroupId];
            if (!g.ValidAnchorsMask.HasBit(p.InitialAnchor))
                throw new InvalidOperationException($"Piece {p.Label} has invalid initial anchor {p.InitialAnchor}.");
            TBitboard m = g.BaseMask.ShiftLeft(p.InitialAnchor);
            for (int layer = 0; layer < 2; layer++)
            {
                if ((g.ElevationMask >> layer & 1) == 0) continue;
                if (layers[layer].Intersects(m)) throw new InvalidOperationException($"Piece {p.Label} overlaps another piece or wall.");
                layers[layer] = layers[layer].Or(m);
            }
        }
        return new PuzzleTopology<TBitboard>(_columns, _rows, _geometry, _walls, groups.ToArray(), _pieces.ToArray(), _diodes.ToArray(), _plates.ToArray(), _partitions.ToArray());
    }

    private TBitboard ComputeValidAnchors(IReadOnlyList<(int dRow, int dCol)> rel)
    {
        TBitboard valid = Bit<TBitboard>.Zero;
        for (int s = 0; s < _columns * _rows; s++)
        {
            int r = s / _columns, c = s % _columns;
            bool ok = true;
            foreach (var (dr, dc) in rel)
            {
                int rr = r + dr, cc = c + dc;
                if (rr < 0 || rr >= _rows || cc < 0 || cc >= _columns || _walls.HasBit(rr * _columns + cc)) { ok = false; break; }
            }
            if (ok) valid = valid.Or(Bit<TBitboard>.One.ShiftLeft(s));
        }
        return valid;
    }

    /// <summary>The distinct cells in row-major order.</summary>
    /// <exception cref="ArgumentException"><paramref name="cells"/> is empty.</exception>
    private static List<(int dRow, int dCol)> Canonical(IEnumerable<(int dRow, int dCol)> cells)
    {
        var list = cells.Distinct().OrderBy(c => c.dRow).ThenBy(c => c.dCol).ToList();
        if (list.Count == 0) throw new ArgumentException("A piece needs at least one cell.", nameof(cells));
        return list;
    }

    /// <summary>
    /// LSB normalization: lay the cells in their bounding box, then shift right by tzcnt so the first
    /// occupied cell in row-major order sits on bit 0. Also returns cells relative to that first cell.
    /// </summary>
    public static (TBitboard BaseMask, IReadOnlyList<(int dRow, int dCol)> RelativeCells) Normalize(IEnumerable<(int dRow, int dCol)> cells, int boardColumns)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var list = Canonical(cells);
        int minR = list.Min(c => c.dRow), minC = list.Min(c => c.dCol), maxC = list.Max(c => c.dCol);
        if (maxC - minC + 1 > boardColumns) throw new ArgumentException("Piece is wider than the board.", nameof(cells));
        TBitboard raw = Bit<TBitboard>.Zero;
        foreach (var (r, c) in list)
        {
            int bit = (r - minR) * boardColumns + (c - minC);
            if (bit >= Grid.BitWidth<TBitboard>()) throw new ArgumentException("Piece does not fit in the bitboard.", nameof(cells));
            raw = raw.Or(Bit<TBitboard>.One.ShiftLeft(bit));
        }
        TBitboard baseMask = raw.ShiftRight(Grid.Lsb(raw));
        var origin = list[0];
        var rel = list.Select(c => (c.dRow - origin.dRow, c.dCol - origin.dCol)).ToArray();
        return (baseMask, rel);
    }
}
