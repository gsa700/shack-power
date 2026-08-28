using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ShackPower.Core;

namespace ShackPower.App.Controls;

/// <summary>
/// The combined chart: all three channels overlaid on one tall plot, each <b>independently
/// normalized</b> to the full plot height. A shared y-axis would be a lie of scale — volts live
/// in a 0.2 V band while watts swing hundreds, so a common axis flattens the voltage trace into
/// a ruler line. Instead each series stretches to its own min/max, and the numbers come from the
/// color-coded range labels down the right edge (top = each trace's max, bottom = its min).
/// Same immediate-mode recipe as <see cref="StripChartControl"/>.
/// </summary>
public sealed class OverlayChartControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartSample>?> VoltSamplesProperty =
        AvaloniaProperty.Register<OverlayChartControl, IReadOnlyList<ChartSample>?>(nameof(VoltSamples));

    public static readonly StyledProperty<IReadOnlyList<ChartSample>?> AmpSamplesProperty =
        AvaloniaProperty.Register<OverlayChartControl, IReadOnlyList<ChartSample>?>(nameof(AmpSamples));

    public static readonly StyledProperty<IReadOnlyList<ChartSample>?> WattSamplesProperty =
        AvaloniaProperty.Register<OverlayChartControl, IReadOnlyList<ChartSample>?>(nameof(WattSamples));

    public static readonly StyledProperty<DateTime> WindowStartProperty =
        AvaloniaProperty.Register<OverlayChartControl, DateTime>(nameof(WindowStart));

    public static readonly StyledProperty<double> WindowSecondsProperty =
        AvaloniaProperty.Register<OverlayChartControl, double>(nameof(WindowSeconds), 3600.0);

    public static readonly StyledProperty<double> GapSecondsProperty =
        AvaloniaProperty.Register<OverlayChartControl, double>(nameof(GapSeconds), 15.0);

    public static readonly StyledProperty<IBrush?> VoltBrushProperty =
        AvaloniaProperty.Register<OverlayChartControl, IBrush?>(nameof(VoltBrush));

    public static readonly StyledProperty<IBrush?> AmpBrushProperty =
        AvaloniaProperty.Register<OverlayChartControl, IBrush?>(nameof(AmpBrush));

    public static readonly StyledProperty<IBrush?> WattBrushProperty =
        AvaloniaProperty.Register<OverlayChartControl, IBrush?>(nameof(WattBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<OverlayChartControl, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<OverlayChartControl, IBrush?>(nameof(LabelBrush));

    static OverlayChartControl()
    {
        AffectsRender<OverlayChartControl>(VoltSamplesProperty, AmpSamplesProperty, WattSamplesProperty,
            WindowStartProperty, WindowSecondsProperty, GapSecondsProperty,
            VoltBrushProperty, AmpBrushProperty, WattBrushProperty, GridBrushProperty, LabelBrushProperty);
    }

    public IReadOnlyList<ChartSample>? VoltSamples { get => GetValue(VoltSamplesProperty); set => SetValue(VoltSamplesProperty, value); }
    public IReadOnlyList<ChartSample>? AmpSamples { get => GetValue(AmpSamplesProperty); set => SetValue(AmpSamplesProperty, value); }
    public IReadOnlyList<ChartSample>? WattSamples { get => GetValue(WattSamplesProperty); set => SetValue(WattSamplesProperty, value); }
    public DateTime WindowStart { get => GetValue(WindowStartProperty); set => SetValue(WindowStartProperty, value); }
    public double WindowSeconds { get => GetValue(WindowSecondsProperty); set => SetValue(WindowSecondsProperty, value); }
    public double GapSeconds { get => GetValue(GapSecondsProperty); set => SetValue(GapSecondsProperty, value); }
    public IBrush? VoltBrush { get => GetValue(VoltBrushProperty); set => SetValue(VoltBrushProperty, value); }
    public IBrush? AmpBrush { get => GetValue(AmpBrushProperty); set => SetValue(AmpBrushProperty, value); }
    public IBrush? WattBrush { get => GetValue(WattBrushProperty); set => SetValue(WattBrushProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    private const double MarginLeft = 4, MarginRight = 78, MarginTop = 4, MarginBottom = 18;

    private Point? _cursor;

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _cursor = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _cursor = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        // Invisible slab so the whole surface hit-tests for the hover crosshair.
        ctx.DrawRectangle(Brushes.Transparent, null, bounds);
        var plot = new Rect(MarginLeft, MarginTop,
            Math.Max(1, bounds.Width - MarginLeft - MarginRight),
            Math.Max(1, bounds.Height - MarginTop - MarginBottom));

        // Same guard as StripChartControl: never draw into a mid-layout sliver — the label
        // clamps invert their bounds below ~40px and throw.
        if (plot.Width < 40 || plot.Height < 20) return;

        var grid = GridBrush ?? Palette.CardDimBrush;
        var label = LabelBrush ?? Palette.CardDimBrush;
        var gridPen = new Pen(grid, 1);

        var series = new (IReadOnlyList<ChartSample>? Samples, IBrush Brush, string Unit, string Fmt)[]
        {
            (VoltSamples, VoltBrush ?? Palette.BlueBrush, "V", "0.00"),
            (AmpSamples, AmpBrush ?? Palette.OrangeDeepBrush, "A", "0.0"),
            (WattSamples, WattBrush ?? Palette.GreenBrush, "W", "0"),
        };

        if (series.All(s => s.Samples is null || s.Samples.Count == 0))
        {
            ctx.DrawRectangle(gridPen, plot);
            DrawText(ctx, "no data", label, 11, new Point(plot.Center.X, plot.Center.Y), centered: true);
            return;
        }

        // Unlabelled quarter gridlines: with three private scales the lines are visual rhythm,
        // not values — the values live in the per-series range labels on the right.
        for (var i = 1; i <= 3; i++)
        {
            var y = plot.Y + plot.Height * i / 4;
            ctx.DrawLine(gridPen, new Point(plot.X, y), new Point(plot.Right, y));
        }

        var windowSeconds = Math.Max(1.0, WindowSeconds);
        for (var i = 0; i <= 2; i++)
        {
            var offset = windowSeconds * i / 2;
            var t = WindowStart.AddSeconds(offset);
            var x = plot.X + offset / windowSeconds * plot.Width;
            DrawText(ctx, t.ToString("HH:mm", CultureInfo.CurrentCulture), label, 10,
                new Point(Math.Clamp(x, plot.X + 14, plot.Right - 14), plot.Bottom + 3),
                centered: true, centerYAtTop: true);
        }

        ctx.DrawRectangle(gridPen, plot);

        // Range labels: three stacked rows at the top-right (each trace's max) and bottom-right
        // (its min), in the trace's own color — how a reader attaches numbers to a trace.
        var row = 0;
        foreach (var (samples, brush, unit, fmt) in series)
        {
            if (samples is null || samples.Count == 0) { row++; continue; }
            var (lo, hi) = RangeOf(samples);
            DrawText(ctx, $"{hi.ToString(fmt, CultureInfo.CurrentCulture)} {unit}", brush, 10,
                new Point(plot.Right + 6, plot.Y + row * 12));
            DrawText(ctx, $"{lo.ToString(fmt, CultureInfo.CurrentCulture)} {unit}", brush, 10,
                new Point(plot.Right + 6, plot.Bottom - 12 * (3 - row)));
            row++;
        }

        using (ctx.PushGeometryClip(new RectangleGeometry(plot)))
        {
            foreach (var (samples, brush, _, _) in series)
            {
                if (samples is null || samples.Count == 0) continue;
                var (lo, hi) = RangeOf(samples);
                var pad = Math.Max((hi - lo) * 0.06, 0.02);
                lo -= pad;
                hi += pad;
                var range = hi - lo;

                Point Map(double offset, double value) => new(
                    plot.X + offset / windowSeconds * plot.Width,
                    plot.Y + (hi - value) / range * plot.Height);

                var envPen = new Pen(Fade(brush), 1);
                foreach (var s in samples)
                {
                    if (s.Max - s.Min < 1e-9) continue;
                    ctx.DrawLine(envPen, Map(s.OffsetSeconds, s.Min), Map(s.OffsetSeconds, s.Max));
                }

                var linePen = new Pen(brush, 1.6);
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    var penDown = false;
                    double lastOffset = 0;
                    foreach (var s in samples)
                    {
                        var p = Map(s.OffsetSeconds, (s.Min + s.Max) / 2);
                        if (!penDown || s.OffsetSeconds - lastOffset > GapSeconds)
                        {
                            g.BeginFigure(p, isFilled: false);
                            penDown = true;
                        }
                        else
                        {
                            g.LineTo(p);
                        }
                        lastOffset = s.OffsetSeconds;
                    }
                    g.EndFigure(false);
                }
                ctx.DrawGeometry(null, linePen, geometry);
            }
        }

        // Hover crosshair: one readout box with all three channels at the cursor's moment,
        // each line in its trace's color.
        if (_cursor is { } cursor && plot.Contains(cursor))
        {
            var offset = (cursor.X - plot.X) / plot.Width * windowSeconds;
            var tolerance = Math.Max(GapSeconds, windowSeconds / plot.Width * 4);
            var lines = new List<ChartCrosshair.Line>();
            double? snappedOffset = null;
            foreach (var (samples, brush, unit, fmt) in series)
            {
                if (ChartCrosshair.Nearest(samples, offset, tolerance) is not { } s) continue;
                snappedOffset ??= s.OffsetSeconds;
                lines.Add(new ChartCrosshair.Line(ChartCrosshair.Describe(s, fmt, unit), brush));
            }
            if (lines.Count > 0 && snappedOffset is { } snap)
            {
                var x = plot.X + snap / windowSeconds * plot.Width;
                ChartCrosshair.Draw(ctx, plot, x, WindowStart.AddSeconds(snap), lines, grid);
            }
        }
    }

    private static (double lo, double hi) RangeOf(IReadOnlyList<ChartSample> samples)
    {
        var lo = double.MaxValue;
        var hi = double.MinValue;
        foreach (var s in samples)
        {
            if (s.Min < lo) lo = s.Min;
            if (s.Max > hi) hi = s.Max;
        }
        return (lo, hi);
    }

    /// <summary>A 40%-opacity version of a solid brush for the envelope strokes, so three
    /// overlapping envelopes read as texture instead of mud.</summary>
    private static IBrush Fade(IBrush brush) =>
        brush is ISolidColorBrush s ? new SolidColorBrush(s.Color, 0.4) : brush;

    private static void DrawText(DrawingContext ctx, string text, IBrush brush, double size,
        Point at, bool centered = false, bool centerYAtTop = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush);
        var origin = centered
            ? new Point(at.X - ft.Width / 2, centerYAtTop ? at.Y : at.Y - ft.Height / 2)
            : at;
        ctx.DrawText(ft, origin);
    }
}
