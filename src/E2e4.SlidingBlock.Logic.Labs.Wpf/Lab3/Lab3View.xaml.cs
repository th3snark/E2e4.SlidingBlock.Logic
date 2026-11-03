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

namespace E2e4.SlidingBlock.Logic.Labs.Wpf.Lab3;

public partial class Lab3View : UserControl
{
    private const double Cell = 80;
    private const int N = LabPuzzles.Lab3Width;
    private const int Bits = N * N;

    private readonly LabPuzzles.Lab3Mode[] _modes = [LabPuzzles.CreateLab3Rotation(), LabPuzzles.CreateLab3Carrier(), LabPuzzles.CreateLab3Ice()];
    private readonly PlaySession<PolyominoState<Bitboard64>, CompositeMove>[]? _sessions;
    private readonly string[] _xor = ["No action yet.", "No action yet.", "No action yet."];
    private readonly string[] _lastTable = ["", "", ""];
    private readonly Bitboard64[] _lastCluster = new Bitboard64[3];
    private readonly Dictionary<int, Canvas> _visuals = [];
    private readonly List<UIElement> _hover = [];
    private int _mode, _selected = -1;
    private bool _busy;
    private Bitboard64 _hoverCluster;

    public Lab3View()
    {
        InitializeComponent();
        _sessions = [.. _modes.Select(m => new PlaySession<PolyominoState<Bitboard64>, CompositeMove>(m.Domain, m.Domain.InitialState))];
        LoadMode(0);
    }

    private LabPuzzles.Lab3Mode M => _modes[_mode];
    private PuzzleTopology<Bitboard64> T => M.Domain.Topology;
    private PlaySession<PolyominoState<Bitboard64>, CompositeMove> S => _sessions![_mode];
    private PolyominoState<Bitboard64> State => S.State;

    private static Point Pos(int anchor) => new(anchor % N * Cell, anchor / N * Cell);
    private static Point Center(int anchor) => new(anchor % N * Cell + Cell / 2, anchor / N * Cell + Cell / 2);
    private static Point CellCenter(int bit) => Center(bit);
    private Color GroupColor(int g) => DrawHelpers.Hex(M.Colors[T.Groups[g].Name]);
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static SolidColorBrush Brush(Color c, byte alpha = 255) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    // ---- modes ------------------------------------------------------------------------------------------------

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_sessions is null || _busy) return;
        int mode = sender == Mode0 ? 0 : sender == Mode1 ? 1 : 2;
        if (mode != _mode) LoadMode(mode);
    }

    private void LoadMode(int mode)
    {
        _mode = mode;
        _selected = -1;
        _hoverCluster = 0;
        Banner.Visibility = Visibility.Collapsed;
        HoverText.Text = "";
        RotateCwButton.Visibility = RotateCcwButton.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = M.Title;
        BriefText.Text = M.Brief;
        BuildStaticBoard();
        BuildPieces();
        RefreshAll();
    }

    // ---- static board -----------------------------------------------------------------------------------------

    private void BuildStaticBoard()
    {
        GridLayer.Children.Clear();
        CoordLayer.Children.Clear();
        var hatch = new DrawingBrush(new GeometryDrawing(null, new Pen(Palette.SlateBrush, 1.5),
            new LineGeometry(new Point(0, 10), new Point(10, 0))))
        { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 10, 10), ViewportUnits = BrushMappingMode.Absolute };
        for (int s = 0; s < Bits; s++)
        {
            bool wall = ((T.StaticWallsMask >> s) & 1UL) != 0;
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

        var dash = (Brush)FindResource("Gold");
        if (M.Domain.Goal.GroupId >= 0)
            AddGoalOutline(FootprintGeometry(T.Groups[M.Domain.Goal.GroupId], 0), M.Domain.Goal.Anchor, dash);
        else if (M.Domain.Goal.Position is { } goal)
            for (int g = 0; g < T.Groups.Count; g++)
                if (T.Groups[g].Name == "Crate")
                    for (Bitboard64 m = goal.GroupAnchorsOf(g); !m.IsEmpty; m = m.ClearLowest())
                        AddGoalOutline(FootprintGeometry(T.Groups[g], 0), m.TrailingZeroCount(), dash);
    }

    private void AddGoalOutline(Geometry geometry, int anchor, Brush stroke)
    {
        GridLayer.Children.Add(new Path
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = 2.5,
            StrokeDashArray = [4, 3],
            Opacity = 0.85,
            RenderTransform = new TranslateTransform(Pos(anchor).X, Pos(anchor).Y),
        });
    }

    private static Geometry FootprintGeometry(EquivalenceGroup<Bitboard64> g, double inset = 2) => DrawHelpers.FootprintGeometry(g, Cell, inset);

    // ---- pieces -----------------------------------------------------------------------------------------------

    private void BuildPieces()
    {
        PieceLayer.Children.Clear();
        _visuals.Clear();
        foreach (var p in T.Pieces)
        {
            int gid = State.GroupOf(p.PieceId);
            var g = T.Groups[gid];
            var color = GroupColor(gid);
            var pos = Pos(State.AnchorOf(p.PieceId));
            var container = new Canvas { Tag = p.PieceId, RenderTransform = new TranslateTransform(pos.X, pos.Y) };
            container.Children.Add(new Path
            {
                Data = FootprintGeometry(g),
                Fill = new LinearGradientBrush(Color.FromArgb(0xF0, color.R, color.G, color.B), Color.FromArgb(0xC0, (byte)(color.R * .75), (byte)(color.G * .75), (byte)(color.B * .75)), 90),
                Stroke = new SolidColorBrush(Rgb((byte)(color.R * .55), (byte)(color.G * .55), (byte)(color.B * .55))),
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round,
                Cursor = Cursors.Hand,
                ToolTip = $"{g.Name} (group {gid}), roles: {g.Roles}",
            });

            var inner = new GeometryGroup();
            var set = g.RelativeCells.ToHashSet();
            foreach (var (dr, dc) in g.RelativeCells)
            {
                if (set.Contains((dr, dc + 1))) inner.Children.Add(new LineGeometry(new Point((dc + 1) * Cell, dr * Cell + 6), new Point((dc + 1) * Cell, (dr + 1) * Cell - 6)));
                if (set.Contains((dr + 1, dc))) inner.Children.Add(new LineGeometry(new Point(dc * Cell + 6, (dr + 1) * Cell), new Point((dc + 1) * Cell - 6, (dr + 1) * Cell)));
            }
            container.Children.Add(new Path { Data = inner, Stroke = Palette.Tint(Colors.White, 0x55), StrokeThickness = 1, IsHitTestVisible = false });
            container.Children.Add(new Polygon
            {
                Points = DrawHelpers.StarPoints(Cell / 2, Cell / 2, 15, 6),
                Fill = (Brush)FindResource("Gold"),
                Stroke = Brushes.Black,
                StrokeThickness = 1.2,
                ToolTip = $"LSB anchor = tzcnt(pieceMask) = {State.AnchorOf(p.PieceId)}",
                IsHitTestVisible = false,
            });
            var label = new TextBlock
            {
                Text = $"{p.Label} G{gid}",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = Brushes.White,
                IsHitTestVisible = false,
                FontFamily = (FontFamily)FindResource("Mono"),
            };
            Canvas.SetLeft(label, 6);
            Canvas.SetTop(label, 4);
            container.Children.Add(label);
            container.MouseLeftButtonDown += OnPieceClicked;
            _visuals[p.PieceId] = container;
            PieceLayer.Children.Add(container);
        }
    }

    private void OnPieceClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_busy) return;
        int id = (int)((Canvas)sender).Tag;
        _selected = _selected == id ? -1 : id;
        RefreshAll();
    }

    private void OnBoardBackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (_busy) return;
        _selected = -1;
        RefreshAll();
    }

    // ---- moves ------------------------------------------------------------------------------------------------

    private CompositeMove[] MovesFor(int pieceId)
    {
        var buffer = new CompositeMove[M.Domain.MaxMoves];
        int n = M.Domain.GenerateMoves(State, buffer);
        return [.. buffer.Take(n).Where(m => m.AsSpan()[0].PieceId == pieceId)];
    }

    private CompositeMove? RotationMove(int pieceId, bool clockwise)
    {
        var g = T.Groups[State.GroupOf(pieceId)];
        if (!g.CanRotate) return null;
        int next = clockwise ? g.NextCwGroupId : g.NextCcwGroupId;
        foreach (var m in MovesFor(pieceId))
            if (m.DirectionIndex == -1 && m.AsSpan()[0].NewGroupId == next) return m;
        return null;
    }

    private static Bitboard64 FootprintOf(PuzzleTopology<Bitboard64> t, PieceDelta d) => t.Groups[d.NewGroupId].BaseMask << d.NewAnchor;

    private IReadOnlyList<int> PathFor(PolyominoState<Bitboard64> before, PieceDelta d, int moveCount)
    {
        if (d.OldGroupId != d.NewGroupId) return [d.OldAnchor];
        if (moveCount == 1 && T.Groups[d.OldGroupId].SlidePhysics == SlidePhysics.ContinuousFloodFill)
        {
            var path = M.Goal.PathFor(before, d.PieceId, d.NewAnchor);
            if (path.Count > 1) return path;
        }
        return [d.OldAnchor, d.NewAnchor];
    }

    private async Task PlayAsync(CompositeMove move)
    {
        if (_busy) return;
        _busy = true;
        UpdateButtons();
        ClearHover();
        MarkerLayer.Children.Clear();
        HoverText.Text = "";
        var before = State;
        var deltas = move.AsSpan().ToArray();
        var jobs = deltas.Select(d => ((int)d.PieceId, Path: PathFor(before, d, deltas.Length))).ToList();
        await AnimateAsync(jobs);

        S.Apply(move);
        _xor[_mode] = Derivation(move, before.Hash, State.Hash);
        _lastTable[_mode] = DeltaTable(move);
        _lastCluster[_mode] = deltas.Aggregate(default(Bitboard64), (acc, d) => acc | FootprintOf(T, d));
        Banner.Visibility = Visibility.Collapsed;
        BuildPieces();
        _busy = false;
        RefreshAll();
    }

    private Task AnimateAsync(List<(int PieceId, IReadOnlyList<int> Path)> jobs)
    {
        var tcs = new TaskCompletionSource();
        int pending = jobs.Count(j => j.Path.Count > 1);
        if (pending == 0) return Task.CompletedTask;
        foreach (var (pieceId, path) in jobs)
        {
            if (path.Count < 2) continue;
            var visual = _visuals[pieceId];
            var tr = (TranslateTransform)visual.RenderTransform;
            int steps = path.Count - 1;
            double total = Math.Max(200, steps * (_mode == 2 ? 80 : 110));
            var kx = new DoubleAnimationUsingKeyFrames();
            var ky = new DoubleAnimationUsingKeyFrames();
            for (int i = 0; i <= steps; i++)
            {
                var time = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(total * i / steps));
                var p = Pos(path[i]);
                kx.KeyFrames.Add(new LinearDoubleKeyFrame(p.X, time));
                ky.KeyFrames.Add(new LinearDoubleKeyFrame(p.Y, time));
            }
            Panel.SetZIndex(visual, 20);
            ky.Completed += (_, _) =>
            {
                Panel.SetZIndex(visual, 0);
                if (--pending == 0) tcs.SetResult();
            };
            tr.BeginAnimation(TranslateTransform.XProperty, kx);
            tr.BeginAnimation(TranslateTransform.YProperty, ky);
        }
        return tcs.Task;
    }

    // ---- markers ----------------------------------------------------------------------------------------------

    private void RebuildMarkers()
    {
        ClearHover();
        MarkerLayer.Children.Clear();
        if (_selected < 0 || _busy) return;
        var moves = MovesFor(_selected);
        switch (_mode)
        {
            case 0: SlideMarkers(moves); break;
            case 1: ArrowMarkers(moves); break;
            default: RayMarkers(moves); break;
        }
    }

    private void SlideMarkers(CompositeMove[] moves)
    {
        var teal = Palette.Teal;
        foreach (var m in moves)
        {
            if (m.DirectionIndex == -1) continue;
            var d = m.AsSpan()[0];
            var move = m;
            AddRing(Center(d.NewAnchor), teal, $"slide to anchor {d.NewAnchor}", () => Hover(move, teal, $"Slide {d.OldAnchor} -> {d.NewAnchor}"), () => PlayAsync(move));
        }
    }

    private void ArrowMarkers(CompositeMove[] moves)
    {
        var g = T.Groups[State.GroupOf(_selected)];
        int a = State.AnchorOf(_selected);
        int minR = g.RelativeCells.Min(c => c.dRow), maxR = g.RelativeCells.Max(c => c.dRow);
        int minC = g.RelativeCells.Min(c => c.dCol), maxC = g.RelativeCells.Max(c => c.dCol);
        double top = (a / N + minR) * Cell, bottom = (a / N + maxR + 1) * Cell, left = (a % N + minC) * Cell, right = (a % N + maxC + 1) * Cell;
        double midX = (left + right) / 2, midY = (top + bottom) / 2;
        string[] glyphs = ["←", "→", "↑", "↓"];
        Point[] at = [new(left, midY), new(right, midY), new(midX, top), new(midX, bottom)];
        foreach (var m in moves)
        {
            if (m.DirectionIndex is < 0 or > 3) continue;
            bool coupled = m.Count > 1;
            var color = coupled ? Palette.Orange : Palette.Teal;
            string text = coupled ? $"Coupled {m.Count}-piece CompositeMove" : "Single-piece slide";
            var move = m;
            AddRing(at[m.DirectionIndex], color, text, () => Hover(move, color, text), () => PlayAsync(move), glyphs[m.DirectionIndex], 38);
        }
    }

    private void RayMarkers(CompositeMove[] moves)
    {
        var teal = Rgb(0x22, 0xD3, 0xEE);
        var red = Rgb(0xF8, 0x71, 0x71);
        foreach (var r in RaycastReport.For(M.Goal, State, _selected))
        {
            if (r.StepCount == 0) continue;
            MarkerLayer.Children.Add(new Line
            {
                X1 = Center(r.StartAnchor).X,
                Y1 = Center(r.StartAnchor).Y,
                X2 = Center(r.TerminalAnchor).X,
                Y2 = Center(r.TerminalAnchor).Y,
                Stroke = new SolidColorBrush(teal),
                StrokeThickness = 3,
                StrokeDashArray = [2, 2],
                Opacity = 0.7,
                IsHitTestVisible = false,
            });
            for (Bitboard64 m = r.BlockingBitsMask; !m.IsEmpty; m = m.ClearLowest())
            {
                var p = Pos(m.TrailingZeroCount());
                var box = new Rectangle { Width = Cell - 6, Height = Cell - 6, Stroke = new SolidColorBrush(red), StrokeThickness = 3, IsHitTestVisible = false };
                Canvas.SetLeft(box, p.X + 3);
                Canvas.SetTop(box, p.Y + 3);
                MarkerLayer.Children.Add(box);
            }
            var move = moves.FirstOrDefault(m => m.AsSpan()[0].NewAnchor == r.TerminalAnchor);
            if (move.Count == 0) continue;
            string text = $"{r.DirectionName}: glides {r.StepCount} to anchor {r.TerminalAnchor} (stopped by: {r.StopReason})";
            AddRing(Center(r.TerminalAnchor), teal, text, () => Hover(move, teal, text), () => PlayAsync(move));
        }
    }

    private void AddRing(Point c, Color color, string tooltip, Action onEnter, Func<Task> onClick, string glyph = "", double size = 30)
    {
        var ring = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(3),
            Background = Palette.Tint(color, 0xAA),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Child = glyph.Length == 0 ? null : new TextBlock
            {
                Text = glyph,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(ring, c.X - size / 2);
        Canvas.SetTop(ring, c.Y - size / 2);
        Panel.SetZIndex(ring, 5);
        ring.MouseEnter += (_, _) => onEnter();
        ring.MouseLeave += (_, _) => { ClearHover(); HoverText.Text = ""; };
        ring.MouseLeftButtonDown += async (_, e) => { e.Handled = true; await onClick(); };
        MarkerLayer.Children.Add(ring);
    }

    private void Hover(CompositeMove move, Color color, string text)
    {
        ClearHover();
        Bitboard64 cluster = default;
        foreach (var d in move.AsSpan())
        {
            var ghost = new Path
            {
                Data = FootprintGeometry(T.Groups[d.NewGroupId]),
                Fill = Palette.Tint(color, 0x55),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2,
                IsHitTestVisible = false,
                RenderTransform = new TranslateTransform(Pos(d.NewAnchor).X, Pos(d.NewAnchor).Y),
            };
            MarkerLayer.Children.Insert(0, ghost);
            _hover.Add(ghost);
            cluster |= FootprintOf(T, d);
        }
        _hoverCluster = cluster;
        HoverText.Text = $"{text}  -  ClusterFootprintMask {cluster.ToHex(9)}";
        RebuildOverlay();
    }

    private void ClearHover()
    {
        foreach (var e in _hover) MarkerLayer.Children.Remove(e);
        _hover.Clear();
        if (_hoverCluster != 0)
        {
            _hoverCluster = 0;
            RebuildOverlay();
        }
    }

    // ---- overlay ----------------------------------------------------------------------------------------------

    private void Tint(Bitboard64 mask, Color color, byte alpha)
    {
        for (Bitboard64 m = mask; !m.IsEmpty; m = m.ClearLowest())
        {
            int bit = m.TrailingZeroCount();
            var rect = new Rectangle { Width = Cell, Height = Cell, Fill = Brush(color, alpha), IsHitTestVisible = false };
            Canvas.SetLeft(rect, bit % N * Cell);
            Canvas.SetTop(rect, bit / N * Cell);
            OverlayLayer.Children.Add(rect);
        }
    }

    /// <summary>Swept rotation box, the cells blocking it, and why a turn is not possible, for the selected piece.</summary>
    private (Bitboard64 Swept, Bitboard64 Collision, string Why) RotationCheck(int pieceId, bool clockwise)
    {
        var g = T.Groups[State.GroupOf(pieceId)];
        int a = State.AnchorOf(pieceId);
        int next = clockwise ? g.NextCwGroupId : g.NextCcwGroupId;
        int nextAnchor = a + (clockwise ? g.AnchorDeltaCw : g.AnchorDeltaCcw);
        int box = a + g.BoxOriginDelta, size = g.RotationBoxSize;
        bool boxOnBoard = box >= 0 && box % N + size <= N && box / N + size <= N;
        Bitboard64 swept = boxOnBoard ? g.SweptClearanceMask << box : 0;
        Bitboard64 collision = ((State.OccupancyMask ^ (g.BaseMask << a)) | T.StaticWallsMask) & swept;
        string why = "";
        if ((uint)nextAnchor >= (uint)Bits || ((T.Groups[next].ValidAnchorsMask >> nextAnchor) & 1UL) == 0)
            why = $"the next orientation would have anchor {nextAnchor}, which is not a valid anchor for group {next}";
        else if (!boxOnBoard)
            why = $"the {size}x{size} rotation box at bit {box} leaves the board";
        else if (collision != 0)
            why = $"bit(s) {Indices(collision.Value)} inside the swept box {swept.ToHex(9)} are occupied (OtherOccupancy & SweptFootprint != 0)";
        return (swept, collision, why);
    }

    private void RebuildOverlay()
    {
        OverlayLayer.Children.Clear();
        bool on = OverlayToggle.IsChecked == true;
        switch (_mode)
        {
            case 0 when _selected >= 0 && T.Groups[State.GroupOf(_selected)].CanRotate:
                var cw = RotationCheck(_selected, true);
                bool blocked = RotationMove(_selected, true) is null && RotationMove(_selected, false) is null;
                if (on || blocked) Tint(cw.Swept, Palette.Gold, 0x38);
                if (blocked) Tint(cw.Collision, Rgb(0xF8, 0x71, 0x71), 0x90);
                break;
            case 1 when on:
                Tint(_hoverCluster != 0 ? _hoverCluster : _lastCluster[1], Rgb(0x3B, 0x82, 0xF6), 0x55);
                break;
            case 2 when on && _selected >= 0:
                foreach (var r in RaycastReport.For(M.Goal, State, _selected))
                {
                    Tint(r.SweptPathCellsMask, Rgb(0x22, 0xD3, 0xEE), 0x30);
                    Tint(r.BlockingBitsMask, Rgb(0xF8, 0x71, 0x71), 0x90);
                }
                break;
        }
    }

    private void OnOverlayToggled(object sender, RoutedEventArgs e)
    {
        if (_sessions is not null) RebuildOverlay();
    }

    // ---- buttons ----------------------------------------------------------------------------------------------

    private async void OnRotateCw(object sender, RoutedEventArgs e) => await RotateAsync(true);

    private async void OnRotateCcw(object sender, RoutedEventArgs e) => await RotateAsync(false);

    private async Task RotateAsync(bool clockwise)
    {
        if (_selected >= 0 && RotationMove(_selected, clockwise) is { } m) await PlayAsync(m);
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var before = State.Hash;
        if (!S.Undo()) return;
        _xor[_mode] = $"Undo: previous state restored\n  PreviousHash:  0x{before:X16}\n  RestoredHash:  0x{State.Hash:X16}";
        _lastTable[_mode] = "";
        _lastCluster[_mode] = 0;
        Banner.Visibility = Visibility.Collapsed;
        BuildPieces();
        RefreshAll();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        S.Reset();
        _selected = -1;
        _xor[_mode] = "State reset.";
        _lastTable[_mode] = "";
        _lastCluster[_mode] = 0;
        Banner.Visibility = Visibility.Collapsed;
        BuildPieces();
        RefreshAll();
    }

    private void UpdateButtons()
    {
        bool idle = !_busy;
        UndoButton.IsEnabled = idle && S.CanUndo;
        ResetButton.IsEnabled = idle;
        RotateCwButton.IsEnabled = idle && _selected >= 0 && RotationMove(_selected, true) is not null;
        RotateCcwButton.IsEnabled = idle && _selected >= 0 && RotationMove(_selected, false) is not null;
        Mode0.IsEnabled = Mode1.IsEnabled = Mode2.IsEnabled = idle;
    }

    // ---- inspector --------------------------------------------------------------------------------------------

    private static string Indices(ulong mask)
    {
        var l = new List<string>();
        for (ulong m = mask; m != 0; m &= m - 1) l.Add(BitOperations.TrailingZeroCount(m).ToString(CultureInfo.InvariantCulture));
        return l.Count == 0 ? "-" : string.Join(",", l);
    }

    private ulong RecomputedHash(in PolyominoState<Bitboard64> state) => state.ComputeZobrist(T);

    private string Derivation(CompositeMove move, ulong previous, ulong result)
    {
        var sb = new StringBuilder();
        var deltas = move.AsSpan();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{deltas.Length * 2}-term XOR ({deltas.Length} piece(s) changed)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {"PreviousHash:",-32}  0x{previous:X16}");
        foreach (var d in deltas)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"^ {$"ZobristTable[{d.OldGroupId}, {d.OldAnchor}]:",-32}  0x{T.ZobristKey(d.OldGroupId, d.OldAnchor):X16}  (remove old)");
            sb.AppendLine(CultureInfo.InvariantCulture, $"^ {$"ZobristTable[{d.NewGroupId}, {d.NewAnchor}]:",-32}  0x{T.ZobristKey(d.NewGroupId, d.NewAnchor):X16}  (add new)");
        }
        sb.AppendLine(new string('-', 64));
        sb.Append(CultureInfo.InvariantCulture, $"= {"New Hash:",-32}  0x{result:X16}");
        foreach (var d in deltas)
            sb.Append(CultureInfo.InvariantCulture, $"\nGroupAnchors[{d.OldGroupId}] ^= 1<<{d.OldAnchor};  GroupAnchors[{d.NewGroupId}] ^= 1<<{d.NewAnchor}");
        return sb.ToString();
    }

    private string DeltaTable(CompositeMove move)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PieceId  OldGroup -> NewGroup  OldAnchor -> NewAnchor");
        Bitboard64 cluster = default;
        foreach (var d in move.AsSpan())
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{d.PieceId,-8} {d.OldGroupId,-8} -> {d.NewGroupId,-8}  {d.OldAnchor,-9} -> {d.NewAnchor}");
            cluster |= FootprintOf(T, d);
        }
        sb.Append(CultureInfo.InvariantCulture, $"ClusterFootprintMask  {cluster.ToHex(9)}  {cluster.ToBinary(Bits)}");
        return sb.ToString();
    }

    private void RefreshAll()
    {
        UpdateButtons();
        RebuildMarkers();
        RebuildOverlay();
        foreach (var kv in _visuals)
            kv.Value.Effect = kv.Key == _selected ? new DropShadowEffect { Color = Palette.Gold, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.95 } : null;

        var sb = new StringBuilder();
        if (_selected < 0)
        {
            PieceText.Text = "Select a piece on the board to inspect its bitboards.";
            ExtraText.Text = "";
        }
        else
        {
            var piece = T.Pieces[_selected];
            var g = T.Groups[State.GroupOf(_selected)];
            int a = State.AnchorOf(_selected);
            Bitboard64 pieceMask = g.BaseMask << a;
            sb.AppendLine(CultureInfo.InvariantCulture, $"Piece                 {piece.Label}  (PieceId {piece.PieceId})  {g.Name}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"EquivalenceGroupId    {g.GroupId}     RoleFlags {g.Roles}     {g.SlidePhysics}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"BaseMask[g]           {g.BaseMask.ToHex(9)}   {g.BaseMask.ToBinary(Bits)}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"CurrentAnchor         tzcnt(pieceMask) = {pieceMask.Lsb}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"PieceMask             {pieceMask.ToHex(9)}   {pieceMask.ToBinary(Bits)}   = BaseMask << {a}");
            sb.Append(CultureInfo.InvariantCulture, $"ValidAnchorsMask[g]   {g.ValidAnchorsMask.ToHex(9)}   {g.ValidAnchorsMask.ToBinary(Bits)}");
            PieceText.Text = sb.ToString();
            ExtraText.Text = ExtraReadout(g, a, pieceMask);
        }
        ExtraTitle.Text = _mode switch
        {
            0 => "MODE 1: ORIENTATION RING & SWEPT CLEARANCE",
            1 => "MODE 2: LAST COMPOSITEMOVE",
            _ => "MODE 3: RAYCAST RESULTS (4 DIRECTIONS)",
        };
        if (_mode == 1 && _selected < 0) ExtraText.Text = _lastTable[1].Length > 0 ? _lastTable[1] : "No CompositeMove executed yet.";

        CanonTitle.Text = $"CANONICAL EQUIVALENCE STATE  (GroupAnchors[0..{T.Groups.Count - 1}])";
        sb.Clear();
        for (int g = 0; g < T.Groups.Count; g++)
        {
            Bitboard64 ga = State.GroupAnchorsOf(g);
            sb.AppendLine(CultureInfo.InvariantCulture, $"GroupAnchors[{g}] = {ga.ToBinary(Bits)} = {ga.ToHex(9)}  anchors {{{Indices(ga.Value)}}}  {T.Groups[g].Name}");
        }
        sb.Append(CultureInfo.InvariantCulture, $"OccupancyMask   = {State.OccupancyMask.ToBinary(Bits)} = {State.OccupancyMask.ToHex(9)}   (+ static walls {T.StaticWallsMask.ToHex(9)})");
        CanonText.Text = sb.ToString();

        ulong recomputed = RecomputedHash(State);
        bool match = recomputed == State.Hash;
        HashText.Text = $"0x{State.Hash:X16}";
        HashBadgeText.Text = match ? $"IncrementalHash == FullRecomputedHash  ✓   (0x{recomputed:X16})" : $"MISMATCH  ✗  recomputed 0x{recomputed:X16}";
        HashBadge.Background = Palette.Frozen(match ? Palette.GoodDeep : Palette.BadDeep);
        XorText.Text = _xor[_mode];

        if (_mode == 0 && _selected >= 0 && T.Groups[State.GroupOf(_selected)].CanRotate
            && RotationMove(_selected, true) is null && RotationMove(_selected, false) is null)
        {
            var cw = RotationCheck(_selected, true);
            var ccw = RotationCheck(_selected, false);
            BannerText.Text = $"Rotation blocked.\nCW: {cw.Why}.\nCCW: {ccw.Why}.";
            Banner.Visibility = Visibility.Visible;
        }
        else if (_mode == 0)
            Banner.Visibility = Visibility.Collapsed;

        StatusText.Text = S.IsSolved ? $"Solved in {S.MoveCount} move(s)!" : $"Moves: {S.MoveCount}   |   goal: {GoalText()}";
    }

    private string GoalText() => _mode switch
    {
        0 => $"L-tromino in group {M.Domain.Goal.GroupId} on anchor {M.Domain.Goal.Anchor}",
        1 => "crates on anchors 8 and 32",
        _ => $"glider on anchor {M.Domain.Goal.Anchor}",
    };

    private string ExtraReadout(EquivalenceGroup<Bitboard64> g, int anchor, Bitboard64 pieceMask)
    {
        var sb = new StringBuilder();
        switch (_mode)
        {
            case 0:
                if (!g.CanRotate) return "This piece does not rotate.";
                int index = 0;
                for (int k = g.GroupId - 1; k >= 0 && T.Groups[k].CanRotate && T.Groups[k].Name == g.Name; k--) index++;
                var cw = RotationCheck(_selected, true);
                int box = anchor + g.BoxOriginDelta;
                sb.AppendLine(CultureInfo.InvariantCulture, $"OrientationAngle      {index * 90} degrees   (ring position {index} of group {g.GroupId})");
                sb.AppendLine(CultureInfo.InvariantCulture, $"BoxOrigin             Anchor + BoxOriginDelta = {anchor} + {g.BoxOriginDelta} = {box}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"AnchorDelta           CW {g.AnchorDeltaCw:+0;-0;0} (-> group {g.NextCwGroupId})    CCW {g.AnchorDeltaCcw:+0;-0;0} (-> group {g.NextCcwGroupId})");
                sb.AppendLine(CultureInfo.InvariantCulture, $"SweptFootprint        {g.SweptClearanceMask.ToHex(9)} << {box} = {cw.Swept.ToHex(9)}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"CollisionBits         OtherOccupancy & SweptFootprint = {cw.Collision.ToHex(9)}   bits: {Indices(cw.Collision.Value)}");
                bool cwOk = RotationMove(_selected, true) is not null, ccwOk = RotationMove(_selected, false) is not null;
                sb.Append(CultureInfo.InvariantCulture, $"Rotation              CW {(cwOk ? "legal" : "blocked")}   CCW {(ccwOk ? "legal" : "blocked")}");
                break;
            case 1:
                sb.AppendLine(_lastTable[1].Length > 0 ? _lastTable[1] : "No CompositeMove executed yet.");
                sb.Append("Legal 1-step moves of this piece: ");
                sb.Append(string.Join(", ", MovesFor(_selected).Where(m => m.DirectionIndex >= 0).Select(m =>
                    $"{new[] { "Left", "Right", "Up", "Down" }[m.DirectionIndex]} ({(m.Count > 1 ? $"coupled x{m.Count}" : "single")})")));
                break;
            default:
                sb.AppendLine("Direction  Steps  Terminal  StopReason            BlockingBitsMask");
                foreach (var r in RaycastReport.For(M.Goal, State, _selected))
                    sb.AppendLine(CultureInfo.InvariantCulture, $"{r.DirectionName,-9}  {r.StepCount,-5}  {(r.StepCount == 0 ? "-" : r.TerminalAnchor.ToString(CultureInfo.InvariantCulture)),-8}  {(r.StepCount == 0 ? "cannot move (" + r.StopReason + ")" : r.StopReason),-20}  {r.BlockingBitsMask.ToHex(9)}");
                break;
        }
        return sb.ToString().TrimEnd();
    }
}
