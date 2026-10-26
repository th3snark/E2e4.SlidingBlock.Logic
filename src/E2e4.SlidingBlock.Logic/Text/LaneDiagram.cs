namespace E2e4.SlidingBlock.Logic.Text;

/// <summary>Reads the vehicles of a lane-sliding puzzle from a diagram with one string per row.</summary>
public static class LaneDiagram
{
    /// <summary>
    /// The symbols of a diagram, one per vehicle, ready for <see cref="Parse"/>: every character except <paramref name="empty"/>, in character
    /// order except that the ones in <paramref name="leadingLabels"/> come first, in that order (so a chosen vehicle can be vehicle 0).
    /// </summary>
    public static string SymbolsOf(IReadOnlyList<string> rows, char empty = '.', string leadingLabels = "")
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(leadingLabels);
        return string.Concat(rows.SelectMany(r => r).Where(c => c != empty).Distinct()
            .OrderBy(c => { int i = leadingLabels.IndexOf(c, StringComparison.Ordinal); return i < 0 ? int.MaxValue : i; }).ThenBy(c => c));
    }

    /// <summary>
    /// Reads the vehicles off a diagram with one string per row. Vehicle <c>i</c> is the rectangle of cells carrying <c>symbols[i]</c>; it slides
    /// along its longer side (a square one along its columns). Every other character is empty floor.
    /// </summary>
    /// <exception cref="ArgumentException">The rows differ in length, or a symbol is missing from the diagram or is not a rectangle.</exception>
    public static IReadOnlyList<LaneVehicle> Parse(IReadOnlyList<string> rows, string symbols)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(symbols);
        int rowCount = rows.Count, columns = rows.Count == 0 ? 0 : rows[0].Length;
        if (rows.Any(r => r.Length != columns)) throw new ArgumentException("The rows of the diagram differ in length.", nameof(rows));
        var vehicles = new List<LaneVehicle>();
        for (int v = 0; v < symbols.Length; v++)
        {
            var cells = Enumerable.Range(0, rowCount * columns).Where(c => rows[c / columns][c % columns] == symbols[v]).ToList();
            if (cells.Count == 0) throw new ArgumentException($"Vehicle '{symbols[v]}' is not on the diagram.", nameof(symbols));
            int minRow = cells.Min(c => c / columns), maxRow = cells.Max(c => c / columns);
            int minCol = cells.Min(c => c % columns), maxCol = cells.Max(c => c % columns);
            int width = maxCol - minCol + 1, height = maxRow - minRow + 1;
            if (width * height != cells.Count) throw new ArgumentException($"Vehicle '{symbols[v]}' is not a rectangle.", nameof(rows));
            bool horizontal = width > height;
            vehicles.Add(new LaneVehicle(v, horizontal, horizontal ? width : height, horizontal ? height : width,
                horizontal ? minRow : minCol, horizontal ? minCol : minRow));
        }
        return vehicles;
    }
}
