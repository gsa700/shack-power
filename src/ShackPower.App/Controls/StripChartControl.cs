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

    private const double MarginLeft = 4, MarginRight = 52, MarginTop = 4, MarginBottom = 18;

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        var plot = new Rect(MarginLeft, MarginTop,
            Math.Max(1, bounds.Width - MarginLeft - MarginRight),
            Math.Max(1, bounds.Height - MarginTop - MarginBottom));

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

        // Horizontal gridlines at a nice step, labelled on the right (outside the clip).
        var step = NiceStep(range);
        for (var y = Math.Ceiling(lo / step) * step; y <= hi; y += step)
        {
            var p = Map(0, y);
            ctx.DrawLine(gridPen, new Point(plot.X, p.Y), new Point(plot.Right, p.Y));
            DrawText(ctx, y.ToString(Math.Abs(y) < 10 && step < 1 ? "0.0" : "0", CultureInfo.CurrentCulture),
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
