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

    private readonly PolyominoDomain _d = PolyominoDomain.CreateLab2();
    private readonly Dictionary<int, Canvas> _visuals = [];
    private PuzzleTopology _t = null!;
    private PolyominoState _state;
    private int _selected = -1;
    private bool _busy;
    private int _moves;
    private string _lastMove = "No move yet.";
    private Path? _ghost;
    private Polyline? _hoverPath;

    public Lab2View()
    {
        InitializeComponent();
        _t = _d.Topology;
        _state = _d.InitialState;
        GridsRow.Visibility = Visibility.Collapsed;
        foreach (var g in new[] { ShiftedGrid, ValidGrid, ReachGrid }) g.Configure(N, N, 20, false);
        BuildStaticBoard();
        BuildPieces();
        RefreshAll();
    }

    private static Point Pos(int anchor) => new(anchor % N * Cell, anchor / N * Cell);
    private static Point Center(int anchor) => new(anchor % N * Cell + Cell / 2, anchor / N * Cell + Cell / 2);
    private Color GroupColor(int g) => DrawHelpers.Hex(_t.Groups[g].ColorHex);

    // ---- static board ---------------------------------------------------------------------------------------

    private void BuildStaticBoard()
    {
        GridLayer.Children.Clear();
        var hatch = new DrawingBrush(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)), 1.5),
            new LineGeometry(new Point(0, 10), new Point(10, 0))))
        { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, 10), ViewportUnits = BrushMappingMode.Absolute };
        for (int s = 0; s < N * N; s++)
        {
            bool wall = ((_t.StaticWallsMask >> s) & 1UL) != 0;
            var tile = new Border
            {
                Width = Cell,
                Height = Cell,
                Background = wall ? hatch : new SolidColorBrush((s / N + s % N) % 2 == 0 ? Color.FromRgb(0x17, 0x20, 0x33) : Color.FromRgb(0x1A, 0x25, 0x3B)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x38, 0x50)),
                BorderThickness = new Thickness(1),
            };
            Canvas.SetLeft(tile, s % N * Cell);
            Canvas.SetTop(tile, s / N * Cell);
            GridLayer.Children.Add(tile);
            var idx = new TextBlock
            {
                Text = s.ToString(CultureInfo.InvariantCulture),
                FontSize = 10,
                FontFamily = (FontFamily)FindResource("Mono"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            };
            Canvas.SetLeft(idx, s % N * Cell + Cell - 20);
            Canvas.SetTop(idx, s / N * Cell + Cell - 17);
            GridLayer.Children.Add(idx);
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

    /// <summary>Merged outline of a polyomino with its anchor cell at the local origin (cells inset only on exposed sides).</summary>
    private static Geometry FootprintGeometry(EquivalenceGroup g, double inset = 2)
    {
        var set = g.RelativeCells.ToHashSet();
        Geometry? union = null;
        foreach (var (dr, dc) in g.RelativeCells)
        {
            double l = dc * Cell + (set.Contains((dr, dc - 1)) ? 0 : inset), t = dr * Cell + (set.Contains((dr - 1, dc)) ? 0 : inset);
            double r = (dc + 1) * Cell - (set.Contains((dr, dc + 1)) ? 0 : inset), b = (dr + 1) * Cell - (set.Contains((dr + 1, dc)) ? 0 : inset);
            Geometry rect = new RectangleGeometry(new Rect(l, t, r - l, b - t));
            union = union is null ? rect : Geometry.Combine(union, rect, GeometryCombineMode.Union, null);
        }
        union!.Freeze();
        return union;
    }

    private static PointCollection StarPoints(double cx, double cy, double outer, double inner)
    {
        var pts = new PointCollection();
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, rad = i % 2 == 0 ? outer : inner;
            pts.Add(new Point(cx + rad * Math.Cos(a), cy + rad * Math.Sin(a)));
        }
        return pts;
    }

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
            container.Children.Add(new Path { Data = inner, Stroke = new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)), StrokeThickness = 1, IsHitTestVisible = false });

            var star = new Polygon
            {
                Points = StarPoints(Cell / 2, Cell / 2, 17, 7),
                Fill = (Brush)FindResource("Gold"),
                Stroke = Brushes.Black,
                StrokeThickness = 1.2,
                ToolTip = $"LSB anchor = tzcnt(pieceMask) = cell of this star",
                IsHitTestVisible = false,
            };
            container.Children.Add(star);
            var label = new TextBlock
            {
                Text = p.Label,
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
        if (!_busy && ReferenceEquals(e.OriginalSource, Board)) Select(-1);
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
        "slide" => Color.FromRgb(0x2D, 0xD4, 0xBF),
        _ => Color.FromRgb(0xFB, 0x92, 0x3C),
    };

    private (ulong other, EquivalenceGroup g, int anchor) Context(int pieceId)
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
        ulong reach = _d.ReachableFor(_state, _selected);
        for (ulong r = reach; r != 0; r &= r - 1)
        {
            int target = BitOperations.TrailingZeroCount(r);
            var path = PolyominoState.ReconstructHookPath(a, target, g.BaseMask, g.ValidAnchorsMask, other, N, g.Directions);
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
                Fill = new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B)),
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
            Fill = new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B)),
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

    private void RebuildOverlay()
    {
        OverlayLayer.Children.Clear();
        if (OverlayToggle.IsChecked != true || _selected < 0) return;
        var g = _t.Groups[_t.Pieces[_selected].GroupId];
        for (int s = 0; s < N * N; s++)
        {
            bool ok = ((g.ValidAnchorsMask >> s) & 1UL) != 0;
            var tint = new Rectangle
            {
                Width = Cell,
                Height = Cell,
                Fill = new SolidColorBrush(ok ? Color.FromArgb(0x26, 0x34, 0xD3, 0x99) : Color.FromArgb(0x30, 0xF8, 0x71, 0x71)),
            };
            Canvas.SetLeft(tint, s % N * Cell);
            Canvas.SetTop(tint, s / N * Cell);
            OverlayLayer.Children.Add(tint);
            var badge = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
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

    private void MoveSelected(int target, IReadOnlyList<int> path)
    {
        if (_busy || _selected < 0) return;
        int piece = _selected;
        var before = _state;
        Animate([(piece, path)], () =>
        {
            var move = new PolyominoMove((byte)piece, (byte)target);
            _state = _d.ApplyMove(before, move);
            _moves++;
            int g = _t.Pieces[piece].GroupId;
            int old = before.AnchorOf(piece);
            ulong zo = _t.ZobristTable[g, old], zn = _t.ZobristTable[g, target];
            _lastMove = $"piece {_t.Pieces[piece].Label} (group {g}): anchor {old} -> {target}   [{Classify(path)}, {path.Count - 1} step(s)]\n"
                + $"path: {string.Join(" -> ", path)}\n\n"
                + $"ZobristTable[{g},{old}]  = 0x{zo:X16}\n"
                + $"ZobristTable[{g},{target}] = 0x{zn:X16}\n"
                + $"xor delta               = 0x{zo ^ zn:X16}\n"
                + $"Hash  0x{before.ZobristHash:X16}\n   ^= 0x{zo ^ zn:X16}\n   -> 0x{_state.ZobristHash:X16}\n\n"
                + $"GroupAnchors[{g}] ^= (1<<{old}) | (1<<{target})\n"
                + $"  0x{before.GroupAnchorsOf(g):X7} -> 0x{_state.GroupAnchorsOf(g):X7}";
            Banner.Visibility = Visibility.Collapsed;
            RefreshAll();
        });
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

    private void SetButtons(bool on) => SwapButton.IsEnabled = HintButton.IsEnabled = ResetButton.IsEnabled = on;

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _state = _d.InitialState;
        _moves = 0;
        _selected = -1;
        _lastMove = "State reset.";
        Banner.Visibility = Visibility.Collapsed;
        SyncPositions();
        RefreshAll();
    }

    private async void OnHint(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_d.IsGoal(_state)) { StatusText.Text = "Already solved."; return; }
        _busy = true;
        SetButtons(false);
        StatusText.Text = "Searching...";
        var start = _state;
        var result = await Task.Run(() => new PuzzleSolver().SolveBfs(_d, start));
        _busy = false;
        SetButtons(true);
        if (result is null || result.Moves.Count == 0) { StatusText.Text = "No solution found."; return; }
        var m = result.Moves[0];
        _selected = m.PieceId;
        var (other, g, a) = Context(m.PieceId);
        var path = PolyominoState.ReconstructHookPath(a, m.TargetAnchor, g.BaseMask, g.ValidAnchorsMask, other, N, g.Directions);
        MoveSelected(m.TargetAnchor, path);
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
        Animate([(pa, [a, b]), (pb, [b, a])], () =>
        {
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
            _lastMove = "Identical-piece swap: Hash ^= Z[3,a] ^ Z[3,b] ^ Z[3,b] ^ Z[3,a] = 0 (permutation invariant).";
            RefreshAll();
        });
    }

    // ---- inspector --------------------------------------------------------------------------------------------

    private void RefreshAll()
    {
        RebuildMarkers();
        RebuildOverlay();
        foreach (var kv in _visuals)
            kv.Value.Effect = kv.Key == _selected ? new DropShadowEffect { Color = Color.FromRgb(0xF5, 0xB3, 0x01), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.95 } : null;

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
            ulong shifted = g.BaseMask << a, valid = g.ValidAnchorsMask, reach = _d.ReachableFor(_state, _selected);
            int slides = 0, hooks = 0;
            for (ulong r = reach; r != 0; r &= r - 1)
            {
                var path = PolyominoState.ReconstructHookPath(a, BitOperations.TrailingZeroCount(r), g.BaseMask, valid, other, N, g.Directions);
                if (Classify(path) == "hook") hooks++; else slides++;
            }
            sb.AppendLine(CultureInfo.InvariantCulture, $"Piece                 {piece.Label}  (id {piece.PieceId})  {g.Name}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"EquivalenceGroupId    {g.GroupId}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"BaseMask[g]           0x{g.BaseMask:X7}   {g.BaseMask.ToBinary(bits)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"CurrentAnchor         tzcnt(mask) = {BitOperations.TrailingZeroCount(shifted)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"BaseMask[g] << {a,-2}      0x{shifted:X7}   {shifted.ToBinary(bits)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"ValidAnchorsMask[g]   0x{valid:X7}   {valid.ToBinary(bits)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"ReachableAnchors      0x{reach:X7}   {reach.ToBinary(bits)}");
            sb.Append(CultureInfo.InvariantCulture, $"                      {BitOperations.PopCount(reach)} destinations: {slides} slide(s), {hooks} corner hook(s)");
            PieceText.Text = sb.ToString();
            ShiftedGrid.SetMask(shifted);
            ValidGrid.SetMask(valid);
            ReachGrid.SetMask(reach, 1UL << a);
            GridsRow.Visibility = Visibility.Visible;
            int left = -g.RelativeCells.Min(c => c.dCol), right = g.RelativeCells.Max(c => c.dCol), down = g.RelativeCells.Max(c => c.dRow);
            WhyText.Text = $"Extent from the anchor: {left} cell(s) left, {right} right, {down} down. Anchors in the first {left} column(s) are invalid because the cell at " +
                $"dCol = -{left} on the row below would land at bit (anchor + {down}*W - {left}), i.e. wrap to the previous row's last column. " +
                "ValidAnchorsMask removes them with one bit-test instead of sentinel columns.";
        }

        sb.Clear();
        for (int g = 0; g < _t.Groups.Count; g++)
        {
            ulong ga = _state.GroupAnchorsOf(g);
            sb.AppendLine(CultureInfo.InvariantCulture, $"GroupAnchors[{g}] = {ga.ToBinary(bits)} = 0x{ga:X7}   {_t.Groups[g].Name}");
        }
        sb.AppendLine(CultureInfo.InvariantCulture, $"OccupancyMask   = {_state.OccupancyMask.ToBinary(bits)} = 0x{_state.OccupancyMask:X7}");
        sb.Append(CultureInfo.InvariantCulture, $"ZobristHash     = 0x{_state.ZobristHash:X16}");
        CanonText.Text = sb.ToString();
        ZobristText.Text = _lastMove;

        StatusText.Text = _d.IsGoal(_state) ? $"Solved in {_moves} move(s)!" : $"Moves: {_moves}   |   goal: master L-tromino on anchor {_d.GoalAnchor}";
    }
}
