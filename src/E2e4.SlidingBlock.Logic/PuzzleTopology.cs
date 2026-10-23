using System.Numerics;

namespace E2e4.SlidingBlock.Logic;

/// <summary>Immutable puzzle description: board size, walls, equivalence groups, pieces and Zobrist keys.</summary>
public sealed class PuzzleTopology
{
    public const ulong ZobristSeed = 0x9E3779B97F4A7C15UL;

    internal PuzzleTopology(int width, int height, ulong walls, IReadOnlyList<EquivalenceGroup> groups, IReadOnlyList<PieceTemplate> pieces)
    {
        Width = width;
        Height = height;
        StaticWallsMask = walls;
        Groups = groups;
        Pieces = pieces;
        ZobristTable = BuildZobrist(groups.Count);
    }

    public int Width { get; }
    public int Height { get; }
    public int CellCount => Width * Height;
    public ulong StaticWallsMask { get; }
    public IReadOnlyList<EquivalenceGroup> Groups { get; }
    public IReadOnlyList<PieceTemplate> Pieces { get; }
    public ulong[,] ZobristTable { get; }

    public ulong PieceMask(int pieceId, int anchor) => Groups[Pieces[pieceId].GroupId].BaseMask << anchor;

    public static PuzzleTopologyBuilder CreateBuilder(int width, int height) => new(width, height);

    /// <summary>
    /// Builds a topology from an ASCII map. '#' is a wall, '.' is empty, every other character is one piece.
    /// Pieces with the same normalized shape, directions and key share an equivalence group; groups are
    /// ordered by the legend order.
    /// </summary>
    public static PuzzleTopology FromAsciiMap(IReadOnlyList<string> rows, IReadOnlyList<(char Symbol, PieceKind Kind)> legend)
    {
        int height = rows.Count, width = rows[0].Length;
        var builder = new PuzzleTopologyBuilder(width, height);
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

        var groupKeys = new Dictionary<(ulong, AllowedDirections, string), int>();
        // Pass 1: create groups in legend order so group ids are predictable.
        foreach (var (symbol, kind) in legend)
        {
            if (!cellsBySymbol.TryGetValue(symbol, out var cells)) continue;
            var (baseMask, _) = PuzzleTopologyBuilder.Normalize(cells, width);
            var key = (baseMask, kind.Directions, kind.Key);
            if (!groupKeys.ContainsKey(key))
                groupKeys[key] = builder.AddGroup(kind.Name, cells, kind.Directions);
        }
        // Pass 2: pieces in row-major order of first appearance.
        foreach (char ch in order)
        {
            var cells = cellsBySymbol[ch];
            var kind = legend.First(l => l.Symbol == ch).Kind;
            var (baseMask, _) = PuzzleTopologyBuilder.Normalize(cells, width);
            int anchor = cells.Min(x => x.r * width + x.c);
            builder.AddPiece(groupKeys[(baseMask, kind.Directions, kind.Key)], ch.ToString(), anchor);
        }
        return builder.Build();
    }

    private static ulong[,] BuildZobrist(int groups)
    {
        var table = new ulong[Math.Max(groups, 1), 64];
        ulong state = ZobristSeed;
        for (int g = 0; g < table.GetLength(0); g++)
            for (int s = 0; s < 64; s++)
            {
                // XorShift64*
                state ^= state >> 12;
                state ^= state << 25;
                state ^= state >> 27;
                table[g, s] = state * 0x2545F4914F6CDD1DUL;
            }
        return table;
    }
}

public sealed class PuzzleTopologyBuilder
{
    private readonly int _width, _height;
    private ulong _walls;
    private readonly List<(string Name, IReadOnlyList<(int dRow, int dCol)> Cells, ulong Base, AllowedDirections Dirs)> _groups = [];
    private readonly List<PieceTemplate> _pieces = [];

    public PuzzleTopologyBuilder(int width, int height)
    {
        if (width <= 0 || height <= 0 || width * height > 64) throw new ArgumentOutOfRangeException(nameof(width), "Board must fit in 64 bits.");
        _width = width;
        _height = height;
    }

    public PuzzleTopologyBuilder AddWall(int row, int col)
    {
        _walls |= 1UL << (row * _width + col);
        return this;
    }

    public int AddGroup(string name, IEnumerable<(int dRow, int dCol)> cells, AllowedDirections directions = AllowedDirections.Both)
    {
        var (baseMask, rel) = Normalize(cells, _width);
        _groups.Add((name, rel, baseMask, directions));
        return _groups.Count - 1;
    }

    public int AddPiece(int groupId, string label, int initialAnchor)
    {
        _pieces.Add(new PieceTemplate(_pieces.Count, groupId, label, initialAnchor));
        return _pieces.Count - 1;
    }

    public PuzzleTopology Build()
    {
        var groups = new List<EquivalenceGroup>();
        for (int g = 0; g < _groups.Count; g++)
        {
            var (name, rel, baseMask, dirs) = _groups[g];
            groups.Add(new EquivalenceGroup(g, name, baseMask, ComputeValidAnchors(rel), dirs, rel));
        }

        ulong occupied = _walls;
        foreach (var p in _pieces)
        {
            var g = groups[p.GroupId];
            if (((g.ValidAnchorsMask >> p.InitialAnchor) & 1UL) == 0)
                throw new InvalidOperationException($"Piece {p.Label} has invalid initial anchor {p.InitialAnchor}.");
            ulong m = g.BaseMask << p.InitialAnchor;
            if ((occupied & m) != 0) throw new InvalidOperationException($"Piece {p.Label} overlaps another piece or wall.");
            occupied |= m;
        }
        return new PuzzleTopology(_width, _height, _walls, groups, _pieces.ToArray());
    }

    private ulong ComputeValidAnchors(IReadOnlyList<(int dRow, int dCol)> rel)
    {
        ulong valid = 0;
        for (int s = 0; s < _width * _height; s++)
        {
            int r = s / _width, c = s % _width;
            bool ok = true;
            foreach (var (dr, dc) in rel)
            {
                int rr = r + dr, cc = c + dc;
                if (rr < 0 || rr >= _height || cc < 0 || cc >= _width || ((_walls >> (rr * _width + cc)) & 1UL) != 0) { ok = false; break; }
            }
            if (ok) valid |= 1UL << s;
        }
        return valid;
    }

    /// <summary>
    /// LSB normalization: lay the cells in their bounding box, then shift right by tzcnt so the first
    /// occupied cell in row-major order sits on bit 0. Also returns cells relative to that first cell.
    /// </summary>
    public static (ulong BaseMask, IReadOnlyList<(int dRow, int dCol)> RelativeCells) Normalize(IEnumerable<(int dRow, int dCol)> cells, int boardWidth)
    {
        var list = cells.Distinct().OrderBy(c => c.dRow).ThenBy(c => c.dCol).ToList();
        if (list.Count == 0) throw new ArgumentException("A piece needs at least one cell.", nameof(cells));
        int minR = list.Min(c => c.dRow), minC = list.Min(c => c.dCol), maxC = list.Max(c => c.dCol);
        if (maxC - minC + 1 > boardWidth) throw new ArgumentException("Piece is wider than the board.", nameof(cells));
        ulong raw = 0;
        foreach (var (r, c) in list) raw |= 1UL << ((r - minR) * boardWidth + (c - minC));
        ulong baseMask = raw >> BitOperations.TrailingZeroCount(raw);
        var origin = list[0];
        var rel = list.Select(c => (c.dRow - origin.dRow, c.dCol - origin.dCol)).ToArray();
        return (baseMask, rel);
    }
}
