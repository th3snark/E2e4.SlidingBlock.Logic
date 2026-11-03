using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace E2e4.SlidingBlock.Logic.Labs.Wpf;

public readonly record struct GroupInfo(Color Color, string Label, string Footer = "", string Detail = "");

/// <summary>The colours the labs share, with frozen brushes, so a view does not repeat the same RGB literals or build a brush on every render.</summary>
internal static class Palette
{
    public static readonly Color Gold = Color.FromRgb(0xF5, 0xB3, 0x01);
    public static readonly Color Accent = Color.FromRgb(0x38, 0xBD, 0xF8);
    public static readonly Color Muted = Color.FromRgb(0x94, 0xA3, 0xB8);
    public static readonly Color Dim = Color.FromRgb(0x64, 0x74, 0x8B);
    public static readonly Color Slate = Color.FromRgb(0x47, 0x55, 0x69);
    public static readonly Color GridLine = Color.FromRgb(0x33, 0x41, 0x55);
    public static readonly Color Edge = Color.FromRgb(0x2A, 0x38, 0x50);
    public static readonly Color Cell = Color.FromRgb(0x17, 0x20, 0x33);
    public static readonly Color CellAlt = Color.FromRgb(0x1A, 0x25, 0x3B);
    public static readonly Color Text = Color.FromRgb(0xCB, 0xD5, 0xE1);
    public static readonly Color Teal = Color.FromRgb(0x2D, 0xD4, 0xBF);
    public static readonly Color Orange = Color.FromRgb(0xFB, 0x92, 0x3C);
    public static readonly Color GoodDeep = Color.FromRgb(0x06, 0x4E, 0x3B);
    public static readonly Color BadDeep = Color.FromRgb(0x7F, 0x1D, 0x1D);

    public static readonly SolidColorBrush GoldBrush = Frozen(Gold);
    public static readonly SolidColorBrush MutedBrush = Frozen(Muted);
    public static readonly SolidColorBrush DimBrush = Frozen(Dim);
    public static readonly SolidColorBrush SlateBrush = Frozen(Slate);
    public static readonly SolidColorBrush CellBrush = Frozen(Cell);
    public static readonly SolidColorBrush CellAltBrush = Frozen(CellAlt);
    public static readonly SolidColorBrush EdgeBrush = Frozen(Edge);

    private static readonly Dictionary<(Color, byte), SolidColorBrush> Tints = [];

    /// <summary>A frozen brush of the colour.</summary>
    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>A frozen brush of the colour at the given alpha, shared by every caller that asks for the same pair. UI thread only.</summary>
    public static SolidColorBrush Tint(Color color, byte alpha)
    {
        if (!Tints.TryGetValue((color, alpha), out var brush)) Tints[(color, alpha)] = brush = Frozen(Color.FromArgb(alpha, color.R, color.G, color.B));
        return brush;
    }
}

internal static class DrawHelpers
{
    public static readonly Typeface Mono = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static FormattedText Text(string s, double size, Brush brush, Visual v, bool bold = false)
    {
        var tf = bold ? new Typeface(Mono.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal) : Mono;
        return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, brush, VisualTreeHelper.GetDpi(v).PixelsPerDip);
    }

    public static void DrawCentered(DrawingContext drawingContext, FormattedText t, Rect r) =>
        drawingContext.DrawText(t, new Point(r.X + (r.Width - t.Width) / 2, r.Y + (r.Height - t.Height) / 2));

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>Hue in degrees (0 to 360), saturation and value (0 to 1) as a colour.</summary>
    public static Color Hue(double hue, double sat = 0.55, double val = 0.85)
    {
        double c = val * sat, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = val - c;
        (double r, double g, double b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    /// <summary>The colour moved <paramref name="f"/> of the way to white.</summary>
    public static Color Lighten(Color c, double f) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

    /// <summary>Merged outline of a polyomino with its anchor cell at the local origin, cells of <paramref name="cell"/> pixels inset only on exposed sides.</summary>
    public static Geometry FootprintGeometry(EquivalenceGroup<Bitboard64> g, double cell, double inset = 2)
    {
        var set = g.RelativeCells.ToHashSet();
        Geometry? union = null;
        foreach (var (dr, dc) in g.RelativeCells)
        {
            double l = dc * cell + (set.Contains((dr, dc - 1)) ? 0 : inset), t = dr * cell + (set.Contains((dr - 1, dc)) ? 0 : inset);
            double r = (dc + 1) * cell - (set.Contains((dr, dc + 1)) ? 0 : inset), b = (dr + 1) * cell - (set.Contains((dr + 1, dc)) ? 0 : inset);
            Geometry rect = new RectangleGeometry(new Rect(l, t, r - l, b - t));
            union = union is null ? rect : Geometry.Combine(union, rect, GeometryCombineMode.Union, null);
        }
        union!.Freeze();
        return union;
    }

    /// <summary>The ten corners of a five-pointed star centred on (<paramref name="cx"/>, <paramref name="cy"/>).</summary>
    public static PointCollection StarPoints(double cx, double cy, double outer, double inner)
    {
        var pts = new PointCollection();
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, rad = i % 2 == 0 ? outer : inner;
            pts.Add(new Point(cx + rad * Math.Cos(a), cy + rad * Math.Sin(a)));
        }
        return pts;
    }
}

/// <summary>Renders a 64-bit register as colour-coded bit groups; changed bits flash gold and fade.</summary>
public sealed class RegisterView : FrameworkElement
{
    public static readonly DependencyProperty FlashProperty = DependencyProperty.Register(
        nameof(Flash), typeof(double), typeof(RegisterView), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double CellW = 13, CellH = 20, Gap = 7, LabelH = 13, FooterH = 12;
    private const double RowH = LabelH + CellH + FooterH + 8;
    private ulong _value, _changed;
    private int _groupBits = 4, _groupCount = 16, _perRow = 8;
    private Func<int, GroupInfo>? _info;
    private ulong _highlight;
    private int _hot = -1;

    /// <summary>Raised when the pointer enters a bit group (its index) or leaves the register (-1).</summary>
    public event Action<int>? GroupHovered;

    public double Flash { get => (double)GetValue(FlashProperty); set => SetValue(FlashProperty, value); }

    public void Configure(int groupBits, int groupCount, int groupsPerRow, Func<int, GroupInfo> info)
    {
        _groupBits = groupBits;
        _groupCount = groupCount;
        _perRow = groupsPerRow;
        _info = info;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Outlines the given groups (bit g = group g); used for cross-highlighting from the board.</summary>
    public void HighlightGroups(ulong groups)
    {
        _highlight = groups;
        InvalidateVisual();
    }

    private int HitGroup(Point pt)
    {
        double gw = _groupBits * CellW + Gap;
        int col = (int)(pt.X / gw), row = (int)(pt.Y / RowH);
        if (pt.X < 0 || pt.Y < 0 || col >= _perRow || pt.X - col * gw > _groupBits * CellW) return -1;
        int p = row * _perRow + col;
        return p < _groupCount ? _groupCount - 1 - p : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int g = HitGroup(e.GetPosition(this));
        if (g == _hot) return;
        _hot = g;
        string detail = g >= 0 && _info is not null ? _info(g).Detail : "";
        ToolTip = detail.Length > 0 ? detail : null;
        GroupHovered?.Invoke(g);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        ToolTip = null;
        GroupHovered?.Invoke(-1);
    }

    public void Update(ulong value, ulong changed)
    {
        _value = value;
        _changed = changed;
        InvalidateVisual();
        if (changed == 0) return;
        BeginAnimation(FlashProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1100)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int rows = (_groupCount + _perRow - 1) / _perRow;
        return new Size(_perRow * (_groupBits * CellW + Gap), rows * RowH);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var dim = Palette.DimBrush;
        var bright = Brushes.White;
        var labelBrush = Palette.MutedBrush;
        for (int g = 0; g < _groupCount; g++)
        {
            int p = _groupCount - 1 - g;
            double x0 = p % _perRow * (_groupBits * CellW + Gap), y0 = p / _perRow * RowH;
            var info = _info?.Invoke(g) ?? new GroupInfo(Colors.SlateGray, g.ToString(CultureInfo.InvariantCulture));
            drawingContext.DrawRoundedRectangle(Palette.Tint(info.Color, 0x55), new Pen(Palette.Tint(info.Color, 0xAA), 1),
                new Rect(x0, y0 + LabelH, _groupBits * CellW, CellH), 3, 3);
            if (((_highlight >> g) & 1UL) != 0)
                drawingContext.DrawRoundedRectangle(null, new Pen(Palette.GoldBrush, 2.5),
                    new Rect(x0 - 2, y0 + LabelH - 2, _groupBits * CellW + 4, CellH + 4), 4, 4);
            if (info.Footer.Length > 0)
            {
                var ft = DrawHelpers.Text(info.Footer, 9, labelBrush, this);
                drawingContext.DrawText(ft, new Point(x0 + (_groupBits * CellW - ft.Width) / 2, y0 + LabelH + CellH + 1));
            }
            var lt = DrawHelpers.Text(info.Label, 9, labelBrush, this);
            drawingContext.DrawText(lt, new Point(x0 + (_groupBits * CellW - lt.Width) / 2, y0));
            for (int i = _groupBits - 1; i >= 0; i--)
            {
                int bit = g * _groupBits + i;
                bool on = ((_value >> bit) & 1UL) != 0;
                var cell = new Rect(x0 + (_groupBits - 1 - i) * CellW, y0 + LabelH, CellW, CellH);
                if (((_changed >> bit) & 1UL) != 0 && Flash > 0)
                    drawingContext.DrawRectangle(Palette.Tint(Palette.Gold, (byte)(255 * Flash)), null, cell);
                DrawHelpers.DrawCentered(drawingContext, DrawHelpers.Text(on ? "1" : "0", 12, on ? bright : dim, this, on), cell);
            }
        }
    }
}

/// <summary>Renders a bitboard as a width x height matrix of 0/1 cells (bit 0 top-left).</summary>
public sealed class BitGridView : FrameworkElement
{
    private int _cols = 4, _rows = 4;
    private ulong _mask, _highlight;
    private double _cell = 26;
    private bool _showIndex;

    public Color Accent { get; set; } = Palette.Accent;

    public void Configure(int cols, int rows, double cell = 26, bool showIndex = false)
    {
        _cols = cols;
        _rows = rows;
        _cell = cell;
        _showIndex = showIndex;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetMask(ulong mask, ulong highlight = 0)
    {
        _mask = mask;
        _highlight = highlight;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(_cols * _cell + 1, _rows * _cell + 1);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var pen = new Pen(Palette.Frozen(Palette.GridLine), 1);
        var on = Palette.Tint(Accent, 0x70);
        var hi = Palette.Tint(Palette.Gold, 0xB0);
        var dim = Palette.DimBrush;
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++)
            {
                int bit = r * _cols + c;
                bool set = ((_mask >> bit) & 1UL) != 0;
                var rect = new Rect(c * _cell, r * _cell, _cell, _cell);
                bool h = ((_highlight >> bit) & 1UL) != 0;
                drawingContext.DrawRectangle(h ? hi : set ? on : null, pen, rect);
                DrawHelpers.DrawCentered(drawingContext, DrawHelpers.Text(set ? "1" : "0", _cell * 0.45, set ? Brushes.White : dim, this, set), rect);
                if (_showIndex)
                    drawingContext.DrawText(DrawHelpers.Text(bit.ToString(CultureInfo.InvariantCulture), 7, dim, this), new Point(rect.X + 2, rect.Y + 1));
            }
    }
}
