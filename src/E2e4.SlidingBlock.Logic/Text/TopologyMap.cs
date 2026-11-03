namespace E2e4.SlidingBlock.Logic.Text;

/// <summary>
/// What a map symbol means. Pieces with the same normalized shape, <see cref="Directions"/>, <see cref="Key"/> and physics share
/// an equivalence group; <see cref="Key"/> is opaque to the engine and lets a puzzle keep otherwise identical shapes apart.
/// Appearance (colours, labels) is the consumer's business.
/// </summary>
/// <param name="Name">The name of the shape class; it does not take part in the group's identity.</param>
/// <param name="Directions">Which axes the piece may slide along.</param>
/// <param name="Key">Keeps otherwise identical shapes apart.</param>
/// <param name="ElevationMask">Bit 0 ground layer, bit 1 upper layer.</param>
/// <param name="SlidePhysics">How the piece travels.</param>
/// <param name="Roles">What the piece does to others when it moves.</param>
public sealed record PieceKind(string Name, AllowedDirections Directions = AllowedDirections.Both, string Key = "",
    byte ElevationMask = 0b01, SlidePhysics SlidePhysics = SlidePhysics.ContinuousFloodFill, RoleFlags Roles = RoleFlags.None);

/// <summary>Reads a board from a diagram with one string per row.</summary>
public static class TopologyMap
{
    /// <summary>
    /// Builds a topology from a diagram. <paramref name="wall"/> marks a wall, <paramref name="empty"/> an empty cell and every other
    /// character one piece. Pieces with the same normalized shape and <see cref="PieceKind"/> (apart from its name) share an equivalence group;
    /// groups are ordered by the legend order and pieces by the row-major position of their first cell.
    /// </summary>
    /// <param name="rows">The diagram, all rows the same length.</param>
    /// <param name="legend">What each piece symbol is; a symbol that is not on the diagram is skipped.</param>
    /// <param name="geometry">How the cells touch.</param>
    /// <param name="wall">The symbol of a wall.</param>
    /// <param name="empty">The symbol of an empty cell.</param>
    /// <exception cref="ArgumentException">The diagram is empty or ragged, or a symbol on it is missing from the legend.</exception>
    public static PuzzleTopology<TBitboard> Parse<TBitboard>(IReadOnlyList<string> rows, IReadOnlyList<(char Symbol, PieceKind Kind)> legend,
        GridGeometry geometry = GridGeometry.Orthogonal4Way, char wall = '#', char empty = '.')
        where TBitboard : unmanaged, IBitboard<TBitboard>
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(legend);
        if (rows.Count == 0 || rows[0].Length == 0) throw new ArgumentException("The diagram is empty.", nameof(rows));
        int height = rows.Count, width = rows[0].Length;
        if (rows.Any(r => r.Length != width)) throw new ArgumentException("The rows of the diagram differ in length.", nameof(rows));
        var builder = new PuzzleTopologyBuilder<TBitboard>(width, height, geometry);
        var cellsBySymbol = new Dictionary<char, List<(int r, int c)>>();
        var order = new List<char>();
        for (int r = 0; r < height; r++)
            for (int c = 0; c < width; c++)
            {
                char ch = rows[r][c];
                if (ch == empty) continue;
                if (ch == wall) { builder.AddWall(r, c); continue; }
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
            var legendEntry = legend.Where(l => l.Symbol == ch).Select(l => (l.Symbol, l.Kind)).ToList();
            if (legendEntry.Count == 0) throw new ArgumentException($"The diagram has a '{ch}' that is not in the legend.", nameof(rows));
            var kind = legendEntry[0].Kind;
            var (baseMask, _) = PuzzleTopologyBuilder<TBitboard>.Normalize(cells, width);
            int anchor = cells.Min(x => x.r * width + x.c);
            builder.AddPiece(groupKeys[(baseMask, KeyOf(kind))], ch.ToString(), anchor);
        }
        return builder.Build();
    }

    // The display name does not take part in the group identity.
    private static PieceKind KeyOf(PieceKind kind) => kind with { Name = "" };
}
