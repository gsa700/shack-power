using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ShackPower.Core;

namespace ShackPower.App.Controls;

/// <summary>
/// The combined chart, modelled directly on VictronConnect's Trends view (screenshotted from
/// the real app, 2026-08-28): <b>two</b> channels overlaid full-height, the primary's value
/// scale down the left edge and the secondary's down the right, each axis in its trace's color,
/// sharing one set of horizontal gridlines. Two-at-a-time is the load-bearing design decision —
/// the earlier three-band attempt proved that a third scale on one canvas stops being readable.
/// Each axis picks a nice step so its five shared tick rows carry round numbers. Same
/// immediate-mode recipe as <see cref="StripChartControl"/>.
/// </summary>
public sealed class OverlayChartControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartSample>?> PrimarySamplesProperty =
        AvaloniaProperty.Register<OverlayChartControl, IReadOnlyList<ChartSample>?>(nameof(PrimarySamples));

    public static readonly StyledProperty<IReadOnlyList<ChartSample>?> SecondarySamplesProperty =
        AvaloniaProperty.Register<OverlayChartControl, IReadOnlyList<ChartSample>?>(nameof(SecondarySamples));

    public static readonly StyledProperty<IBrush?> PrimaryBrushProperty =
        AvaloniaProperty.Register<OverlayChartControl, IBrush?>(nameof(PrimaryBrush));

    public static readonly StyledProperty<IBrush?> SecondaryBrushProperty =
        AvaloniaProperty.Register<OverlayChartControl, IBrush?>(nameof(SecondaryBrush));

    public static readonly StyledProperty<string> PrimaryUnitProperty =
        AvaloniaProperty.Register<OverlayChartControl, string>(nameof(PrimaryUnit), "");

    public static readonly StyledProperty<string> SecondaryUnitProperty =
        AvaloniaProperty.Register<OverlayChartControl, string>(nameof(SecondaryUnit), "");

    public static readonly StyledProperty<DateTime> WindowStartProperty =
        AvaloniaProperty.Register<OverlayChartControl, DateTime>(nameof(WindowStart));

    public static readonly StyledProperty<double> WindowSecondsProperty =
        AvaloniaProperty.Register<OverlayChartControl, double>(nameof(WindowSeconds), 3600.0);

    /// <summary>Successive samples further apart than this break the midline (a data gap).</summary>
    public static readonly StyledProperty<double> GapSecondsProperty =
        AvaloniaProperty.Register<OverlayChartControl, double>(nameof(GapSeconds), 15.0);

    // Full three-channel sets for the hover readout — always all of V/A/W at the cursor, not
    // just the two channels on display.
    public static readonly StyledProperty<IReadOnlyList<ChartSample>?> VoltSamplesProperty =
        AvaloniaProperty.Register<OverlayChartControl, IReadOnlyList<ChartSample>?>(nameof(VoltSamples));

    public static readonly StyledProperty<IReadOnlyList<ChartSample>?> AmpSamplesProperty =
        AvaloniaProperty.Register<OverlayChartControl, IReadOnlyList<ChartSample>?>(nameof(AmpSamples));

    public static readonly StyledProperty<IReadOnlyList<ChartSample>?> WattSamplesProperty =
        AvaloniaProperty.Register<OverlayChartControl, IReadOnlyList<ChartSample>?>(nameof(WattSamples));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<OverlayChartControl, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<OverlayChartControl, IBrush?>(nameof(LabelBrush));

    static OverlayChartControl()
    {
        AffectsRender<OverlayChartControl>(PrimarySamplesProperty, SecondarySamplesProperty,
            PrimaryBrushProperty, SecondaryBrushProperty, PrimaryUnitProperty, SecondaryUnitProperty,
            WindowStartProperty, WindowSecondsProperty, GapSecondsProperty,
            GridBrushProperty, LabelBrushProperty);
    }

    public IReadOnlyList<ChartSample>? PrimarySamples { get => GetValue(PrimarySamplesProperty); set => SetValue(PrimarySamplesProperty, value); }
    public IReadOnlyList<ChartSample>? SecondarySamples { get => GetValue(SecondarySamplesProperty); set => SetValue(SecondarySamplesProperty, value); }
    public IBrush? PrimaryBrush { get => GetValue(PrimaryBrushProperty); set => SetValue(PrimaryBrushProperty, value); }
    public IBrush? SecondaryBrush { get => GetValue(SecondaryBrushProperty); set => SetValue(SecondaryBrushProperty, value); }
    public string PrimaryUnit { get => GetValue(PrimaryUnitProperty); set => SetValue(PrimaryUnitProperty, value); }
    public string SecondaryUnit { get => GetValue(SecondaryUnitProperty); set => SetValue(SecondaryUnitProperty, value); }
    public DateTime WindowStart { get => GetValue(WindowStartProperty); set => SetValue(WindowStartProperty, value); }
    public double WindowSeconds { get => GetValue(WindowSecondsProperty); set => SetValue(WindowSecondsProperty, value); }
    public double GapSeconds { get => GetValue(GapSecondsProperty); set => SetValue(GapSecondsProperty, value); }
    public IReadOnlyList<ChartSample>? VoltSamples { get => GetValue(VoltSamplesProperty); set => SetValue(VoltSamplesProperty, value); }
    public IReadOnlyList<ChartSample>? AmpSamples { get => GetValue(AmpSamplesProperty); set => SetValue(AmpSamplesProperty, value); }
    public IReadOnlyList<ChartSample>? WattSamples { get => GetValue(WattSamplesProperty); set => SetValue(WattSamplesProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    private const int Ticks = 4;   // intervals; 5 label rows, like the VictronConnect plot
    private const double MarginLeft = 64, MarginRight = 64, MarginTop = 6, MarginBottom = 18;

    private Point? _cursor;
    private Point? _dragLast;

    /// <summary>Wheel zoom over the plot: (anchor fraction 0..1, scale factor).</summary>
    public event Action<double, double>? ZoomRequested;

    /// <summary>Drag pan, as a fraction of the current window (positive = later in time).</summary>
    public event Action<double>? PanRequested;

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        if (_dragLast is { } last)
        {
            var plotWidth = Math.Max(1, Bounds.Width - MarginLeft - MarginRight);
            PanRequested?.Invoke(-(pos.X - last.X) / plotWidth);   // drag right = look earlier
            _dragLast = pos;
        }
        _cursor = pos;
        InvalidateVisual();
    }

    protected override void OnPointerExited(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _cursor = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var plotWidth = Math.Max(1, Bounds.Width - MarginLeft - MarginRight);
        var anchor = Math.Clamp((e.GetPosition(this).X - MarginLeft) / plotWidth, 0, 1);
        ZoomRequested?.Invoke(anchor, e.Delta.Y > 0 ? 1 / 1.3 : 1.3);
        e.Handled = true;
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragLast = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragLast = null;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = Bounds;
        // Invisible slab so the whole surface hit-tests for the hover crosshair.
        ctx.DrawRectangle(Brushes.Transparent, null, bounds);
        var plot = new Rect(MarginLeft, MarginTop,
            Math.Max(1, bounds.Width - MarginLeft - MarginRight),
            Math.Max(1, bounds.Height - MarginTop - MarginBottom));

        // Never draw into a mid-layout sliver — the label clamps invert their bounds there.
        if (plot.Width < 40 || plot.Height < 20) return;

        var grid = GridBrush ?? Palette.CardDimBrush;
        var label = LabelBrush ?? Palette.CardDimBrush;
        var gridPen = new Pen(grid, 1);

        var primary = PrimarySamples;
        var secondary = SecondarySamples;
        if ((primary is null || primary.Count == 0) && (secondary is null || secondary.Count == 0))
        {
            ctx.DrawRectangle(gridPen, plot);
            DrawText(ctx, "no data", label, 11, new Point(plot.Center.X, plot.Center.Y), centered: true);
            return;
        }

        var windowSeconds = Math.Max(1.0, WindowSeconds);

        // Shared gridline rows; each side labels them in its own scale and color.
        for (var i = 1; i < Ticks; i++)
        {
            var y = plot.Bottom - plot.Height * i / Ticks;
            ctx.DrawLine(gridPen, new Point(plot.X, y), new Point(plot.Right, y));
        }
        // A center vertical, like the reference plot's midline.
        ctx.DrawLine(gridPen, new Point(plot.Center.X, plot.Y), new Point(plot.Center.X, plot.Bottom));
        ctx.DrawRectangle(gridPen, plot);

        var timeFmt = windowSeconds < 600 ? "HH:mm:ss" : "HH:mm";
        for (var i = 0; i <= 2; i++)
        {
            var offset = windowSeconds * i / 2;
            var t = WindowStart.AddSeconds(offset);
            var x = plot.X + offset / windowSeconds * plot.Width;
            DrawText(ctx, t.ToString(timeFmt, CultureInfo.CurrentCulture), label, 10,
                new Point(Math.Clamp(x, plot.X + 14, plot.Right - 14), plot.Bottom + 3),
                centered: true, centerYAtTop: true);
        }

        // The reference app separates the scaling so the traces don't ride on top of each
        // other: each axis range gets extra room on one side, parking the secondary's trace in
        // the upper half and the primary's in the lower (volts over amps with the defaults).
        DrawSeries(ctx, plot, primary, PrimaryBrush ?? Palette.OrangeDeepBrush, PrimaryUnit,
            windowSeconds, leftAxis: true, biasHigh: false);
        DrawSeries(ctx, plot, secondary, SecondaryBrush ?? Palette.BlueBrush, SecondaryUnit,
            windowSeconds, leftAxis: false, biasHigh: true);

        DrawCrosshair(ctx, plot, windowSeconds);
    }

    private void DrawSeries(DrawingContext ctx, Rect plot, IReadOnlyList<ChartSample>? samples,
        IBrush brush, string unit, double windowSeconds, bool leftAxis, bool biasHigh)
    {
        if (samples is null || samples.Count == 0) return;

        var (axisMin, step) = Axis(samples, biasHigh);
        var span = Ticks * step;

        // Tick labels down this series' side, in its color — the reader attaches numbers to a
        // trace by color, exactly as VictronConnect does it.
        var fmt = ChartScale.StepFormat(step);
        for (var i = 0; i <= Ticks; i++)
        {
            var value = axisMin + i * step;
            var y = plot.Bottom - plot.Height * i / Ticks - 6;
            var text = $"{value.ToString(fmt, CultureInfo.CurrentCulture)} {unit}";
            if (leftAxis) DrawTextRight(ctx, text, brush, 10, new Point(plot.X - 6, y));
            else DrawText(ctx, text, brush, 10, new Point(plot.Right + 6, y));
        }

        Point Map(double offset, double value) => new(
            plot.X + offset / windowSeconds * plot.Width,
            plot.Y + (axisMin + span - value) / span * plot.Height);

        using (ctx.PushGeometryClip(new RectangleGeometry(plot)))
        {
            var envPen = new Pen(Fade(brush, 0.4), 1);
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

    private void DrawCrosshair(DrawingContext ctx, Rect plot, double windowSeconds)
    {
        if (_cursor is not { } cursor || !plot.Contains(cursor)) return;

        var offset = (cursor.X - plot.X) / plot.Width * windowSeconds;
        var tolerance = Math.Max(GapSeconds, windowSeconds / plot.Width * 4);
        // Always all three channels, not just the two on display — the reader hovering "what
        // happened at 18:30" wants the whole picture.
        if (ChartCrosshair.AllChannels(VoltSamples, AmpSamples, WattSamples, offset, tolerance)
            is { } hit)
        {
            var x = plot.X + hit.Offset / windowSeconds * plot.Width;
            ChartCrosshair.Draw(ctx, plot, x, WindowStart.AddSeconds(hit.Offset), hit.Lines,
                GridBrush ?? Palette.CardDimBrush);
        }
    }

    /// <summary>
    /// Axis for one series: a nice step and a floor such that <see cref="Ticks"/> intervals of
    /// that step cover the data — so the shared gridline rows carry round numbers on both sides
    /// even though the sides have unrelated scales. The step is sized so the data spans only
    /// about half the intervals, and <paramref name="biasHigh"/> decides which half: the spare
    /// intervals go under a high-biased trace and over a low-biased one, which is how the two
    /// overlaid traces get their own vertical territory.
    /// </summary>
    private static (double Min, double Step) Axis(IReadOnlyList<ChartSample> samples, bool biasHigh)
    {
        var lo = double.MaxValue;
        var hi = double.MinValue;
        foreach (var s in samples)
        {
            if (s.Min < lo) lo = s.Min;
            if (s.Max > hi) hi = s.Max;
        }
        if (hi - lo < 1e-9) { lo -= 0.5; hi += 0.5; }   // a flat line still needs a scale

        // step ≥ range/2 ⇒ the data fits in ~2 of the 4 intervals; the anchor picks which two.
        var step = NiceCeil((hi - lo) / 2.0);
        var min = biasHigh
            ? Math.Ceiling(hi / step) * step - Ticks * step   // data hugs the top rows
            : Math.Floor(lo / step) * step;                   // data hugs the bottom rows
        while (min + Ticks * step < hi) step = NiceCeil(step * 1.01);   // belt for float edges
        return (min, step);
    }

    /// <summary>Smallest 1/2/5×10ⁿ value ≥ <paramref name="raw"/>.</summary>
    private static double NiceCeil(double raw)
    {
        var mag = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1e-12))));
        foreach (var m in new[] { 1.0, 2.0, 5.0 })
            if (raw <= m * mag) return m * mag;
        return 10 * mag;
    }

    private static IBrush Fade(IBrush brush, double opacity) =>
        brush is ISolidColorBrush s ? new SolidColorBrush(s.Color, opacity) : brush;

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

    private static void DrawTextRight(DrawingContext ctx, string text, IBrush brush, double size, Point rightEdge)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush);
        ctx.DrawText(ft, new Point(rightEdge.X - ft.Width, rightEdge.Y));
    }
}
