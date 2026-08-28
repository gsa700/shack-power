using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

/// <summary>
/// Pins byte compatibility with the Python prototype's CSV format. The fixture rows in
/// <see cref="A_row_matches_the_prototypes_output_byte_for_byte"/> are copied verbatim from the
/// real file the prototype wrote on this station (power-20260828.csv) — at cutover this app
/// continues that day's file, so its rows must be indistinguishable.
/// </summary>
public class PowerLogRecordTests
{
    [Fact]
    public void Header_matches_the_prototype() =>
        Assert.Equal("timestamp,volts,amps,watts", PowerLogRecord.CsvHeader);

    [Theory]
    // Verbatim rows from the prototype's real output:
    [InlineData("2026-08-28T16:53:18,13.962,6.303,88.0", 13.962, 6.303, 88.0)]
    [InlineData("2026-08-28T16:53:20,13.961,6.294,88.0", 13.961, 6.294, 88.0)]
    public void A_row_matches_the_prototypes_output_byte_for_byte(
        string expected, double v, double a, double w)
    {
        var ts = DateTime.ParseExact(expected.Split(',')[0], "yyyy-MM-dd'T'HH:mm:ss", null);
        var row = new PowerLogRecord { Timestamp = ts, Volts = v, Amps = a, Watts = w }.ToCsvRow();
        Assert.Equal(expected, row);
    }

    [Fact]
    public void Integral_values_keep_one_decimal_like_python_str()
    {
        // Python str(88.0) is "88.0", never "88" — the fixture above already covers watts, this
        // pins the rule for the other columns too.
        var row = new PowerLogRecord
        {
            Timestamp = new DateTime(2026, 8, 28, 12, 0, 0),
            Volts = 13.0, Amps = -2.0, Watts = 0.0,
        }.ToCsvRow();
        Assert.Equal("2026-08-28T12:00:00,13.0,-2.0,0.0", row);
    }

    [Fact]
    public void Null_writes_an_empty_cell_like_python_none()
    {
        var row = new PowerLogRecord { Timestamp = new DateTime(2026, 8, 28, 12, 0, 0) }.ToCsvRow();
        Assert.Equal("2026-08-28T12:00:00,,,", row);
    }

    [Fact]
    public void Negative_fractions_round_trip()
    {
        var row = new PowerLogRecord
        {
            Timestamp = new DateTime(2026, 8, 28, 12, 0, 0),
            Volts = 12.845, Amps = -0.31, Watts = -4.0,
        }.ToCsvRow();
        Assert.Equal("2026-08-28T12:00:00,12.845,-0.31,-4.0", row);
    }
}
