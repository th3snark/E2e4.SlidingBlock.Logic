using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace E2e4.SlidingBlock.Logic.Labs.Wpf.Lab1;

public partial class Lab1View : UserControl
{
    private const double BoardPx = 460;
    private const int AnimMs = 180;

    private readonly Dictionary<string, PuzzleSession> _sessions = [];
    private readonly Dictionary<int, Border> _visuals = [];
    private readonly List<UIElement> _arrows = [];
    private PuzzleSession _session = null!;
    private double _cell;
    private bool _busy;
    private int _animating;

    public Lab1View()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (_session is null) Activate(ModeNPuzzle); };
    }

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && _session is null) return;
        Activate((RadioButton)sender);
    }

    private void Activate(RadioButton mode)
    {
        if (_session is not null) _session.Changed -= OnSessionChanged;
        string key = mode.Name;
        if (!_sessions.TryGetValue(key, out var session))
            _sessions[key] = session = key switch
            {
                nameof(ModeKlotski) => new KlotskiSession(),
                nameof(ModeRush) => new RushHourSession(),
                _ => new NPuzzleSession(),
            };
        _session = session;
        _session.Changed += OnSessionChanged;
        _cell = Math.Floor(BoardPx / Math.Max(_session.Rows, _session.Cols));
        TitleText.Text = _session.Title;
        ReprText.Text = _session.Representation;
        var spec = _session.RegisterLayout;
        Register.Configure(spec.GroupBits, spec.GroupCount, spec.GroupsPerRow, _session.GroupInfoFor);
        RegisterCaption.Text = $"{spec.Caption}; groups drawn from the most significant end";
        Occupancy.Configure(_session.Cols, _session.Rows, 24, true);
        OccupancyCaption.Text = _session.OccupancyCaption;
        RebuildBoard();
        RefreshInspector(animate: false);
    }

    private void OnSessionChanged(bool animate)
    {
        if (animate) AnimateBoard(); else RebuildBoard();
        RefreshInspector(animate);
    }

    private void RefreshInspector(bool animate)
    {
        RegisterHex.Text = _session.RegisterHex;
        Register.Configure(_session.RegisterLayout.GroupBits, _session.RegisterLayout.GroupCount, _session.RegisterLayout.GroupsPerRow, _session.GroupInfoFor);
        Register.Update(_session.Register, animate ? _session.ChangedBits : 0);
        Occupancy.SetMask(_session.Occupancy, animate ? _session.ChangedBits & 0 : 0);
        OccupancyHex.Text = _session.OccupancyHex;
        TraceText.Text = _session.Trace;
        StatusText.Text = _session.IsSolved ? $"Solved in {_session.MoveCount} moves!" : $"Moves: {_session.MoveCount}   |   legal moves: {_session.LegalMoves.Count}";
    }

    // ---- board rendering ------------------------------------------------------------------------------------

    private static (int r, int c, int h, int w) Bounds(PieceView p)
    {
        int r0 = p.Cells.Min(x => x.R), c0 = p.Cells.Min(x => x.C);
        return (r0, c0, p.Cells.Max(x => x.R) - r0 + 1, p.Cells.Max(x => x.C) - c0 + 1);
    }

    private void RebuildBoard()
    {
        Board.Children.Clear();
        _visuals.Clear();
        _arrows.Clear();
        double gridW = _session.Cols * _cell, gridH = _session.Rows * _cell;
        Board.Width = Board.Height = BoardPx;
        double ox = (BoardPx - gridW) / 2, oy = (BoardPx - gridH) / 2;
        Board.Tag = new Point(ox, oy);

        for (int r = 0; r < _session.Rows; r++)
            for (int c = 0; c < _session.Cols; c++)
            {
                var bg = new Border
                {
                    Width = _cell,
                    Height = _cell,
                    Background = new SolidColorBrush(Color.FromRgb(0x17, 0x20, 0x33)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x38, 0x50)),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = (r * _session.Cols + c).ToString(CultureInfo.InvariantCulture),
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                        Margin = new Thickness(3, 1, 0, 0),
                        FontFamily = (FontFamily)FindResource("Mono"),
                    },
                };
                Canvas.SetLeft(bg, ox + c * _cell);
                Canvas.SetTop(bg, oy + r * _cell);
                Board.Children.Add(bg);
            }

        foreach (var p in _session.Pieces)
        {
            var v = MakeVisual(p);
            _visuals[p.Id] = v;
            var (r, c, _, _) = Bounds(p);
            Canvas.SetLeft(v, ox + c * _cell);
            Canvas.SetTop(v, oy + r * _cell);
            Board.Children.Add(v);
        }
        BuildArrows();
    }

    private Border MakeVisual(PieceView p)
    {
        var (_, _, h, w) = Bounds(p);
        var color = p.Color;
        var border = new Border
        {
            Width = w * _cell - 4,
            Height = h * _cell - 4,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(7),
            Background = new LinearGradientBrush(Lighten(color, 0.12), color, 90),
            BorderBrush = new SolidColorBrush(Lighten(color, 0.35)),
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            Tag = p.Id,
            Child = new TextBlock
            {
                Text = p.Label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = _cell * (p.Label.Length > 2 ? 0.28 : 0.4),
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                IsHitTestVisible = false,
            },
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.5 },
        };
        border.MouseLeftButtonUp += OnPieceClicked;
        return border;
    }

    private static Color Lighten(Color c, double f) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

    private void BuildArrows()
    {
        foreach (var a in _arrows) Board.Children.Remove(a);
        _arrows.Clear();
        var origin = (Point)Board.Tag;
        foreach (var m in _session.LegalMoves)
        {
            var piece = _session.Pieces.First(p => p.Id == m.PieceId);
            var (r, c, h, w) = Bounds(piece);
            double cx = origin.X + (c + w / 2.0) * _cell, cy = origin.Y + (r + h / 2.0) * _cell;
            double px = cx + m.DCol * (w * _cell / 2 + _cell * 0.28), py = cy + m.DRow * (h * _cell / 2 + _cell * 0.28);
            var arrow = new Polygon
            {
                Points = ArrowPoints(m.DRow, m.DCol, Math.Min(26, _cell * 0.34)),
                Fill = new SolidColorBrush(Color.FromArgb(0xE0, 0xF5, 0xB3, 0x01)),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Cursor = Cursors.Hand,
                Tag = m.Index,
                ToolTip = $"Move piece {piece.Label}",
            };
            Canvas.SetLeft(arrow, px);
            Canvas.SetTop(arrow, py);
            int idx = m.Index;
            arrow.MouseLeftButtonUp += (_, e) => { e.Handled = true; TryApply(idx); };
            Panel.SetZIndex(arrow, 10);
            Board.Children.Add(arrow);
            _arrows.Add(arrow);
        }
    }

    private static PointCollection ArrowPoints(int dr, int dc, double s)
    {
        double h = s / 2;
        // Pointing right in local coords, then rotated to the move direction. Anchored at (0,0) = arrow centre.
        (double x, double y)[] pts = [(-h, -h), (h, 0), (-h, h)];
        var result = new PointCollection();
        foreach (var (x, y) in pts)
        {
            (double rx, double ry) = (dr, dc) switch
            {
                (0, 1) => (x, y),
                (0, -1) => (-x, -y),
                (1, 0) => (-y, x),
                _ => (y, -x),
            };
            result.Add(new Point(rx, ry));
        }
        return result;
    }

    private void OnPieceClicked(object sender, MouseButtonEventArgs e)
    {
        var border = (Border)sender;
        int id = (int)border.Tag;
        var moves = _session.LegalMoves.Where(m => m.PieceId == id).ToList();
        if (moves.Count == 0) return;
        if (moves.Count > 1)
        {
            var pos = e.GetPosition(border);
            double dx = pos.X - border.ActualWidth / 2, dy = pos.Y - border.ActualHeight / 2;
            moves = [.. moves.OrderByDescending(m => m.DCol * dx + m.DRow * dy)];
        }
        TryApply(moves[0].Index);
    }

    private void TryApply(int index)
    {
        if (_busy || _animating > 0) return;
        _session.Apply(index);
    }

    private void AnimateBoard()
    {
        var origin = (Point)Board.Tag;
        var pieces = _session.Pieces;
        if (pieces.Count != _visuals.Count || pieces.Any(p => !_visuals.ContainsKey(p.Id))) { RebuildBoard(); return; }
        foreach (var a in _arrows) Board.Children.Remove(a);
        _arrows.Clear();
        bool any = false;
        foreach (var p in pieces)
        {
            var v = _visuals[p.Id];
            var (r, c, _, _) = Bounds(p);
            double nx = origin.X + c * _cell, ny = origin.Y + r * _cell;
            if (p.Label.Length > 0 && int.TryParse(p.Label, out _))
            {
                v.Background = new LinearGradientBrush(Lighten(p.Color, 0.12), p.Color, 90);
                v.BorderBrush = new SolidColorBrush(Lighten(p.Color, 0.35));
            }
            if (Math.Abs(Canvas.GetLeft(v) - nx) < 0.5 && Math.Abs(Canvas.GetTop(v) - ny) < 0.5) continue;
            any = true;
            _animating++;
            Panel.SetZIndex(v, 5);
            var ex = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var ax = new DoubleAnimation(nx, TimeSpan.FromMilliseconds(AnimMs)) { EasingFunction = ex };
            var ay = new DoubleAnimation(ny, TimeSpan.FromMilliseconds(AnimMs)) { EasingFunction = ex };
            ay.Completed += (_, _) =>
            {
                v.BeginAnimation(Canvas.LeftProperty, null);
                v.BeginAnimation(Canvas.TopProperty, null);
                Canvas.SetLeft(v, nx);
                Canvas.SetTop(v, ny);
                Panel.SetZIndex(v, 0);
                if (--_animating == 0) BuildArrows();
            };
            v.BeginAnimation(Canvas.LeftProperty, ax);
            v.BeginAnimation(Canvas.TopProperty, ay);
        }
        if (!any) BuildArrows();
    }

    // ---- buttons ----------------------------------------------------------------------------------------------

    private void OnRandom(object sender, RoutedEventArgs e)
    {
        if (_animating == 0 && !_busy) _session.RandomMove();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _session.Reset();
    }

    private async void OnHint(object sender, RoutedEventArgs e)
    {
        if (_busy || _animating > 0) return;
        _busy = true;
        SetButtons(false);
        var session = _session;
        StatusText.Text = "Searching for the optimal next move...";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            bool ok = await session.HintStepAsync(cts.Token);
            if (!ok && ReferenceEquals(session, _session) && !session.IsSolved) StatusText.Text = "No solution found within the search limits.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Solver timed out.";
        }
        finally
        {
            _busy = false;
            SetButtons(true);
        }
    }

    private void SetButtons(bool enabled)
    {
        RandomButton.IsEnabled = HintButton.IsEnabled = ResetButton.IsEnabled = enabled;
    }
}
