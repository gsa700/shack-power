using System.Globalization;
using System.Text;

namespace ShackPower.Core;

/// <summary>
/// Reads daily power CSVs back for the chart. Columns are resolved <b>by header name</b>, not by
/// position (the LP-100A rule — the schema will grow, and index-based parsing silently mislabels
/// columns in files written by another version). Unknown columns are ignored, missing ones read
/// as null, and a missing file reads as empty, not an error. Splitting on ',' is sufficient
/// because the writer emits only invariant fixed-point numbers — no field can contain a comma or
/// quote. If that ever stops being true, this needs a real CSV parser.
/// </summary>
public static class PowerLogReader
{
    /// <summary>Read every row of one file, oldest first.</summary>
    public static IReadOnlyList<PowerLogEntry> Read(string path)
    {
        if (!File.Exists(path)) return [];
        return Parse(File.ReadAllLines(path, Encoding.UTF8));
    }

    /// <summary>Read one day's file from a log directory (the writer's naming).</summary>
    public static IReadOnlyList<PowerLogEntry> ReadDay(string directory, DateOnly day) =>
        Read(Path.Combine(directory, $"power-{day:yyyyMMdd}.csv"));

    /// <summary>Every day that has a log file, oldest first — the chart's browse list.</summary>
    public static IReadOnlyList<DateOnly> ListDays(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var days = new List<DateOnly>();
        foreach (var file in Directory.GetFiles(directory, "power-*.csv"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            // Archived files are power-YYYYMMDD_stamp.csv — the 8-digit form is the live one.
            if (stem.Length == "power-00000000".Length
                && DateOnly.TryParseExact(stem["power-".Length..], "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                days.Add(day);
        }
        days.Sort();
        return days;
    }

    /// <summary>Parse pre-read lines (header first). Exposed for testing without touching disk.</summary>
    public static IReadOnlyList<PowerLogEntry> Parse(IReadOnlyList<string> lines)
    {
        var header = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (header is null) return [];

        // Strip a UTF-8 BOM so the first column name still matches.
        var index = BuildIndex(header.TrimStart('﻿'));
        var rows = new List<PowerLogEntry>();

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = line.Split(',');
            rows.Add(new PowerLogEntry
            {
                Timestamp = Time(Cell(cells, index, "timestamp")),
                Volts = Num(Cell(cells, index, "volts")),
                Amps = Num(Cell(cells, index, "amps")),
                Watts = Num(Cell(cells, index, "watts")),
            });
        }
        return rows;
    }

    private static Dictionary<string, int> BuildIndex(string header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var names = header.Split(',');
        for (var i = 0; i < names.Length; i++)
            map[names[i].Trim()] = i;
        return map;
    }

    private static string? Cell(string[] cells, Dictionary<string, int> index, string column) =>
        index.TryGetValue(column, out var i) && i < cells.Length ? cells[i].Trim() : null;

    private static double? Num(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateTime? Time(string? s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var v) ? v : null;
}
