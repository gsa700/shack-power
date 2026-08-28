using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

public class PowerLogReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "shackpower-logreader-tests", Guid.NewGuid().ToString("N"));

    // Verbatim from the prototype's real file on this station (power-20260828.csv head).
    private static readonly string[] PrototypeFixture =
    [
        "timestamp,volts,amps,watts",
        "2026-08-28T16:53:18,13.962,6.303,88.0",
        "2026-08-28T16:53:19,13.962,6.301,88.0",
        "2026-08-28T16:53:20,13.961,6.294,88.0",
    ];

    [Fact]
    public void Reads_a_prototype_file_verbatim()
    {
        var rows = PowerLogReader.Parse(PrototypeFixture);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new DateTime(2026, 8, 28, 16, 53, 18), rows[0].Timestamp);
        Assert.Equal(13.962, rows[0].Volts!.Value, 6);
        Assert.Equal(6.303, rows[0].Amps!.Value, 6);
        Assert.Equal(88.0, rows[0].Watts!.Value, 6);
    }

    [Fact]
    public void Columns_resolve_by_header_name_not_position()
    {
        var rows = PowerLogReader.Parse(
        [
            "watts,timestamp,volts,amps",
            "88.0,2026-08-28T16:53:18,13.962,6.303",
        ]);
        Assert.Equal(13.962, rows[0].Volts!.Value, 6);
        Assert.Equal(88.0, rows[0].Watts!.Value, 6);
    }

    [Fact]
    public void Unknown_columns_are_ignored_and_missing_ones_read_null()
    {
        var rows = PowerLogReader.Parse(
        [
            "timestamp,volts,extra",
            "2026-08-28T16:53:18,13.962,junk",
        ]);
        Assert.Equal(13.962, rows[0].Volts!.Value, 6);
        Assert.Null(rows[0].Amps);
    }

    [Fact]
    public void Empty_cells_read_as_null()
    {
        var rows = PowerLogReader.Parse(["timestamp,volts,amps,watts", "2026-08-28T16:53:18,,,"]);
        Assert.Null(rows[0].Volts);
        Assert.NotNull(rows[0].Timestamp);
    }

    [Fact]
    public void A_bom_on_the_header_does_not_break_the_first_column()
    {
        var rows = PowerLogReader.Parse(["﻿timestamp,volts,amps,watts", "2026-08-28T16:53:18,1.0,2.0,3.0"]);
        Assert.NotNull(rows[0].Timestamp);
    }

    [Fact]
    public void A_missing_file_reads_as_empty_not_an_error() =>
        Assert.Empty(PowerLogReader.Read(Path.Combine(_dir, "power-20260101.csv")));

    [Fact]
    public void ListDays_finds_live_files_and_skips_archives()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "power-20260827.csv"), "");
        File.WriteAllText(Path.Combine(_dir, "power-20260828.csv"), "");
        File.WriteAllText(Path.Combine(_dir, "power-20260828_20260828120000.csv"), "");  // archive
        File.WriteAllText(Path.Combine(_dir, "unrelated.csv"), "");

        var days = PowerLogReader.ListDays(_dir);
        Assert.Equal([new DateOnly(2026, 8, 27), new DateOnly(2026, 8, 28)], days);
    }

    [Fact]
    public void ListDays_of_a_missing_directory_is_empty() =>
        Assert.Empty(PowerLogReader.ListDays(Path.Combine(_dir, "nope")));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
