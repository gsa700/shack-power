using System.Globalization;
using System.Text;

namespace ShackPower.Core;

/// <summary>
/// Appends 1 Hz readings to daily CSVs (<c>power-YYYYMMDD.csv</c>) in one directory. Adapted
/// from LP-100A's <c>TxLogWriter</c>: the rolling row cap is replaced by daily rotation (a day
/// of 1 Hz rows is ~85 KB — nothing worth trimming), while the two policies worth keeping are
/// kept verbatim — an existing file whose header doesn't match the current schema is archived
/// aside so a schema change never mixes into old data, and nothing here ever deletes a row.
///
/// Rotation follows the <b>record's own timestamp</b>, not a clock — the row for 23:59:59
/// belongs in that day's file even if it is written a moment after midnight, and it makes
/// rotation deterministically testable. The injected clock only stamps archive names.
///
/// Each append opens/writes/closes (unlike the prototype's held-open handle): at 1 Hz that costs
/// nothing and means a crash can lose at most the row being written. Thin IO, not thread-safe;
/// IO exceptions propagate — the caller surfaces them without killing the display.
/// </summary>
public sealed class PowerLogWriter
{
    private readonly string _directory;
    private readonly Func<DateTime> _clock;

    public PowerLogWriter(string directory, Func<DateTime>? clock = null)
    {
        _directory = directory;
        _clock = clock ?? (() => DateTime.Now);
    }

    public string Directory => _directory;

    /// <summary>The file a given day's rows live in.</summary>
    public string PathFor(DateOnly day) =>
        System.IO.Path.Combine(_directory, $"power-{day:yyyyMMdd}.csv");

    /// <summary>Append one reading to its day's file, creating file + header as needed.</summary>
    public void Append(PowerLogRecord record)
    {
        System.IO.Directory.CreateDirectory(_directory);
        var path = PathFor(DateOnly.FromDateTime(record.Timestamp));
        EnsureHeader(path);
        File.AppendAllText(path, record.ToCsvRow() + Environment.NewLine, Encoding.UTF8);
    }

    private void EnsureHeader(string path)
    {
        if (File.Exists(path))
        {
            var first = File.ReadLines(path, Encoding.UTF8).FirstOrDefault()?.TrimStart('﻿');
            if (first is not null && first != PowerLogRecord.CsvHeader)
                ArchiveAside(path);
        }
        // A file that exists with the right header is continued as-is — this is what lets the
        // app pick up the prototype's same-day file at cutover without a duplicate header.
        if (!File.Exists(path))
            File.WriteAllText(path, PowerLogRecord.CsvHeader + Environment.NewLine, Encoding.UTF8);
    }

    private void ArchiveAside(string path)
    {
        var stamp = _clock().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var dir = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var ext = System.IO.Path.GetExtension(path);

        // Uniquify: the stamp is per-second, so two archives inside one second would otherwise
        // overwrite each other — quietly losing the data the archive exists to preserve.
        var target = System.IO.Path.Combine(dir, $"{name}_{stamp}{ext}");
        for (var n = 2; File.Exists(target); n++)
            target = System.IO.Path.Combine(dir, $"{name}_{stamp}-{n}{ext}");

        File.Move(path, target);
    }
}
