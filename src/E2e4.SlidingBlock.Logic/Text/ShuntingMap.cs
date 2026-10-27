using System.Globalization;
using System.Numerics;

namespace E2e4.SlidingBlock.Logic.Text;

/// <summary>A railway shunting puzzle as data: the arguments of the <see cref="ShuntingDomain{TBitboard}"/> constructor.</summary>
/// <param name="Track">The track.</param>
/// <param name="Groups">The groups of pieces with their starting slots, indexed by group id.</param>
/// <param name="Goal">What counts as solved.</param>
/// <param name="StartAlignments">The starting alignment of each turntable, or null if there are none or all start at 0.</param>
public sealed record ShuntingLayout(ShuntingTrack Track, IReadOnlyList<ShuntingGroup> Groups, ShuntingGoal Goal, IReadOnlyList<int>? StartAlignments)
{
    /// <summary>The rules of this puzzle.</summary>
    /// <param name="metric">How moves are counted.</param>
    public ShuntingDomain<TBitboard> CreateDomain<TBitboard>(ShuntingMetric metric = ShuntingMetric.EngineReversals)
        where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard> =>
        new(Track, Groups, Goal, metric, StartAlignments);
}

/// <summary>
/// Reads a railway shunting puzzle from text. Each line is a keyword and its arguments; empty lines and lines starting with <c>;</c> are skipped.
/// Slots are numbered from 0 and each has ends 0 and 1; an end is written <c>slot.end</c>, a list as comma-separated numbers.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term><c>track N</c></term><description>the number of slots; must come first.</description></item>
/// <item><term><c>turntable 1,2 0,1</c></term><description>a turntable whose bridge is the listed slots and whose alignments are numbered from 0; the second list says for each alignment whether the bridge lies turned round (1) or not (0).</description></item>
/// <item><term><c>join 0.1 1.0</c></term><description>joins two slot ends; a third word <c>@T.A</c> makes the join exist only in alignment A of turntable T.</description></item>
/// <item><term><c>start E.WW..</c></term><description>one character per slot: the symbol of the piece standing there, or the empty symbol.</description></item>
/// <item><term><c>goal ....W.</c></term><description>the same, for the slots a group must end on; groups left out are free.</description></item>
/// <item><term><c>front E 0</c></term><description>the end each piece of a group faces at the start, in slot order (default 1).</description></item>
/// <item><term><c>heading W</c></term><description>makes a wagon group track which way it faces.</description></item>
/// <item><term><c>allow W 0,1,2</c></term><description>the only slots a group may enter.</description></item>
/// <item><term><c>faces 3 0</c></term><description>a goal: the piece on slot 3 faces end 0.</description></item>
/// <item><term><c>align 0,1</c></term><description>the starting alignment of each turntable.</description></item>
/// </list>
/// </remarks>
public static class ShuntingMap
{
    /// <summary>Reads the puzzle.</summary>
    /// <param name="lines">The description, one string per line.</param>
    /// <param name="symbols">One character per group of pieces; group <c>n</c> is the <c>n</c>th character.</param>
    /// <param name="engines">The characters of <paramref name="symbols"/> that are engines.</param>
    /// <param name="empty">The symbol of an empty slot in <c>start</c> and <c>goal</c>.</param>
    /// <exception cref="ArgumentException">A line is unknown or malformed, or the pieces and track do not make a valid puzzle. The message names the line.</exception>
    public static ShuntingLayout Parse(IReadOnlyList<string> lines, string symbols, string engines = "", char empty = '.')
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(engines);
        if (symbols.Length == 0 || symbols.Distinct().Count() != symbols.Length || symbols.Contains(empty))
            throw new ArgumentException("The symbols must be distinct, not empty, and not the empty symbol.", nameof(symbols));
        var reader = new Reader(symbols, engines, empty);
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i]?.Trim() ?? throw new ArgumentException($"Line {i + 1} is null.", nameof(lines));
            if (line.Length == 0 || line[0] == ';') continue;
            try
            {
                reader.Read(line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Line {i + 1}: {ex.Message}", nameof(lines), ex);
            }
        }
        try
        {
            return reader.Finish();
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(ex.Message, nameof(lines), ex);
        }
    }

    /// <summary>Reads the puzzle (see <see cref="Parse"/>) and creates its rules.</summary>
    /// <param name="lines">The description, one string per line.</param>
    /// <param name="symbols">One character per group of pieces.</param>
    /// <param name="engines">The characters of <paramref name="symbols"/> that are engines.</param>
    /// <param name="metric">How moves are counted.</param>
    public static ShuntingDomain<TBitboard> CreateDomain<TBitboard>(IReadOnlyList<string> lines, string symbols, string engines = "",
        ShuntingMetric metric = ShuntingMetric.EngineReversals)
        where TBitboard : unmanaged, IBinaryInteger<TBitboard>, IUnsignedNumber<TBitboard> =>
        Parse(lines, symbols, engines).CreateDomain<TBitboard>(metric);

    private sealed class Reader(string symbols, string engines, char empty)
    {
        private ShuntingTrackBuilder? _track;
        private int _slots;
        private string? _start, _goal;
        private readonly Dictionary<char, int[]> _fronts = [];
        private readonly Dictionary<char, int[]> _allowed = [];
        private readonly HashSet<char> _heading = [];
        private readonly List<ShuntingFrontGoal> _faces = [];
        private int[]? _align;

        public void Read(string[] words)
        {
            string key = words[0];
            if (key != "track" && _track is null) throw new ArgumentException("The first line must be 'track N'.");
            switch (key)
            {
                case "track":
                    Expect(words, 2);
                    if (_track is not null) throw new ArgumentException("'track' appears twice.");
                    _slots = Int(words[1]);
                    _track = new ShuntingTrackBuilder(_slots);
                    break;
                case "turntable":
                    Expect(words, 3);
                    _track!.AddTurntable(Ints(words[1]), [.. Ints(words[2]).Select(v => v != 0)]);
                    break;
                case "join":
                    if (words.Length is not (3 or 4)) throw new ArgumentException("Expected 'join A.e B.e [@T.A]'.");
                    var (sa, ea) = End(words[1]);
                    var (sb, eb) = End(words[2]);
                    if (words.Length == 3)
                    {
                        _track!.Connect(sa, ea, sb, eb);
                        break;
                    }
                    if (words[3][0] != '@') throw new ArgumentException("The third word must look like @turntable.alignment.");
                    var (turntable, alignment) = End(words[3][1..]);
                    _track!.ConnectWhen(turntable, alignment, sa, ea, sb, eb);
                    break;
                case "start":
                    Expect(words, 2);
                    _start = Diagram(words[1]);
                    break;
                case "goal":
                    Expect(words, 2);
                    _goal = Diagram(words[1]);
                    break;
                case "front":
                    Expect(words, 3);
                    _fronts[Symbol(words[1])] = Ints(words[2]);
                    break;
                case "heading":
                    Expect(words, 2);
                    _heading.Add(Symbol(words[1]));
                    break;
                case "allow":
                    Expect(words, 3);
                    _allowed[Symbol(words[1])] = Ints(words[2]);
                    break;
                case "faces":
                    Expect(words, 3);
                    _faces.Add(new ShuntingFrontGoal(Int(words[1]), Int(words[2])));
                    break;
                case "align":
                    Expect(words, 2);
                    _align = Ints(words[1]);
                    break;
                default:
                    throw new ArgumentException($"Unknown keyword '{key}'.");
            }
        }

        public ShuntingLayout Finish()
        {
            if (_track is null) throw new ArgumentException("The description has no 'track' line.");
            if (_start is null) throw new ArgumentException("The description has no 'start' line.");
            var groups = new List<ShuntingGroup>();
            for (int g = 0; g < symbols.Length; g++)
            {
                char symbol = symbols[g];
                int[] at = Slots(_start, symbol);
                if (at.Length == 0) throw new ArgumentException($"Group '{symbol}' has no piece in the 'start' line.");
                groups.Add(new ShuntingGroup(symbol.ToString(), engines.Contains(symbol), at)
                {
                    StartFronts = _fronts.TryGetValue(symbol, out var fronts) ? fronts : null,
                    Allowed = _allowed.TryGetValue(symbol, out var allowed) ? allowed : null,
                    TrackHeading = _heading.Contains(symbol),
                });
            }
            var goals = new List<ShuntingGroupGoal>();
            if (_goal is not null)
            {
                for (int g = 0; g < symbols.Length; g++)
                {
                    int[] at = Slots(_goal, symbols[g]);
                    if (at.Length > 0) goals.Add(new ShuntingGroupGoal(g, at));
                }
            }
            return new ShuntingLayout(_track.Build(), groups, new ShuntingGoal(goals) { Fronts = _faces }, _align);
        }

        private string Diagram(string text)
        {
            if (text.Length != _slots) throw new ArgumentException($"A diagram needs one character per slot ({_slots}), not {text.Length}.");
            foreach (char c in text)
            {
                if (c != empty && !symbols.Contains(c)) throw new ArgumentException($"Unknown symbol '{c}'.");
            }
            return text;
        }

        private char Symbol(string word)
        {
            if (word.Length != 1 || !symbols.Contains(word[0])) throw new ArgumentException($"'{word}' is not a group symbol.");
            return word[0];
        }

        private static int[] Slots(string diagram, char symbol) =>
            [.. Enumerable.Range(0, diagram.Length).Where(i => diagram[i] == symbol)];

        private static void Expect(string[] words, int count)
        {
            if (words.Length != count) throw new ArgumentException($"'{words[0]}' takes {count - 1} argument(s).");
        }

        private static int Int(string word) =>
            int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : throw new ArgumentException($"'{word}' is not a number.");

        private static int[] Ints(string word) => [.. word.Split(',').Select(Int)];

        private static (int, int) End(string word)
        {
            string[] parts = word.Split('.');
            if (parts.Length != 2) throw new ArgumentException($"'{word}' is not of the form slot.end.");
            return (Int(parts[0]), Int(parts[1]));
        }
    }
}
