using System.Globalization;
using System.Numerics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using E2e4.SlidingBlock.Logic;

namespace E2e4.SlidingBlock.Logic.Labs.Wpf.Lab2;

public partial class Lab2View : UserControl
{
    private const double Cell = 88;
    private const int N = 5;

    private readonly PolyominoDomain<ulong> _d = LabPuzzles.CreateLab2();
    private readonly Dictionary<int, Canvas> _visuals = [];
    private PuzzleTopology<ulong> _t = null!;
    private PolyominoState<ulong> _state;
    private int _selected = -1;
    private bool _busy;
    private int _moves;
    private string _lastMove = "No move yet.";
    private Path? _ghost;
    private Polyline? _hoverPath;
    private string _note = "";
    private readonly Stack<Entry> _history = new();

    /// <summary>One undoable action: the state before it and the piece trajectories it animated.</summary>
    private sealed record Entry(PolyominoState<ulong> Before, int MovesBefore, List<(int piece, IReadOnlyList<int> path)> Jobs);

    public Lab2View()
    {
        InitializeComponent();
        _t = _d.Topology;
        _state = _d.InitialState;
        GridsRow.Visibility = Visibility.Collapsed;
        foreach (var g in new[] { BaseGrid, ShiftedGrid, ValidGrid, OtherGrid, ReachGrid, StraightGrid, HookGrid }) g.Configure(N, N, 20, true);
        StraightGrid.Accent = Palette.Teal;
        HookGrid.Accent = Palette.Orange;
        OtherGrid.Accent = Palette.Muted;
        BuildStaticBoard();
        BuildPieces();
        RefreshAll();
    }

    private static Point Pos(int anchor) => new(anchor % N * Cell, anchor / N * Cell);
    private static Point Center(int anchor) => new(anchor % N * Cell + Cell / 2, anchor / N * Cell + Cell / 2);
    private Color GroupColor(int g) => DrawHelpers.Hex(LabPuzzles.Lab2Colors[_t.Groups[g].Name]);

    // ---- static board ---------------------------------------------------------------------------------------

    private void BuildStaticBoard()
    {
        GridLayer.Children.Clear();
        CoordLayer.Children.Clear();
        var hatch = new DrawingBrush(new GeometryDrawing(null, new Pen(Palette.SlateBrush, 1.5),
            new LineGeometry(new Point(0, 10), new Point(10, 0))))
        { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, 10), ViewportUnits = BrushMappingMode.Absolute };
        for (int s = 0; s < N * N; s++)
        {
            bool wall = ((_t.StaticWallsMask >> s) & 1UL) != 0;
            var tile = new Border
            {
                Width = Cell,
                Height = Cell,
                Background = wall ? hatch : (s / N + s % N) % 2 == 0 ? Palette.CellBrush : Palette.CellAltBrush,
                BorderBrush = Palette.EdgeBrush,
                BorderThickness = new Thickness(1),
            };
            Canvas.SetLeft(tile, s % N * Cell);
            Canvas.SetTop(tile, s / N * Cell);
            GridLayer.Children.Add(tile);
            var idx = new TextBlock
            {
                Text = wall ? $"{s} ({s / N},{s % N}) wall" : $"{s} ({s / N},{s % N})",
                FontSize = 10,
                FontFamily = (FontFamily)FindResource("Mono"),
                Foreground = Palette.Tint(Palette.Text, 0xC0),
            };
            Canvas.SetLeft(idx, s % N * Cell + 4);
            Canvas.SetTop(idx, s / N * Cell + Cell - 17);
            CoordLayer.Children.Add(idx);
        }
        // Goal outline for the master L.
        var master = _t.Groups[_d.GoalGroupId];
        var goal = new Path
        {
            Data = FootprintGeometry(master, 0),
            Stroke = (Brush)FindResource("Gold"),
            StrokeThickness = 2.5,
            StrokeDashArray = [4, 3],
            Opacity = 0.85,
        };
        goal.RenderTransform = new TranslateTransform(Pos(_d.GoalAnchor).X, Pos(_d.GoalAnchor).Y);
        GridLayer.Children.Add(goal);
    }

    private static Geometry FootprintGeometry(EquivalenceGroup<ulong> g, double inset = 2) => DrawHelpers.FootprintGeometry(g, Cell, inset);

    // ---- pieces -----------------------------------------------------------------------------------------------

    private void BuildPieces()
    {
        PieceLayer.Children.Clear();
        _visuals.Clear();
        foreach (var p in _t.Pieces)
        {
            var g = _t.Groups[p.GroupId];
            var color = GroupColor(g.GroupId);
            var container = new Canvas { Tag = p.PieceId, RenderTransform = new TranslateTransform(Pos(_state.AnchorOf(p.PieceId)).X, Pos(_state.AnchorOf(p.PieceId)).Y) };
            var fill = new Path
            {
                Data = FootprintGeometry(g),
                Fill = new LinearGradientBrush(Color.FromArgb(0xF0, color.R, color.G, color.B), Color.FromArgb(0xC0, (byte)(color.R * .75), (byte)(color.G * .75), (byte)(color.B * .75)), 90),
                Stroke = new SolidColorBrush(Color.FromRgb((byte)(color.R * .55), (byte)(color.G * .55), (byte)(color.B * .55))),
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round,
                Cursor = Cursors.Hand,
            };
            container.Children.Add(fill);

            var inner = new GeometryGroup();
            var set = g.RelativeCells.ToHashSet();
            foreach (var (dr, dc) in g.RelativeCells)
            {
                if (set.Contains((dr, dc + 1))) inner.Children.Add(new LineGeometry(new Point((dc + 1) * Cell, dr * Cell + 6), new Point((dc + 1) * Cell, (dr + 1) * Cell - 6)));
                if (set.Contains((dr + 1, dc))) inner.Children.Add(new LineGeometry(new Point(dc * Cell + 6, (dr + 1) * Cell), new Point((dc + 1) * Cell - 6, (dr + 1) * Cell)));
            }
            container.Children.Add(new Path { Data = inner, Stroke = Palette.Tint(Colors.White, 0x55), StrokeThickness = 1, IsHitTestVisible = false });

            var star = new Polygon
            {
                Points = DrawHelpers.StarPoints(Cell / 2, Cell / 2, 17, 7),
                Fill = (Brush)FindResource("Gold"),
                Stroke = Brushes.Black,
                StrokeThickness = 1.2,
                ToolTip = $"LSB anchor = tzcnt(pieceMask) = the cell under this star (group {g.GroupId}: {g.Name})",
                IsHitTestVisible = false,
            };
            container.Children.Add(star);
            var label = new TextBlock
            {
                Text = g.GroupId == 3 ? $"1x1 [{p.Label}]\nG{g.GroupId}" : $"{p.Label} (G{g.GroupId})",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = Brushes.White,
                IsHitTestVisible = false,
                FontFamily = (FontFamily)FindResource("Mono"),
            };
            Canvas.SetLeft(label, 8);
            Canvas.SetTop(label, 5);
            container.Children.Add(label);
            container.MouseLeftButtonDown += OnPieceClicked;
            _visuals[p.PieceId] = container;
            PieceLayer.Children.Add(container);
        }
    }

    private void SyncPositions()
    {
        foreach (var p in _t.Pieces)
        {
            var tr = (TranslateTransform)_visuals[p.PieceId].RenderTransform;
            var pos = Pos(_state.AnchorOf(p.PieceId));
            tr.X = pos.X;
            tr.Y = pos.Y;
        }
    }

    private void OnPieceClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_busy) return;
        Select((int)((Canvas)sender).Tag);
    }

    private void OnBoardBackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (!_busy) Select(-1);
    }

    private void Select(int pieceId)
    {
        _selected = _selected == pieceId ? -1 : pieceId;
        RefreshAll();
    }

    // ---- markers / overlay ------------------------------------------------------------------------------------

    private static string Classify(IReadOnlyList<int> path)
    {
        if (path.Count == 2) return "step";
        int d0 = path[1] - path[0];
        for (int i = 2; i < path.Count; i++) if (path[i] - path[i - 1] != d0) return "hook";
        return "slide";
    }

    private static Color KindColor(string kind) => kind switch
    {
        "step" => Color.FromRgb(0x34, 0xD3, 0x99),
        "slide" => Palette.Teal,
        _ => Color.FromRgb(0xFB, 0x92, 0x3C),
    };

    private (ulong other, EquivalenceGroup<ulong> g, int anchor) Context(int pieceId)
    {
        var g = _t.Groups[_t.Pieces[pieceId].GroupId];
        int a = _state.AnchorOf(pieceId);
        return (_state.OccupancyMask ^ (g.BaseMask << a), g, a);
    }

    private void RebuildMarkers()
    {
        MarkerLayer.Children.Clear();
        _ghost = null;
        _hoverPath = null;
        if (_selected < 0 || _busy) return;
        var (other, g, a) = Context(_selected);
        int minCol = g.RelativeCells.Min(c => c.dCol);
        if (minCol < 0)
        {
            var origin = Pos(a + minCol);
            var box = new Rectangle
            {
                Width = Cell - 8,
                Height = Cell - 8,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                StrokeDashArray = [3, 3],
                Opacity = 0.8,
                IsHitTestVisible = false,
                ToolTip = "Empty top-left corner of the bounding box (row, col - 1): tracking this corner would put the origin off the piece, or off the board.",
            };
            Canvas.SetLeft(box, origin.X + 4);
            Canvas.SetTop(box, origin.Y + 4);
            MarkerLayer.Children.Add(box);
            var cap = new TextBlock
            {
                Text = $"bbox origin\n(empty) {a / N},{a % N + minCol}",
                FontSize = 10,
                Foreground = Brushes.White,
                FontFamily = (FontFamily)FindResource("Mono"),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(cap, origin.X + 8);
            Canvas.SetTop(cap, origin.Y + 8);
            MarkerLayer.Children.Add(cap);
        }
        ulong reach = _d.ReachableFor(_state, _selected);
        for (ulong r = reach; r != 0; r &= r - 1)
        {
            int target = BitOperations.TrailingZeroCount(r);
            var path = PolyominoState<ulong>.ReconstructHookPath(a, target, g.BaseMask, g.ValidAnchorsMask, other, N, g.Directions);
            if (path.Count < 2) continue;
            string kind = Classify(path);
            var color = KindColor(kind);
            if (kind == "hook")
                MarkerLayer.Children.Add(new Polyline
                {
                    Points = [.. path.Select(Center)],
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 2,
                    StrokeDashArray = [3, 3],
                    Opacity = 0.35,
                    IsHitTestVisible = false,
                });
            var ring = new Ellipse
            {
                Width = 30,
                Height = 30,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 3,
                Fill = Palette.Tint(color, 0x55),
                Cursor = Cursors.Hand,
                ToolTip = kind switch { "step" => "1-step orthogonal slide", "slide" => $"{path.Count - 1}-step straight slide", _ => $"{path.Count - 1}-step corner hook move" },
            };
            var c = Center(target);
            Canvas.SetLeft(ring, c.X - 15);
            Canvas.SetTop(ring, c.Y - 15);
            int t = target;
            var pathCopy = path;
            ring.MouseEnter += (_, _) => ShowHover(t, pathCopy, color);
            ring.MouseLeave += (_, _) => ClearHover();
            ring.MouseLeftButtonDown += (_, e) => { e.Handled = true; MoveSelected(t, pathCopy); };
            Panel.SetZIndex(ring, 5);
            MarkerLayer.Children.Add(ring);
        }
    }

    private void ShowHover(int target, IReadOnlyList<int> path, Color color)
    {
        ClearHover();
        var g = _t.Groups[_t.Pieces[_selected].GroupId];
        var pos = Pos(target);
        _ghost = new Path
        {
            Data = FootprintGeometry(g),
            Fill = Palette.Tint(color, 0x55),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2,
            IsHitTestVisible = false,
            RenderTransform = new TranslateTransform(pos.X, pos.Y),
        };
        _hoverPath = new Polyline
        {
            Points = [.. path.Select(Center)],
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 3.5,
            StrokeDashArray = [2.5, 2],
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        MarkerLayer.Children.Insert(0, _ghost);
        MarkerLayer.Children.Insert(1, _hoverPath);
    }

    private void ClearHover()
    {
        if (_ghost is not null) MarkerLayer.Children.Remove(_ghost);
        if (_hoverPath is not null) MarkerLayer.Children.Remove(_hoverPath);
        _ghost = null;
        _hoverPath = null;
    }

    private string AnchorReason(EquivalenceGroup<ulong> g, int anchor)
    {
        int ar = anchor / N, ac = anchor % N;
        var sb = new StringBuilder();
        foreach (var (dr, dc) in g.RelativeCells)
        {
            int r = ar + dr, c = ac + dc;
            string arm = $"arm (dRow={dr:+0;-0;0}, dCol={dc:+0;-0;0}) = bit offset {N * dr + dc}";
            if (c < 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"Left Overhang: {arm} would overhang left off the board (col {ac} - {-dc} < 0) and wrap around to column {N - 1} of row {r - 1}.");
            else if (c >= N)
                sb.AppendLine(CultureInfo.InvariantCulture, $"Right Overhang: {arm} would overhang right off the board (col {ac} + {dc} >= {N}) and wrap around to column 0 of row {r + 1}.");
            else if (r >= N)
                sb.AppendLine(CultureInfo.InvariantCulture, $"Bottom Overflow: {arm} would land on row {r} >= {N}, past bit {N * N - 1}.");
            else if (((_t.StaticWallsMask >> (r * N + c)) & 1UL) != 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"Collides with Static Wall at Bit {r * N + c}: {arm}.");
        }
        return sb.Length == 0
            ? $"Valid anchor {anchor} ({ar},{ac}): BaseMask << {anchor} lies fully on the board and clears the wall - no runtime wrap check needed."
            : $"Invalid Anchor {anchor} ({ar},{ac}):\n{sb.ToString().TrimEnd()}";
    }

    private void RebuildOverlay()
    {
        OverlayLayer.Children.Clear();
        ReasonText.Text = _selected < 0 && OverlayToggle.IsChecked == true
            ? "Select a piece to overlay its ValidAnchorsMask[g]."
            : "Select a piece, switch the overlay on, then hover any square to see why its anchor is valid or masked out.";
        if (OverlayToggle.IsChecked != true || _selected < 0) return;
        var g = _t.Groups[_t.Pieces[_selected].GroupId];
        for (int s = 0; s < N * N; s++)
        {
            bool ok = ((g.ValidAnchorsMask >> s) & 1UL) != 0;
            string reason = AnchorReason(g, s);
            var tint = new Rectangle
            {
                Width = Cell,
                Height = Cell,
                Fill = new SolidColorBrush(ok ? Color.FromArgb(0x26, 0x34, 0xD3, 0x99) : Color.FromArgb(0x30, 0xF8, 0x71, 0x71)),
                ToolTip = reason,
            };
            ToolTipService.SetInitialShowDelay(tint, 150);
            tint.MouseEnter += (_, _) => ReasonText.Text = reason;
            Canvas.SetLeft(tint, s % N * Cell);
            Canvas.SetTop(tint, s / N * Cell);
            OverlayLayer.Children.Add(tint);
            var badge = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                IsHitTestVisible = false,
                Background = new SolidColorBrush(ok ? Color.FromRgb(0x05, 0x96, 0x69) : Color.FromRgb(0xB9, 0x1C, 0x1C)),
                Child = new TextBlock { Text = ok ? "✓" : "✕", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Canvas.SetLeft(badge, s % N * Cell + Cell - 28);
            Canvas.SetTop(badge, s / N * Cell + 6);
            OverlayLayer.Children.Add(badge);
        }
        Panel.SetZIndex(OverlayLayer, 1);
    }

    private void OnOverlayToggled(object sender, RoutedEventArgs e)
    {
        if (IsLoaded || OverlayLayer is not null) RebuildOverlay();
    }

    // ---- moves & animation ------------------------------------------------------------------------------------

    private IEnumerable<(int g, int anchor, bool add)> TermsFor(List<(int piece, IReadOnlyList<int> path)> jobs, bool reverse)
    {
        foreach (var (piece, path) in jobs)
        {
            int g = _t.Pieces[piece].GroupId, from = reverse ? path[^1] : path[0], to = reverse ? path[0] : path[^1];
            yield return (g, from, false);
            yield return (g, to, true);
        }
    }

    private string Derivation(string title, ulong previous, IEnumerable<(int g, int anchor, bool add)> terms, ulong result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {"PreviousHash:",-30}  0x{previous:X16}");
        foreach (var (g, anchor, add) in terms)
            sb.AppendLine(CultureInfo.InvariantCulture, $"^ {$"ZobristTable[{g}, {anchor}]:",-30}  0x{_t.ZobristKey(g, anchor):X16}  ({(add ? "Add new anchor" : "Remove old anchor")})");
        sb.AppendLine(new string('-', 62));
        sb.Append(CultureInfo.InvariantCulture, $"= {"New ZobristHash:",-30}  0x{result:X16}");
        return sb.ToString();
    }

    private ulong RecomputedHash(in PolyominoState<ulong> state) => state.ComputeZobrist(_t);

    private void MoveSelected(int target, IReadOnlyList<int> path)
    {
        if (_busy || _selected < 0) return;
        int piece = _selected;
        var before = _state;
        var jobs = new List<(int piece, IReadOnlyList<int> path)> { (piece, path) };
        Animate(jobs, () =>
        {
            _history.Push(new Entry(before, _moves, jobs));
            _state = _d.ApplyMove(before, new PolyominoMove((byte)piece, (byte)target));
            _moves++;
            _note = "";
            int g = _t.Pieces[piece].GroupId, old = before.AnchorOf(piece);
            _lastMove = Derivation($"piece {_t.Pieces[piece].Label} (group {g}): anchor {old} -> {target}   [{Classify(path)}, {path.Count - 1} step(s)]   path: {string.Join(" -> ", path)}",
                    before.ZobristHash, TermsFor(jobs, false), _state.ZobristHash)
                + $"\n\nGroupAnchors[{g}] ^= (1<<{old}) | (1<<{target})\n  0x{before.GroupAnchorsOf(g):X7} -> 0x{_state.GroupAnchorsOf(g):X7}";
            Banner.Visibility = Visibility.Collapsed;
            RefreshAll();
        });
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (_busy || _history.Count == 0) return;
        var entry = _history.Pop();
        var reversed = entry.Jobs.Select(j => (j.piece, (IReadOnlyList<int>)j.path.Reverse().ToArray())).ToList();
        var previous = _state;
        _selected = -1;
        Animate(reversed, () =>
        {
            _state = entry.Before;
            _moves = entry.MovesBefore;
            _note = "";
            _lastMove = Derivation("Undo: previous state restored", previous.ZobristHash, TermsFor(entry.Jobs, true), _state.ZobristHash);
            Banner.Visibility = Visibility.Collapsed;
            RefreshAll();
        });
    }

    private void OnRandom(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var buf = new PolyominoMove[_d.MaxMoves];
        int n = _d.GenerateMoves(_state, buf);
        if (n == 0) { StatusText.Text = "No legal move."; return; }
        var m = buf[Random.Shared.Next(n)];
        _selected = m.PieceId;
        var (other, g, a) = Context(m.PieceId);
        MoveSelected(m.TargetAnchor, PolyominoState<ulong>.ReconstructHookPath(a, m.TargetAnchor, g.BaseMask, g.ValidAnchorsMask, other, N, g.Directions));
    }

    /// <summary>Moves the 1x1 block A above the wall and clears the right-hand side, so reaching anchor 17 needs a 4-step hook around the wall.</summary>
    private void OnPreset(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        int a = _t.Pieces.First(p => p.Label == "A").PieceId, j = _t.Pieces.First(p => p.Label == "J").PieceId;
        _state = _d.InitialState.WithPieceMoved(_t, a, 7).WithPieceMoved(_t, j, 19);
        _history.Clear();
        _moves = 0;
        _selected = a;
        _note = "Hook demo: hover the orange ring on anchor 17 - block A must go round the wall at bit 12 (7 -> 8 -> 13 -> 18 -> 17).";
        _lastMove = "Preset loaded: A at anchor 7, J moved to anchor 19.";
        Banner.Visibility = Visibility.Collapsed;
        SyncPositions();
        RefreshAll();
    }

    private void Animate(List<(int piece, IReadOnlyList<int> path)> jobs, Action done)
    {
        _busy = true;
        SetButtons(false);
        RebuildMarkers();
        int pending = jobs.Count;
        foreach (var (piece, path) in jobs)
        {
            var visual = _visuals[piece];
            var tr = (TranslateTransform)visual.RenderTransform;
            int steps = path.Count - 1;
            double total = steps == 1 ? 180 : Math.Max(180, steps * 95);
            var kx = new DoubleAnimationUsingKeyFrames();
            var ky = new DoubleAnimationUsingKeyFrames();
            for (int i = 0; i <= steps; i++)
            {
                var time = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(total * i / steps));
                var p = Pos(path[i]);
                kx.KeyFrames.Add(new LinearDoubleKeyFrame(p.X, time));
                ky.KeyFrames.Add(new LinearDoubleKeyFrame(p.Y, time));
            }
            var end = Pos(path[^1]);
            Panel.SetZIndex(visual, 20);
            ky.Completed += (_, _) =>
            {
                tr.BeginAnimation(TranslateTransform.XProperty, null);
                tr.BeginAnimation(TranslateTransform.YProperty, null);
                tr.X = end.X;
                tr.Y = end.Y;
                Panel.SetZIndex(visual, 0);
                if (--pending > 0) return;
                _busy = false;
                SetButtons(true);
                done();
            };
            tr.BeginAnimation(TranslateTransform.XProperty, kx);
            tr.BeginAnimation(TranslateTransform.YProperty, ky);
        }
    }

    private void SetButtons(bool on)
    {
        SwapButton.IsEnabled = ResetButton.IsEnabled = PresetButton.IsEnabled = RandomButton.IsEnabled = on;
        UndoButton.IsEnabled = on && _history.Count > 0;
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _state = _d.InitialState;
        _history.Clear();
        _moves = 0;
        _selected = -1;
        _note = "";
        _lastMove = "State reset.";
        Banner.Visibility = Visibility.Collapsed;
        SyncPositions();
        RefreshAll();
    }

    private void OnSwap(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        int groupId = 3;
        var pieces = _t.Pieces.Where(p => p.GroupId == groupId).Select(p => p.PieceId).ToArray();
        int pa = pieces[0], pb = pieces[1];
        var before = _state;
        int a = before.AnchorOf(pa), b = before.AnchorOf(pb);
        _selected = -1;
        var jobs = new List<(int piece, IReadOnlyList<int> path)> { (pa, [a, b]), (pb, [b, a]) };
        Animate(jobs, () =>
        {
            _history.Push(new Entry(before, _moves, jobs));
            _note = "";
            // Route through a scratch square: every update is XOR algebra, so overlap is irrelevant.
            _state = before.WithPieceMoved(_t, pa, 24).WithPieceMoved(_t, pb, a).WithPieceMoved(_t, pa, b);
            bool sameGa = before.GroupAnchorsOf(groupId) == _state.GroupAnchorsOf(groupId);
            bool sameHash = before.ZobristHash == _state.ZobristHash;
            bool equal = before.Equals(_state);
            string ok(bool v) => v ? "IDENTICAL  ✓" : "DIFFERENT  ✗";
            BannerText.Text =
                $"Swapped block A ({a} -> {b}) and block B ({b} -> {a}).\n" +
                $"GroupAnchors[3]: 0x{before.GroupAnchorsOf(groupId):X7} -> 0x{_state.GroupAnchorsOf(groupId):X7}   {ok(sameGa)}\n" +
                $"ZobristHash:     0x{before.ZobristHash:X16} -> 0x{_state.ZobristHash:X16}   {ok(sameHash)}\n" +
                $"state.Equals / GetHashCode: {equal} / {before.GetHashCode() == _state.GetHashCode()}   (PieceAnchors differ: A={_state.AnchorOf(pa)}, B={_state.AnchorOf(pb)})";
            Banner.Background = new SolidColorBrush(sameGa && sameHash && equal ? Color.FromRgb(0x06, 0x4E, 0x3B) : Color.FromRgb(0x7F, 0x1D, 0x1D));
            Banner.Visibility = Visibility.Visible;
            _lastMove = Derivation("Identical-piece swap: A leaves " + a + " for " + b + ", B leaves " + b + " for " + a + ". Every term appears twice, so the hash cancels back to itself.",
                before.ZobristHash, TermsFor(jobs, false), _state.ZobristHash);
            RefreshAll();
        });
    }

    // ---- inspector --------------------------------------------------------------------------------------------

    private static string Indices(ulong mask)
    {
        var l = new List<string>();
        for (ulong m = mask; m != 0; m &= m - 1) l.Add(BitOperations.TrailingZeroCount(m).ToString(CultureInfo.InvariantCulture));
        return l.Count == 0 ? "-" : string.Join(",", l);
    }

    private void RefreshAll()
    {
        RebuildMarkers();
        RebuildOverlay();
        foreach (var kv in _visuals)
            kv.Value.Effect = kv.Key == _selected ? new DropShadowEffect { Color = Color.FromRgb(0xF5, 0xB3, 0x01), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.95 } : null;
        UndoButton.IsEnabled = !_busy && _history.Count > 0;

        const int bits = 25;
        var sb = new StringBuilder();
        if (_selected < 0)
        {
            PieceText.Text = "Select a piece on the board to inspect its bitboards.";
            GridsRow.Visibility = Visibility.Collapsed;
            WhyText.Text = "";
        }
        else
        {
            var (other, g, a) = Context(_selected);
            var piece = _t.Pieces[_selected];
            ulong shifted = g.BaseMask << a, valid = g.ValidAnchorsMask;
            var parent = new sbyte[N * N];
            ulong reach = PolyominoState<ulong>.FindReachableAnchors(a, g.BaseMask, valid, other, N, parent, out ulong straight, out ulong hook, g.Directions);
            string offsets = string.Join("  ", g.RelativeCells.Select(c => $"({c.dRow},{c.dCol})->{N * c.dRow + c.dCol}"));
            sb.AppendLine(CultureInfo.InvariantCulture, $"Piece                 {piece.Label}  (PieceId {piece.PieceId})  {g.Name}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"EquivalenceGroupId    {g.GroupId}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"(dRow,dCol)->{N}*dRow+dCol  {offsets}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"BaseMask[g]           0x{g.BaseMask:X7}   {g.BaseMask.ToBinary(bits)}   (bit 0 is always set)");
            sb.AppendLine(CultureInfo.InvariantCulture, $"CurrentAnchor         tzcnt(pieceMask) = {BitOperations.TrailingZeroCount(shifted)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"PieceMask             0x{shifted:X7}   {shifted.ToBinary(bits)}   = BaseMask << {a}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"ValidAnchorsMask[g]   0x{valid:X7}   {valid.ToBinary(bits)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"OtherOccupancy        0x{other:X7}   {other.ToBinary(bits)}   = BoardOccupancy ^ PieceMask");
            sb.AppendLine(CultureInfo.InvariantCulture, $"ReachableAnchors      0x{reach:X7}   {reach.ToBinary(bits)}   {BitOperations.PopCount(reach)} destination(s)");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  StraightMovesMask   0x{straight:X7}   {BitOperations.PopCount(straight)} anchor(s): {Indices(straight)}");
            sb.Append(CultureInfo.InvariantCulture, $"  HookMovesMask       0x{hook:X7}   {BitOperations.PopCount(hook)} anchor(s): {Indices(hook)}");
            PieceText.Text = sb.ToString();
            BaseGrid.SetMask(g.BaseMask);
            ShiftedGrid.SetMask(shifted);
            ValidGrid.SetMask(valid);
            OtherGrid.SetMask(other);
            ReachGrid.SetMask(reach, 1UL << a);
            StraightGrid.SetMask(straight);
            HookGrid.SetMask(hook);
            GridsRow.Visibility = Visibility.Visible;
            int left = -g.RelativeCells.Min(c => c.dCol), right = g.RelativeCells.Max(c => c.dCol), down = g.RelativeCells.Max(c => c.dRow);
            WhyText.Text = $"Extent from the anchor: {left} cell(s) left, {right} right, {down} down. Anchors in the first {left} column(s) are invalid because the cell at " +
                $"dCol = -{left} on the row below would land at bit (anchor + {down}*W - {left}), i.e. wrap to the previous row's last column. " +
                "ValidAnchorsMask removes them with one bit-test instead of sentinel columns."
                + (left > 0 ? $" The dashed box on the board marks the empty bounding-box origin, {left} column(s) left of the real anchor." : "");
        }

        sb.Clear();
        for (int g = 0; g < _t.Groups.Count; g++)
        {
            ulong ga = _state.GroupAnchorsOf(g);
            sb.AppendLine(CultureInfo.InvariantCulture, $"Group {g}: {_t.Groups[g].Name}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  GroupAnchors[{g}] = {ga.ToBinary(bits)} = 0x{ga:X7}   PopCount {BitOperations.PopCount(ga)}   anchors {{{Indices(ga)}}}");
        }
        sb.AppendLine(CultureInfo.InvariantCulture, $"OccupancyMask   = {_state.OccupancyMask.ToBinary(bits)} = 0x{_state.OccupancyMask:X7}   (+ static wall 0x{_t.StaticWallsMask:X7})");
        CanonText.Text = sb.ToString().TrimEnd();

        ulong recomputed = RecomputedHash(_state);
        bool match = recomputed == _state.ZobristHash;
        HashText.Text = $"0x{_state.ZobristHash:X16}";
        HashBadgeText.Text = match
            ? $"IncrementalHash == RecomputedFromScratchHash  ✓   (0x{recomputed:X16})"
            : $"MISMATCH  ✗  recomputed 0x{recomputed:X16}";
        HashBadge.Background = new SolidColorBrush(match ? Color.FromRgb(0x06, 0x4E, 0x3B) : Color.FromRgb(0x7F, 0x1D, 0x1D));
        ZobristText.Text = _lastMove;

        StatusText.Text = (_d.IsGoal(_state) ? $"Solved in {_moves} move(s)!" : $"Moves: {_moves}   |   goal: master L-tromino on anchor {_d.GoalAnchor}")
            + (_note.Length > 0 ? "\n" + _note : "");
    }
}
