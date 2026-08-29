using System.Globalization;
using Avalonia;
using Avalonia.Media;
using ShackPower.Core;

namespace ShackPower.App.Controls;

/// <summary>
/// Shared hover crosshair + value readout for the chart controls — the first pointer input in
/// the family's custom controls, kept in one place so both chart styles behave identically.
/// Everything is drawn in immediate mode like the charts themselves; no ToolTip control, no
/// popup, nothing that outlives the Render pass.
/// </summary>
internal static class ChartCrosshair
{
    public sealed record Line(string Text, IBrush Brush);

    /// <summary>Nearest sample to a cursor x-offset, or null when the cursor sits in a data gap
    /// (more than <paramref name="gapSeconds"/> from anything) — a tooltip must not invent a
    /// value where the chart deliberately shows a break.</summary>
    public static ChartSample? Nearest(IReadOnlyList<ChartSample>? samples, double offsetSeconds, double gapSeconds)
    {
        if (samples is null || samples.Count == 0) return null;
        ChartSample? best = null;
        var bestDist = double.MaxValue;
        foreach (var s in samples)
        {
            var d = Math.Abs(s.OffsetSeconds - offsetSeconds);
            if (d < bestDist) { bestDist = d; best = s; }
        }
        return bestDist <= gapSeconds ? best : null;
    }

    /// <summary>One tooltip line for a sample: the midline value, or the bucket's span when
    /// decimation squeezed visibly different readings into it.</summary>
    public static string Describe(ChartSample s, string fmt, string unit)
    {
        var c = CultureInfo.CurrentCulture;
        var spread = s.Max - s.Min;
        var mid = (s.Min + s.Max) / 2;
        // Show the envelope once it's wider than the displayed precision would hide.
        var epsilon = fmt.Contains('.') ? Math.Pow(10, -(fmt.Length - fmt.IndexOf('.') - 1)) : 1.0;
        return spread > epsilon * 2
            ? $"{s.Min.ToString(fmt, c)}…{s.Max.ToString(fmt, c)} {unit}"
            : $"{mid.ToString(fmt, c)} {unit}";
    }

    /// <summary>
    /// The standard readout: all three channels at the cursor's moment, each in its fixed
    /// color, whatever the view is displaying — the data is decimated for every channel on
    /// every refresh anyway, so the tooltip always knows everything. Returns null when the
    /// cursor is in a gap on every channel.
    /// </summary>
    public static (double Offset, IReadOnlyList<Line> Lines)? AllChannels(
        IReadOnlyList<ChartSample>? volts, IReadOnlyList<ChartSample>? amps,
        IReadOnlyList<ChartSample>? watts, double offsetSeconds, double tolerance)
    {
        var channels = new (IReadOnlyList<ChartSample>? Samples, IBrush Brush, string Fmt, string Unit)[]
        {
            (volts, Palette.BlueBrush, "0.00", "V"),
            (amps, Palette.OrangeDeepBrush, "0.00", "A"),
            (watts, Palette.GreenBrush, "0", "W"),
        };
        double? snap = null;
        var lines = new List<Line>();
        foreach (var (samples, brush, fmt, unit) in channels)
        {
            if (Nearest(samples, offsetSeconds, tolerance) is not { } s) continue;
            snap ??= s.OffsetSeconds;
            lines.Add(new Line(Describe(s, fmt, unit), brush));
        }
        return snap is { } o ? (o, lines) : null;
    }

    /// <summary>Draw the vertical cursor line and the readout box beside it.</summary>
    public static void Draw(DrawingContext ctx, Rect plot, double cursorX, DateTime time,
        IReadOnlyList<Line> lines, IBrush gridBrush)
    {
        ctx.DrawLine(new Pen(gridBrush, 1) { DashStyle = DashStyle.Dash },
            new Point(cursorX, plot.Y), new Point(cursorX, plot.Bottom));

        // The timestamp gets the readable dim gray, not the grid color — grid-pale text on the
        // white readout box is exactly the unreadability this tooltip exists to fix.
        var texts = new List<(FormattedText Ft, IBrush Brush)>
        {
            (Format(time.ToString("HH:mm:ss", CultureInfo.CurrentCulture), Palette.CardDimBrush), Palette.CardDimBrush),
        };
        foreach (var line in lines)
            texts.Add((Format(line.Text, line.Brush), line.Brush));

        const double padX = 8, padY = 5, lineGap = 2;
        var boxW = texts.Max(t => t.Ft.Width) + padX * 2;
        var boxH = texts.Sum(t => t.Ft.Height) + lineGap * (texts.Count - 1) + padY * 2;

        // Beside the cursor, flipping sides at the edge so it never leaves the plot.
        var x = cursorX + 10 + boxW <= plot.Right ? cursorX + 10 : cursorX - 10 - boxW;
        var y = Math.Clamp(plot.Y + 8, plot.Y, Math.Max(plot.Y, plot.Bottom - boxH));

        var box = new Rect(x, y, boxW, boxH);
        ctx.DrawRectangle(new SolidColorBrush(Colors.White, 0.94), new Pen(gridBrush, 1), box, 4, 4);

        var ty = y + padY;
        foreach (var (ft, _) in texts)
        {
            ctx.DrawText(ft, new Point(x + padX, ty));
            ty += ft.Height + lineGap;
        }
    }

    private static FormattedText Format(string text, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush);
}

/// <summary>Axis-scale arithmetic shared by both chart controls.</summary>
internal static class ChartScale
{
    /// <summary>1/2/5×10ⁿ step giving roughly 3–6 gridlines across the range.</summary>
    public static double NiceStep(double range)
    {
        var raw = range / 4.0;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1e-9))));
        foreach (var m in new[] { 1.0, 2.0, 5.0 })
            if (raw <= m * mag) return m * mag;
        return 10 * mag;
    }

    /// <summary>Enough decimals to distinguish neighbouring gridlines at this step — the label
    /// precision comes from the step, never the magnitude (a 13.84–13.99 V axis at a 0.05 step
    /// must read 13.85 / 13.90 / 13.95, not a wall of "14"s).</summary>
    public static string StepFormat(double step) =>
        step >= 1 ? "0" : step >= 0.1 ? "0.0" : step >= 0.01 ? "0.00" : "0.000";
}
