using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

/// <summary>Immutable puzzle description: board size, walls, equivalence groups, pieces, board cells with state and Zobrist keys.</summary>
public sealed class PuzzleTopology<TBitboard> where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>
{
    internal const ulong ZobristSeed = 0x9E3779B97F4A7C15UL;

    /// <summary>Columns of <see cref="ZobristTable"/>: enough for a <see cref="UInt128"/> board.</summary>
    internal const int ZobristColumns = 128;

    internal PuzzleTopology(int width, int height, GridGeometry geometry, TBitboard walls, EquivalenceGroup<TBitboard>[] groups,
        PieceTemplate[] pieces, DirectionalDiode[] diodes, PressurePlateRule<TBitboard>[] plates, Partition[] partitions)
    {
        Width = width;
        Height = height;
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
            _deltas[d] = Grid.Delta(width, d);
            EntryTable[d] = Grid.EntryMask<TBitboard>(d, width);
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
    public int Width { get; }

    /// <summary>Rows of the board.</summary>
    public int Height { get; }

    /// <summary>How the cells touch: four neighbours or six.</summary>
    public GridGeometry Geometry { get; }

    /// <summary>Cells on the board: <see cref="Width"/> times <see cref="Height"/>.</summary>
    public int CellCount => Width * Height;

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
    public TBitboard ForbiddenEntryCells(int direction) => _forbiddenEntry[direction];

    // Indexed [group, cell]; the hot paths read it directly.
    internal ulong[,] ZobristTable { get; }

    /// <summary>The Zobrist key of a piece of group <paramref name="groupId"/> anchored on <paramref name="cell"/>; a position's hash is the XOR of its pieces' keys.</summary>
    public ulong ZobristKey(int groupId, int cell) => ZobristTable[groupId, cell];

    /// <summary>
    /// Bit s is set iff s is a valid anchor of the group and moving the group into s along <paramref name="direction"/> does not enter a diode
    /// cell from a forbidden side or cross a partition. Equal to the group's <see cref="EquivalenceGroup{TBitboard}.ValidAnchorsMask"/> on
    /// boards with neither.
    /// </summary>
    public TBitboard ValidAnchorsFor(int groupId, int direction) => _validByDir[groupId * _deltas.Length + direction];

    /// <summary>One mask per direction for <paramref name="groupId"/> (see <see cref="ValidAnchorsFor"/>).</summary>
    internal ReadOnlySpan<TBitboard> ValidByDirRow(int groupId) => _validByDir.AsSpan(groupId * _deltas.Length, _deltas.Length);

    /// <summary>Cell index change of a one-step move per direction: -1, +1, -W, +W (and -W + 1, +W - 1 on a hex board).</summary>
    public ReadOnlySpan<int> GetDirectionDeltas() => _deltas;

    /// <summary>The cells piece <paramref name="pieceId"/> covers if its <em>starting</em> shape is anchored on <paramref name="anchor"/> (a rotated piece has another shape).</summary>
    public TBitboard PieceMask(int pieceId, int anchor) => Groups[Pieces[pieceId].GroupId].BaseMask << anchor;

    /// <summary>Starts describing a board of <paramref name="width"/> x <paramref name="height"/> cells.</summary>
    public static PuzzleTopologyBuilder<TBitboard> CreateBuilder(int width, int height, GridGeometry geometry = GridGeometry.Orthogonal4Way) =>
        new(width, height, geometry);

    /// <summary>
    /// Builds a topology from an ASCII map. '#' is a wall, '.' is empty, every other character is one piece.
    /// Pieces with the same normalized shape, directions, key and physics share an equivalence group; groups are
    /// ordered by the legend order.
    /// </summary>
    public static PuzzleTopology<TBitboard> FromAsciiMap(IReadOnlyList<string> rows, IReadOnlyList<(char Symbol, PieceKind Kind)> legend,
        GridGeometry geometry = GridGeometry.Orthogonal4Way)
    {
        int height = rows.Count, width = rows[0].Length;
        var builder = new PuzzleTopologyBuilder<TBitboard>(width, height, geometry);
        var cellsBySymbol = new Dictionary<char, List<(int r, int c)>>();
        var order = new List<char>();
        for (int r = 0; r < height; r++)
            for (int c = 0; c < width; c++)
            {
                char ch = rows[r][c];
                if (ch == '.') continue;
                if (ch == '#') { builder.AddWall(r, c); continue; }
                if (!cellsBySymbol.TryGetValue(ch, out var list)) { cellsBySymbol[ch] = list = []; order.Add(ch); }
                list.Add((r, c));
            }

        var groupKeys = new Dictionary<(TBitboard, PieceKind), int>();
        // Pass 1: create groups in legend order so group ids are predictable.
        foreach (var (symbol, kind) in legend)
        {
            if (!cellsBySymbol.TryGetValue(symbol, out var cells)) continue;
            var (baseMask, _) = PuzzleTopologyBuilder<TBitboard>.Normalize(cells, width);
            var key = (baseMask, KeyOf(kind));
            if (!groupKeys.ContainsKey(key))
                groupKeys[key] = builder.AddGroup(kind.Name, cells, kind.Directions, kind.ElevationMask, kind.SlidePhysics, kind.Roles);
        }
        // Pass 2: pieces in row-major order of first appearance.
        foreach (char ch in order)
        {
            var cells = cellsBySymbol[ch];
            var kind = legend.First(l => l.Symbol == ch).Kind;
            var (baseMask, _) = PuzzleTopologyBuilder<TBitboard>.Normalize(cells, width);
            int anchor = cells.Min(x => x.r * width + x.c);
            builder.AddPiece(groupKeys[(baseMask, KeyOf(kind))], ch.ToString(), anchor);
        }
        return builder.Build();
    }

    // The display name does not take part in the group identity.
    private static PieceKind KeyOf(PieceKind kind) => kind with { Name = "" };

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
                if ((diode.AllowedEntryDirectionsMask >> d & 1) == 0) forbidden[d] |= TBitboard.One << diode.CellIndex;
        foreach (var partition in PartitionTable)
        {
            forbidden[partition.Direction] |= TBitboard.One << (partition.Cell + _deltas[partition.Direction]);
            forbidden[partition.Direction ^ 1] |= TBitboard.One << partition.Cell;
        }
        return forbidden;
    }

    /// <summary>True when a partition lies between two cells of the square box of <paramref name="size"/> cells whose top-left cell is <paramref name="origin"/>.</summary>
    internal bool HasPartitionWithin(int origin, int size)
    {
        int row0 = origin / Width, col0 = origin % Width;
        foreach (var partition in PartitionTable)
        {
            int other = partition.Cell + _deltas[partition.Direction];
            if (Inside(partition.Cell) && Inside(other)) return true;
        }
        return false;

        bool Inside(int cell) => (uint)(cell / Width - row0) < (uint)size && (uint)(cell % Width - col0) < (uint)size;
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
                if (_forbiddenEntry[d] != TBitboard.Zero)
                {
                    foreach (int n in Grid.Bits(group.ValidAnchorsMask))
                    {
                        int prev = n - _deltas[d];
                        TBitboard before = (uint)prev < (uint)cells ? group.BaseMask << prev : TBitboard.Zero;
                        TBitboard entered = (group.BaseMask << n) & ~before;
                        if ((entered & _forbiddenEntry[d]) != TBitboard.Zero) valid &= ~(TBitboard.One << n);
                    }
                }
                table[g * dirs + d] = valid;
            }
        }
        return table;
    }
}

/// <summary>Describes a board, its shape classes and its pieces, and builds the immutable <see cref="PuzzleTopology{TBitboard}"/>.</summary>
public sealed class PuzzleTopologyBuilder<TBitboard> where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard>
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

    private readonly int _width, _height;
    private readonly GridGeometry _geometry;
    private TBitboard _walls;
    private readonly List<GroupDef> _groups = [];
    private readonly List<PieceTemplate> _pieces = [];
    private readonly List<DirectionalDiode> _diodes = [];
    private readonly List<PressurePlateRule<TBitboard>> _plates = [];
    private readonly List<Partition> _partitions = [];

    /// <summary>
    /// Starts describing a board of <paramref name="width"/> x <paramref name="height"/> cells.
    /// </summary>
    /// <param name="width">Columns.</param>
    /// <param name="height">Rows.</param>
    /// <param name="geometry">How the cells touch.</param>
    public PuzzleTopologyBuilder(int width, int height, GridGeometry geometry = GridGeometry.Orthogonal4Way)
    {
        if (width <= 0 || height <= 0 || (long)width * height > Grid.BitWidth<TBitboard>())
            throw new ArgumentOutOfRangeException(nameof(width), $"Board must fit in {Grid.BitWidth<TBitboard>()} bits.");
        _width = width;
        _height = height;
        _geometry = geometry;
    }

    /// <summary>Makes cell (<paramref name="row"/>, <paramref name="col"/>) a wall.</summary>
    public PuzzleTopologyBuilder<TBitboard> AddWall(int row, int col)
    {
        CheckCell(row, col);
        _walls |= TBitboard.One << (row * _width + col);
        return this;
    }

    /// <summary>Makes cell (<paramref name="row"/>, <paramref name="col"/>) one-way: a piece may enter it only by the directions set in <paramref name="allowedEntryDirectionsMask"/> (bit d is direction d).</summary>
    public PuzzleTopologyBuilder<TBitboard> AddDiode(int row, int col, byte allowedEntryDirectionsMask)
    {
        CheckCell(row, col);
        _diodes.Add(new DirectionalDiode(row * _width + col, allowedEntryDirectionsMask));
        return this;
    }

    /// <summary>
    /// Puts an immovable partition on the edge of cell (<paramref name="row"/>, <paramref name="col"/>) in <paramref name="direction"/>
    /// (a <see cref="GridDirection"/>): no piece crosses it either way. Declaring the same edge from either side adds it once.
    /// </summary>
    public PuzzleTopologyBuilder<TBitboard> AddPartition(int row, int col, int direction)
    {
        CheckCell(row, col);
        if ((uint)direction >= (uint)Grid.DirectionCount(_geometry)) throw new ArgumentOutOfRangeException(nameof(direction));
        int cell = row * _width + col;
        if (!Grid.StepStaysOnBoard(cell, direction, _width, _width * _height))
            throw new ArgumentException("A partition lies between two cells of the board.", nameof(direction));
        int delta = Grid.Delta(_width, direction);
        var partition = delta > 0 ? new Partition(cell, direction) : new Partition(cell + delta, direction ^ 1);
        if (!_partitions.Contains(partition)) _partitions.Add(partition);
        return this;
    }

    private void CheckCell(int row, int col)
    {
        if ((uint)row >= (uint)_height) throw new ArgumentOutOfRangeException(nameof(row), "The cell is not on the board.");
        if ((uint)col >= (uint)_width) throw new ArgumentOutOfRangeException(nameof(col), "The cell is not on the board.");
    }

    /// <summary>Adds a plate: while a piece on an elevation in <paramref name="triggerElevationMask"/> covers a trigger cell, the gate cells open (or, if not <paramref name="normallyClosed"/>, close).</summary>
    public PuzzleTopologyBuilder<TBitboard> AddPressurePlate(TBitboard triggerCellsMask, TBitboard gateCellsMask, byte triggerElevationMask = 0b01,
        bool normallyClosed = true)
    {
        _plates.Add(new PressurePlateRule<TBitboard>(triggerCellsMask, gateCellsMask, triggerElevationMask, normallyClosed));
        return this;
    }

    /// <summary>Adds a shape class from the cells it covers (row and column offsets) and returns its group id.</summary>
    public int AddGroup(string name, IEnumerable<(int dRow, int dCol)> cells, AllowedDirections directions = AllowedDirections.Both,
        byte elevationMask = 0b01, SlidePhysics physics = SlidePhysics.ContinuousFloodFill, RoleFlags roles = RoleFlags.None)
    {
        var (baseMask, rel) = Normalize(cells, _width);
        _groups.Add(new GroupDef(name, rel, baseMask, directions, elevationMask, physics, roles));
        return _groups.Count - 1;
    }

    /// <summary>
    /// Adds the distinct 90-degree orientations of a shape as a ring of groups that rotate into each other inside the square box
    /// around the shape's bounding box (anchored at its top-left). Returns the group ids in clockwise order, starting with the shape as given.
    /// </summary>
    public IReadOnlyList<int> AddRotatableGroup(string name, IEnumerable<(int dRow, int dCol)> cells, AllowedDirections directions = AllowedDirections.Both,
        byte elevationMask = 0b01, SlidePhysics physics = SlidePhysics.ContinuousFloodFill, RoleFlags roles = RoleFlags.None)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var start = cells.Distinct().ToList();
        if (start.Count == 0) throw new ArgumentException("A piece needs at least one cell.", nameof(cells));
        int minR = start.Min(c => c.dRow), minC = start.Min(c => c.dCol);
        start = start.Select(c => (c.dRow - minR, c.dCol - minC)).ToList();
        int n = Math.Max(start.Max(c => c.dRow), start.Max(c => c.dCol)) + 1;
        if (n > _width || n > _height) throw new ArgumentException("The rotation box does not fit on the board.", nameof(cells));

        var orientations = new List<List<(int dRow, int dCol)>>();
        var current = start;
        while (true)
        {
            var sorted = current.OrderBy(c => c.dRow).ThenBy(c => c.dCol).ToList();
            if (orientations.Count > 0 && orientations[0].SequenceEqual(sorted)) break;
            orientations.Add(sorted);
            current = sorted.Select(c => (c.dCol, n - 1 - c.dRow)).ToList();
        }

        int first = _groups.Count, len = orientations.Count;
        var offsets = orientations.Select(o => o[0].dRow * _width + o[0].dCol).ToArray();
        var ids = new List<int>();
        for (int i = 0; i < len; i++)
        {
            var (baseMask, rel) = Normalize(orientations[i], _width);
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
    public int AddPiece(int groupId, string label, int initialAnchor)
    {
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
                TBitboard box = TBitboard.Zero;
                for (int r = 0; r < d.BoxSize; r++)
                    for (int c = 0; c < d.BoxSize; c++) box |= TBitboard.One << (r * _width + c);
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
            if (((g.ValidAnchorsMask >> p.InitialAnchor) & TBitboard.One) == TBitboard.Zero)
                throw new InvalidOperationException($"Piece {p.Label} has invalid initial anchor {p.InitialAnchor}.");
            TBitboard m = g.BaseMask << p.InitialAnchor;
            for (int layer = 0; layer < 2; layer++)
            {
                if ((g.ElevationMask >> layer & 1) == 0) continue;
                if ((layers[layer] & m) != TBitboard.Zero) throw new InvalidOperationException($"Piece {p.Label} overlaps another piece or wall.");
                layers[layer] |= m;
            }
        }
        return new PuzzleTopology<TBitboard>(_width, _height, _geometry, _walls, groups.ToArray(), _pieces.ToArray(), _diodes.ToArray(), _plates.ToArray(), _partitions.ToArray());
    }

    private TBitboard ComputeValidAnchors(IReadOnlyList<(int dRow, int dCol)> rel)
    {
        TBitboard valid = TBitboard.Zero;
        for (int s = 0; s < _width * _height; s++)
        {
            int r = s / _width, c = s % _width;
            bool ok = true;
            foreach (var (dr, dc) in rel)
            {
                int rr = r + dr, cc = c + dc;
                if (rr < 0 || rr >= _height || cc < 0 || cc >= _width || ((_walls >> (rr * _width + cc)) & TBitboard.One) != TBitboard.Zero) { ok = false; break; }
            }
            if (ok) valid |= TBitboard.One << s;
        }
        return valid;
    }

    /// <summary>
    /// LSB normalization: lay the cells in their bounding box, then shift right by tzcnt so the first
    /// occupied cell in row-major order sits on bit 0. Also returns cells relative to that first cell.
    /// </summary>
    public static (TBitboard BaseMask, IReadOnlyList<(int dRow, int dCol)> RelativeCells) Normalize(IEnumerable<(int dRow, int dCol)> cells, int boardWidth)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var list = cells.Distinct().OrderBy(c => c.dRow).ThenBy(c => c.dCol).ToList();
        if (list.Count == 0) throw new ArgumentException("A piece needs at least one cell.", nameof(cells));
        int minR = list.Min(c => c.dRow), minC = list.Min(c => c.dCol), maxC = list.Max(c => c.dCol);
        if (maxC - minC + 1 > boardWidth) throw new ArgumentException("Piece is wider than the board.", nameof(cells));
        TBitboard raw = TBitboard.Zero;
        foreach (var (r, c) in list)
        {
            int bit = (r - minR) * boardWidth + (c - minC);
            if (bit >= Grid.BitWidth<TBitboard>()) throw new ArgumentException("Piece does not fit in the bitboard.", nameof(cells));
            raw |= TBitboard.One << bit;
        }
        TBitboard baseMask = raw >> Grid.Lsb(raw);
        var origin = list[0];
        var rel = list.Select(c => (c.dRow - origin.dRow, c.dCol - origin.dCol)).ToArray();
        return (baseMask, rel);
    }
}
