using System.Globalization;

namespace ShackPower.Core;

/// <summary>
/// One 1 Hz row of the daily power CSV. <b>The format is the Python prototype's, byte for
/// byte</b> — header string, ISO-seconds timestamp, Python's float formatting (shortest
/// round-trip, but integral values keep one decimal: <c>88.0</c>), and an empty cell for null —
/// because at cutover this app continues the very file the prototype started that morning, and
/// the two halves must be indistinguishable to any reader.
/// </summary>
public sealed record PowerLogRecord
{
    public required DateTime Timestamp { get; init; }
    public double? Volts { get; init; }
    public double? Amps { get; init; }
    public double? Watts { get; init; }

    public const string CsvHeader = "timestamp,volts,amps,watts";

    public string ToCsvRow() =>
        $"{Timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)},{F(Volts)},{F(Amps)},{F(Watts)}";

    /// <summary>Python's <c>str(float)</c>: shortest round-trip ("R"), except integral values
    /// render as <c>88.0</c>, never <c>88</c>. Invariant — never a group separator, which in a
    /// CSV would split one value across two columns (the family's documented "N"-format trap).</summary>
    private static string F(double? v) => v switch
    {
        null => "",
        { } x when x == Math.Floor(x) && Math.Abs(x) < 1e15 => x.ToString("0.0", CultureInfo.InvariantCulture),
        { } x => x.ToString("R", CultureInfo.InvariantCulture),
    };
}
