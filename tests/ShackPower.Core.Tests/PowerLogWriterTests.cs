using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

public class PowerLogWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "shackpower-logwriter-tests", Guid.NewGuid().ToString("N"));

    private static PowerLogRecord At(DateTime ts, double v = 13.5) =>
        new() { Timestamp = ts, Volts = v, Amps = 1.0, Watts = 13.5 };

    [Fact]
    public void First_append_creates_the_file_with_one_header()
    {
        var w = new PowerLogWriter(_dir);
        w.Append(At(new DateTime(2026, 8, 28, 10, 0, 0)));
        var lines = File.ReadAllLines(w.PathFor(new DateOnly(2026, 8, 28)));
        Assert.Equal(2, lines.Length);
        Assert.Equal(PowerLogRecord.CsvHeader, lines[0]);
    }

    [Fact]
    public void Appending_to_an_existing_file_adds_no_second_header()
    {
        // The cutover case: the prototype started today's file; this app must continue it.
        Directory.CreateDirectory(_dir);
        var w = new PowerLogWriter(_dir);
        var path = w.PathFor(new DateOnly(2026, 8, 28));
        File.WriteAllText(path,
            PowerLogRecord.CsvHeader + Environment.NewLine
            + "2026-08-28T09:59:59,13.9,6.3,88.0" + Environment.NewLine);

        w.Append(At(new DateTime(2026, 8, 28, 10, 0, 0)));

        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.Single(lines, l => l == PowerLogRecord.CsvHeader);
    }

    [Fact]
    public void Rotation_follows_the_records_own_timestamp()
    {
        var w = new PowerLogWriter(_dir);
        w.Append(At(new DateTime(2026, 8, 28, 23, 59, 59)));
        w.Append(At(new DateTime(2026, 8, 29, 0, 0, 0)));

        Assert.True(File.Exists(w.PathFor(new DateOnly(2026, 8, 28))));
        Assert.True(File.Exists(w.PathFor(new DateOnly(2026, 8, 29))));
        Assert.Equal(2, File.ReadAllLines(w.PathFor(new DateOnly(2026, 8, 28))).Length);
        Assert.Equal(2, File.ReadAllLines(w.PathFor(new DateOnly(2026, 8, 29))).Length);
    }

    [Fact]
    public void A_file_with_a_foreign_header_is_archived_aside_not_overwritten()
    {
        Directory.CreateDirectory(_dir);
        var w = new PowerLogWriter(_dir, () => new DateTime(2026, 8, 28, 12, 0, 0));
        var path = w.PathFor(new DateOnly(2026, 8, 28));
        File.WriteAllText(path, "some,other,schema" + Environment.NewLine + "1,2,3" + Environment.NewLine);

        w.Append(At(new DateTime(2026, 8, 28, 12, 0, 1)));

        var archived = Path.Combine(_dir, "power-20260828_20260828120000.csv");
        Assert.True(File.Exists(archived));                      // old data preserved…
        Assert.Contains("some,other,schema", File.ReadAllText(archived));
        Assert.Equal(PowerLogRecord.CsvHeader, File.ReadLines(path).First());   // …fresh file current
    }

    [Fact]
    public void Two_archives_in_one_second_do_not_overwrite_each_other()
    {
        Directory.CreateDirectory(_dir);
        var w = new PowerLogWriter(_dir, () => new DateTime(2026, 8, 28, 12, 0, 0));
        var path = w.PathFor(new DateOnly(2026, 8, 28));

        for (var i = 0; i < 2; i++)
        {
            File.WriteAllText(path, $"schema{i}" + Environment.NewLine);
            w.Append(At(new DateTime(2026, 8, 28, 12, 0, 1)));
            File.Delete(path);   // force the next round to re-create then re-archive
        }

        Assert.True(File.Exists(Path.Combine(_dir, "power-20260828_20260828120000.csv")));
        Assert.True(File.Exists(Path.Combine(_dir, "power-20260828_20260828120000-2.csv")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
