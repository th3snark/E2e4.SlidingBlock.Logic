using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace E2e4.SlidingBlock.Logic.Labs.Wpf;

public readonly record struct GroupInfo(Color Color, string Label, string Footer = "", string Detail = "");

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
        var dim = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        var bright = Brushes.White;
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        for (int g = 0; g < _groupCount; g++)
        {
            int p = _groupCount - 1 - g;
            double x0 = p % _perRow * (_groupBits * CellW + Gap), y0 = p / _perRow * RowH;
            var info = _info?.Invoke(g) ?? new GroupInfo(Colors.SlateGray, g.ToString(CultureInfo.InvariantCulture));
            var bg = new SolidColorBrush(Color.FromArgb(0x55, info.Color.R, info.Color.G, info.Color.B));
            drawingContext.DrawRoundedRectangle(bg, new Pen(new SolidColorBrush(Color.FromArgb(0xAA, info.Color.R, info.Color.G, info.Color.B)), 1),
                new Rect(x0, y0 + LabelH, _groupBits * CellW, CellH), 3, 3);
            if (((_highlight >> g) & 1UL) != 0)
                drawingContext.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0xF5, 0xB3, 0x01)), 2.5),
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
                    drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * Flash), 0xF5, 0xB3, 0x01)), null, cell);
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

    public Color Accent { get; set; } = Color.FromRgb(0x38, 0xBD, 0xF8);

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
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)), 1);
        var on = new SolidColorBrush(Color.FromArgb(0x70, Accent.R, Accent.G, Accent.B));
        var hi = new SolidColorBrush(Color.FromArgb(0xB0, 0xF5, 0xB3, 0x01));
        var dim = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
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
