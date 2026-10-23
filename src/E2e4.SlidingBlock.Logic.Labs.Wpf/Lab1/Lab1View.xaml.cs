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
    private ulong _hoverMask, _prevSecondary;
    private int _focusPiece = -1;
    private readonly List<UIElement> _hoverOverlay = [];

    public Lab1View()
    {
        InitializeComponent();
        Register.GroupHovered += g => ApplyHover(g >= 0 && _session is not null ? _session.CellsOfGroup(g) : 0);
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
        _focusPiece = -1;
        _prevSecondary = session.SecondaryRegister;
        _session.Changed += OnSessionChanged;
        _cell = Math.Floor(BoardPx / Math.Max(_session.Rows, _session.Cols));
        TitleText.Text = _session.Title;
        ReprText.Text = _session.Representation;
        var spec = _session.RegisterLayout;
        Register.Configure(spec.GroupBits, spec.GroupCount, spec.GroupsPerRow, _session.GroupInfoFor);
        RegisterCaption.Text = $"{spec.Caption}; groups drawn from the most significant end";
        Occupancy.Configure(_session.Cols, _session.Rows, 24, true);
        PieceGrid.Configure(_session.Cols, _session.Rows, 16);
        OtherGrid.Configure(_session.Cols, _session.Rows, 16);
        OtherGrid.Accent = Color.FromRgb(0x94, 0xA3, 0xB8);
        OccupancyCaption.Text = _session.OccupancyCaption;
        RebuildBoard();
        RefreshInspector(animate: false);
    }

    private void OnSessionChanged(bool animate)
    {
        if (animate) AnimateBoard(); else RebuildBoard();
        RefreshInspector(animate);
    }

    private static string Bin64(ulong v)
    {
        string b = Convert.ToString((long)v, 2).PadLeft(64, '0');
        return "0b" + string.Join('_', Enumerable.Range(0, 8).Select(i => b.Substring(i * 8, 8)));
    }

    private string DeltaDescription()
    {
        var spec = _session.RegisterLayout;
        ulong mask = (1UL << spec.GroupBits) - 1, prev = _session.PreviousRegister, cur = _session.Register;
        var sb = new System.Text.StringBuilder();
        for (int g = spec.GroupCount - 1; g >= 0; g--)
        {
            ulong b = (prev >> (g * spec.GroupBits)) & mask, a = (cur >> (g * spec.GroupBits)) & mask;
            if (a == b) continue;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{_session.GroupInfoFor(g).Label,-10} {Convert.ToString((long)b, 2).PadLeft(spec.GroupBits, '0')} -> {Convert.ToString((long)a, 2).PadLeft(spec.GroupBits, '0')}   ({b} -> {a})"));
        }
        return sb.Length == 0 ? "No mutation yet: make a move to see which bit groups change." : sb.ToString().TrimEnd();
    }

    private void RefreshInspector(bool animate)
    {
        RegisterHex.Text = _session.RegisterHex;
        RegisterBin.Text = Bin64(_session.Register);
        Register.Configure(_session.RegisterLayout.GroupBits, _session.RegisterLayout.GroupCount, _session.RegisterLayout.GroupsPerRow, _session.GroupInfoFor);
        Register.Update(_session.Register, animate ? _session.ChangedBits : 0);
        DeltaText.Text = animate ? DeltaDescription() : "No mutation yet: make a move to see which bit groups change.";

        string? secondary = _session.SecondaryCaption;
        InversePanel.Visibility = secondary is null ? Visibility.Collapsed : Visibility.Visible;
        if (secondary is not null)
        {
            var layout = _session.SecondaryLayout;
            ulong inv = _session.SecondaryRegister;
            InverseHex.Text = $"0x{inv:X16}";
            InverseCaption.Text = secondary;
            InverseRegister.Configure(layout.GroupBits, layout.GroupCount, layout.GroupsPerRow, _session.SecondaryGroupInfoFor);
            InverseRegister.Update(inv, animate ? inv ^ _prevSecondary : 0);
            _prevSecondary = inv;
        }

        OccupancyHex.Text = _session.OccupancyHex;
        TraceText.Text = _session.Trace;
        RegisterCaption.Text = $"{_session.RegisterLayout.Caption}; groups drawn from the most significant end; 64 bits = 8 bytes (ulong)";
        UndoButton.IsEnabled = _session.CanUndo && !_busy && _animating == 0;
        ApplyHover(0);
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
                };
                ulong bit = 1UL << (r * _session.Cols + c);
                bg.MouseEnter += (_, _) => ApplyHover(bit);
                bg.MouseLeave += (_, _) => ApplyHover(0);
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

        // Bit index and (row, col) of every cell, drawn above the pieces so they stay readable.
        for (int r = 0; r < _session.Rows; r++)
            for (int c = 0; c < _session.Cols; c++)
            {
                var tag = new TextBlock
                {
                    Text = string.Create(CultureInfo.InvariantCulture, $"{r * _session.Cols + c} ({r},{c})"),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xCB, 0xD5, 0xE1)),
                    IsHitTestVisible = false,
                    FontFamily = (FontFamily)FindResource("Mono"),
                };
                Canvas.SetLeft(tag, ox + c * _cell + 4);
                Canvas.SetTop(tag, oy + r * _cell + 2);
                Panel.SetZIndex(tag, 6);
                Board.Children.Add(tag);
            }
        _hoverOverlay.Clear();
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
            ToolTip = $"{p.Label}",
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
        ulong mask = MaskOf(p);
        border.MouseEnter += (_, _) => ApplyHover(mask);
        border.MouseLeave += (_, _) => ApplyHover(0);
        return border;
    }

    private ulong MaskOf(PieceView p)
    {
        ulong m = 0;
        foreach (var (r, c) in p.Cells) m |= 1UL << (r * _session.Cols + c);
        return m;
    }

    /// <summary>Cross-highlights board cells, register groups and occupancy bits for a cell mask (0 clears).</summary>
    private void ApplyHover(ulong mask)
    {
        _hoverMask = mask;
        foreach (var o in _hoverOverlay) Board.Children.Remove(o);
        _hoverOverlay.Clear();
        if (Board.Tag is Point origin)
            for (ulong m = mask; m != 0; m &= m - 1)
            {
                int bit = System.Numerics.BitOperations.TrailingZeroCount(m);
                if (bit >= _session.Rows * _session.Cols) continue;
                var box = new Rectangle
                {
                    Width = _cell - 2,
                    Height = _cell - 2,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xF5, 0xB3, 0x01)),
                    StrokeThickness = 3,
                    Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xF5, 0xB3, 0x01)),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(box, origin.X + bit % _session.Cols * _cell + 1);
                Canvas.SetTop(box, origin.Y + bit / _session.Cols * _cell + 1);
                Panel.SetZIndex(box, 8);
                Board.Children.Add(box);
                _hoverOverlay.Add(box);
            }

        ulong groups = 0;
        if (mask != 0)
            for (int g = 0; g < _session.RegisterLayout.GroupCount; g++)
                if ((_session.CellsOfGroup(g) & mask) != 0) groups |= 1UL << g;
        Register.HighlightGroups(groups);
        Occupancy.SetMask(_session.Occupancy, mask);
        UpdatePieceMask();
    }

    private void UpdatePieceMask()
    {
        PieceView? piece = null;
        if (_hoverMask != 0) piece = _session.Pieces.FirstOrDefault(p => (MaskOf(p) & _hoverMask) != 0);
        else if (_focusPiece >= 0) piece = _session.Pieces.FirstOrDefault(p => p.Id == _focusPiece);
        if (piece is null)
        {
            PieceGrid.SetMask(0);
            OtherGrid.SetMask(0);
            PieceGridCaption.Text = "pieceMask";
            PieceMaskText.Text = "Hover a piece, or make a move,\nto see its footprint.";
            return;
        }
        ulong pm = MaskOf(piece), occ = _session.Occupancy, other = occ ^ pm;
        string name = _session.PieceMaskName(piece.Id);
        PieceGridCaption.Text = name;
        PieceGrid.SetMask(pm);
        OtherGrid.SetMask(other);
        PieceMaskText.Text = $"{name}\n  = 0x{pm:X16}\nOccupancyMask\n  = 0x{occ:X16}\notherPieces\n  = 0x{other:X16}";
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
            ulong pieceMask = MaskOf(piece);
            arrow.MouseEnter += (_, _) =>
            {
                ApplyHover(pieceMask);
                TraceText.Text = "PREVIEW (hovered move, not played)\n\n" + _session.Preview(idx);
            };
            arrow.MouseLeave += (_, _) =>
            {
                ApplyHover(0);
                TraceText.Text = _session.Trace;
            };
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
        _focusPiece = _session.LegalMoves[index].PieceId;
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
                if (--_animating == 0)
                {
                    BuildArrows();
                    UndoButton.IsEnabled = _session.CanUndo && !_busy;
                }
            };
            v.BeginAnimation(Canvas.LeftProperty, ax);
            v.BeginAnimation(Canvas.TopProperty, ay);
        }
        if (!any) BuildArrows();
    }

    // ---- buttons ----------------------------------------------------------------------------------------------

    private void OnRandom(object sender, RoutedEventArgs e)
    {
        var moves = _session.LegalMoves;
        if (_animating == 0 && !_busy && moves.Count > 0) TryApply(moves[Random.Shared.Next(moves.Count)].Index);
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (_busy || _animating > 0) return;
        _focusPiece = -1;
        _session.Reset();
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (_busy || _animating > 0) return;
        _focusPiece = -1;
        _session.Undo();
    }

    private void OnScramble(object sender, RoutedEventArgs e)
    {
        if (_busy || _animating > 0) return;
        _focusPiece = -1;
        _session.Scramble();
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
        RandomButton.IsEnabled = HintButton.IsEnabled = ResetButton.IsEnabled = ScrambleButton.IsEnabled = enabled;
        UndoButton.IsEnabled = enabled && _session.CanUndo;
    }
}
