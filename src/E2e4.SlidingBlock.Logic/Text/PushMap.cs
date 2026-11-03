namespace E2e4.SlidingBlock.Logic.Text;

/// <summary>
/// A Soko warehouse as data: the arguments of the <see cref="PushDomain"/> constructor. Cells are numbered row by row (<c>row * Columns + column</c>).
/// </summary>
/// <param name="Columns">Columns of the warehouse.</param>
/// <param name="Rows">Rows of the warehouse.</param>
/// <param name="Floor">The cells that are floor.</param>
/// <param name="Targets">The cells that are a place for a case.</param>
/// <param name="Cases">The starting cell of each case, indexed by case id.</param>
/// <param name="Player">The cell the storeman starts on.</param>
public sealed record PushLayout(int Columns, int Rows, IReadOnlyList<int> Floor, IReadOnlyList<int> Targets, IReadOnlyList<int> Cases, int Player)
{
    /// <summary>The rules of this warehouse.</summary>
    /// <param name="pruneDeadSquares">Whether move generation leaves out pushes that strand a case (see <see cref="PushDomain.PruneDeadSquares"/>).</param>
    public PushDomain CreateDomain(bool pruneDeadSquares = true) => new(Columns, Rows, Floor, Targets, Cases, Player, pruneDeadSquares);
}

/// <summary>Reads a Soko warehouse from a diagram with one string per row.</summary>
public static class PushMap
{
    /// <summary>
    /// Reads the warehouse from a diagram: <paramref name="wall"/> is a wall, <paramref name="outside"/> is outside the warehouse,
    /// <paramref name="floor"/> is floor, <paramref name="target"/> is a place for a case, <paramref name="player"/> is the storeman and
    /// the <c>n</c>th character of <paramref name="caseSymbols"/> is case <c>n</c> (case ids run 0, 1, 2 ... without gaps). Rows may differ in length;
    /// the rest of a short row is outside.
    /// </summary>
    /// <param name="map">The diagram.</param>
    /// <param name="caseSymbols">The symbol of each case, by case id; none of them may be one of the other symbols.</param>
    /// <param name="wall">The symbol of a wall.</param>
    /// <param name="outside">The symbol of a cell outside the warehouse.</param>
    /// <param name="floor">The symbol of an empty floor cell.</param>
    /// <param name="target">The symbol of a place for a case.</param>
    /// <param name="player">The symbol of the storeman.</param>
    /// <exception cref="ArgumentException">The diagram is empty, has an unknown symbol, no storeman or no cases, skips a case, or has a different number of places and cases.</exception>
    public static PushLayout Parse(IReadOnlyList<string> map, string caseSymbols, char wall = '#', char outside = ' ', char floor = '.',
        char target = 'X', char player = '@')
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(caseSymbols);
        if (map.Count == 0) throw new ArgumentException("The map is empty.", nameof(map));
        int columns = map.Max(row => row.Length), rows = map.Count;
        if (columns == 0) throw new ArgumentException("The map is empty.", nameof(map));
        List<int> floorCells = [], targets = [];
        var cases = new SortedDictionary<int, int>();
        int storeman = -1;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < map[r].Length; c++)
            {
                char ch = map[r][c];
                int cell = r * columns + c;
                if (ch == wall || ch == outside) continue;
                floorCells.Add(cell);
                if (ch == target) targets.Add(cell);
                else if (ch == player) storeman = cell;
                else if (caseSymbols.IndexOf(ch) is int id and >= 0) cases[id] = cell;
                else if (ch != floor) throw new ArgumentException($"Unknown map symbol '{ch}'.", nameof(map));
            }
        if (storeman < 0) throw new ArgumentException("The map has no storeman.", nameof(map));
        if (cases.Count == 0) throw new ArgumentException("The map has no cases.", nameof(map));
        if (cases.Keys.Where((id, i) => id != i).Any()) throw new ArgumentException("The cases must use the first symbols of caseSymbols without gaps.", nameof(map));
        if (cases.Count != targets.Count) throw new ArgumentException("There must be as many places as cases.", nameof(map));
        return new PushLayout(columns, rows, floorCells, targets, [.. cases.Values], storeman);
    }

    /// <summary>Reads the warehouse (see <see cref="Parse"/>) and creates its rules.</summary>
    /// <param name="map">The diagram.</param>
    /// <param name="caseSymbols">The symbol of each case, by case id.</param>
    /// <param name="pruneDeadSquares">Whether move generation leaves out pushes that strand a case.</param>
    public static PushDomain CreateDomain(IReadOnlyList<string> map, string caseSymbols, bool pruneDeadSquares = true) =>
        Parse(map, caseSymbols).CreateDomain(pruneDeadSquares);
}
