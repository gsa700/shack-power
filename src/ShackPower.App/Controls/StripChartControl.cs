using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ShackPower.Core;

namespace ShackPower.App.Controls;

/// <summary>
/// Immediate-mode time-series strip: min/max envelope strokes with a midline, hand-drawn like
/// the family's other controls (PeakBar's brush injection, SmithChartControl's Map() + clip).
/// Data arrives pre-decimated (<see cref="ChartSampler"/>) as an immutable list — the reference
/// swap is the invalidation. Gaps in the samples render as line breaks, never interpolation:
/// the chart must not draw power that was never measured.
/// </summary>
public sealed class StripChartControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartSample>?> SamplesProperty =
        AvaloniaProperty.Register<StripChartControl, IReadOnlyList<ChartSample>?>(nameof(Samples));

    public static readonly StyledProperty<DateTime> WindowStartProperty =
        AvaloniaProperty.Register<StripChartControl, DateTime>(nameof(WindowStart));

    public static readonly StyledProperty<double> WindowSecondsProperty =
        AvaloniaProperty.Register<StripChartControl, double>(nameof(WindowSeconds), 3600.0);

    /// <summary>Successive samples further apart than this break the midline (a data gap).</summary>
    public static readonly StyledProperty<double> GapSecondsProperty =
        AvaloniaProperty.Register<StripChartControl, double>(nameof(GapSeconds), 15.0);

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<StripChartControl, IBrush?>(nameof(LineBrush));

    public static readonly StyledProperty<IBrush?> EnvelopeBrushProperty =
        AvaloniaProperty.Register<StripChartControl, IBrush?>(nameof(EnvelopeBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<StripChartControl, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<StripChartControl, IBrush?>(nameof(LabelBrush));

    /// <summary>Unit suffix for the hover readout ("V", "A", "W").</summary>
    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<StripChartControl, string>(nameof(Unit), "");

    /// <summary>Number format for the hover readout.</summary>
    public static readonly StyledProperty<string> ValueFormatProperty =
        AvaloniaProperty.Register<StripChartControl, string>(nameof(ValueFormat), "0.0");

    static StripChartControl()
    {
        AffectsRender<StripChartControl>(SamplesProperty, WindowStartProperty, WindowSecondsProperty,
            GapSecondsProperty, LineBrushProperty, EnvelopeBrushProperty, GridBrushProperty, LabelBrushProperty);
    }

    public IReadOnlyList<ChartSample>? Samples { get => GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public DateTime WindowStart { get => GetValue(WindowStartProperty); set => SetValue(WindowStartProperty, value); }
    public double WindowSeconds { get => GetValue(WindowSecondsProperty); set => SetValue(WindowSecondsProperty, value); }
    public double GapSeconds { get => GetValue(GapSecondsProperty); set => SetValue(GapSecondsProperty, value); }
    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush? EnvelopeBrush { get => GetValue(EnvelopeBrushProperty); set => SetValue(EnvelopeBrushProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string ValueFormat { get => GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }

    private const double MarginLeft = 4, MarginRight = 52, MarginTop = 4, MarginBottom = 18;

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
        // A custom Control only hit-tests where it drew something; this invisible slab makes the
        // whole surface hoverable so the crosshair works over empty plot area too.
        ctx.DrawRectangle(Brushes.Transparent, null, bounds);
        var plot = new Rect(MarginLeft, MarginTop,
            Math.Max(1, bounds.Width - MarginLeft - MarginRight),
            Math.Max(1, bounds.Height - MarginTop - MarginBottom));

        // A control mid-layout (or in a collapsing panel) can render at near-zero size; the
        // label clamps invert their bounds there and throw, which took the whole app down once.
        if (plot.Width < 40 || plot.Height < 20) return;

        var grid = GridBrush ?? Palette.CardDimBrush;
        var label = LabelBrush ?? Palette.CardDimBrush;
        var line = LineBrush ?? Palette.BlueBrush;
        var envelope = EnvelopeBrush ?? line;
        var gridPen = new Pen(grid, 1);

        var samples = Samples;
        if (samples is null || samples.Count == 0)
        {
            ctx.DrawRectangle(gridPen, plot);
            DrawText(ctx, "no data", label, 11,
                new Point(plot.Center.X, plot.Center.Y), centered: true);
            return;
        }

        // Y range from the data, padded so the trace doesn't kiss the frame.
        var lo = samples.Min(s => s.Min);
        var hi = samples.Max(s => s.Max);
        var pad = Math.Max((hi - lo) * 0.12, 0.05);
        lo -= pad;
        hi += pad;
        var range = hi - lo;

        var windowSeconds = Math.Max(1.0, WindowSeconds);
        Point Map(double offset, double value) => new(
            plot.X + offset / windowSeconds * plot.Width,
            plot.Y + (hi - value) / range * plot.Height);

        // Horizontal gridlines at a nice step, labelled on the right (outside the clip). The
        // label precision comes from the STEP, not the magnitude — a 13.84–13.99 V axis at a
        // 0.05 step must read 13.85 / 13.90 / 13.95, never a wall of "14"s.
        var step = NiceStep(range);
        var axisFmt = StepFormat(step);
        for (var y = Math.Ceiling(lo / step) * step; y <= hi; y += step)
        {
            var p = Map(0, y);
            ctx.DrawLine(gridPen, new Point(plot.X, p.Y), new Point(plot.Right, p.Y));
            DrawText(ctx, y.ToString(axisFmt, CultureInfo.CurrentCulture),
                label, 10, new Point(plot.Right + 6, p.Y - 6));
        }

        // Time labels: start / middle / end of the window.
        for (var i = 0; i <= 2; i++)
        {
            var offset = windowSeconds * i / 2;
            var t = WindowStart.AddSeconds(offset);
            var x = plot.X + offset / windowSeconds * plot.Width;
            DrawText(ctx, t.ToString("HH:mm", CultureInfo.CurrentCulture), label, 10,
                new Point(Math.Clamp(x, plot.X + 14, plot.Right - 14), plot.Bottom + 3), centered: true, centerYAtTop: true);
        }

        ctx.DrawRectangle(gridPen, plot);

        using (ctx.PushGeometryClip(new RectangleGeometry(plot)))
        {
            // Envelope: one vertical stroke per bucket, min to max — the transmit spikes.
            var envPen = new Pen(envelope, 1);
            foreach (var s in samples)
            {
                if (s.Max - s.Min < 1e-9) continue;
                ctx.DrawLine(envPen, Map(s.OffsetSeconds, s.Min), Map(s.OffsetSeconds, s.Max));
            }

            // Midline, broken at gaps.
            var linePen = new Pen(line, 1.6);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                var pen0 = false;
                double lastOffset = 0;
                foreach (var s in samples)
                {
                    var p = Map(s.OffsetSeconds, (s.Min + s.Max) / 2);
                    if (!pen0 || s.OffsetSeconds - lastOffset > GapSeconds)
                    {
                        g.BeginFigure(p, isFilled: false);
                        pen0 = true;
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

        // Hover crosshair + readout, when the cursor is over the plot and near data.
        if (_cursor is { } cursor && plot.Contains(cursor))
        {
            var offset = (cursor.X - plot.X) / plot.Width * windowSeconds;
            if (ChartCrosshair.Nearest(samples, offset, Math.Max(GapSeconds, windowSeconds / plot.Width * 4)) is { } s)
            {
                ChartCrosshair.Draw(ctx, plot, Map(s.OffsetSeconds, hi).X,
                    WindowStart.AddSeconds(s.OffsetSeconds),
                    [new ChartCrosshair.Line(ChartCrosshair.Describe(s, ValueFormat, Unit), line)],
                    grid);
            }
        }
    }

    /// <summary>1/2/5×10ⁿ step giving roughly 3–6 gridlines across the range.</summary>
    private static double NiceStep(double range)
    {
        var raw = range / 4.0;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1e-9))));
        foreach (var m in new[] { 1.0, 2.0, 5.0 })
            if (raw <= m * mag) return m * mag;
        return 10 * mag;
    }

    /// <summary>Enough decimals to distinguish neighbouring gridlines at this step.</summary>
    private static string StepFormat(double step) =>
        step >= 1 ? "0" : step >= 0.1 ? "0.0" : step >= 0.01 ? "0.00" : "0.000";

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
