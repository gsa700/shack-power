using System.Text;
using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

public class VeDirectParserTests
{
    private static byte[] Body(string s) => Encoding.Latin1.GetBytes(s);

    // ---- block splitting ----

    [Fact]
    public void Splits_labels_and_values_on_the_first_tab()
    {
        Assert.True(VeDirectParser.TryParseBlock(Body("\r\nV\t13960\r\nBMV\tSmartShunt 300A\r\n"), out var f));
        Assert.Equal("13960", f["V"]);
        Assert.Equal("SmartShunt 300A", f["BMV"]);
    }

    [Fact]
    public void Skips_hex_protocol_lines_that_interleave()
    {
        Assert.True(VeDirectParser.TryParseBlock(Body("\r\nV\t13960\r\n:A0002000148\r\nI\t6298\r\n"), out var f));
        Assert.Equal(2, f.Count);
        Assert.False(f.ContainsKey(":A0002000148"));
    }

    [Fact]
    public void Skips_lines_without_a_tab_rather_than_failing_the_block()
    {
        Assert.True(VeDirectParser.TryParseBlock(Body("\r\nnoise\r\nV\t13960\r\n"), out var f));
        Assert.Equal("13960", Assert.Single(f).Value);
    }

    [Fact]
    public void A_block_with_no_fields_is_rejected()
    {
        Assert.False(VeDirectParser.TryParseBlock(Body("\r\n:A0002000148\r\n"), out _));
    }

    [Fact]
    public void A_repeated_label_keeps_the_last_value()
    {
        Assert.True(VeDirectParser.TryParseBlock(Body("\r\nV\t1\r\nV\t2\r\n"), out var f));
        Assert.Equal("2", f["V"]);
    }

    // ---- unit conversions: mV/mA → V/A, 0.01 kWh → kWh, ‰ → %, "---" → null ----

    [Theory]
    [InlineData("13960", 13.96)]
    [InlineData("6298", 6.298)]
    [InlineData("-1450", -1.45)]
    [InlineData("0", 0.0)]
    public void Milli_scales_thousandths(string raw, double expected) =>
        Assert.Equal(expected, VeDirectParser.Milli(raw)!.Value, 6);

    [Theory]
    [InlineData("---")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("junk")]
    public void Milli_is_null_for_unavailable_or_junk(string? raw) =>
        Assert.Null(VeDirectParser.Milli(raw));

    [Theory]
    [InlineData("88", 88.0)]
    [InlineData("-1", -1.0)]
    public void Number_passes_integers_through(string raw, double expected) =>
        Assert.Equal(expected, VeDirectParser.Number(raw)!.Value, 6);

    [Theory]
    [InlineData("225", 2.25)]   // the probe once misread this very value as 22.5 kWh
    [InlineData("0", 0.0)]
    public void CentiKwh_scales_hundredths(string raw, double expected) =>
        Assert.Equal(expected, VeDirectParser.CentiKwh(raw)!.Value, 6);

    [Theory]
    [InlineData("872", 87.2)]
    [InlineData("1000", 100.0)]
    public void Permille_scales_to_percent(string raw, double expected) =>
        Assert.Equal(expected, VeDirectParser.Permille(raw)!.Value, 6);

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("-1", -1)]
    public void Int_parses_signed_integers(string raw, int expected) =>
        Assert.Equal(expected, VeDirectParser.Int(raw));

    [Fact]
    public void Int_is_null_for_unavailable() => Assert.Null(VeDirectParser.Int("---"));

    // ---- alarm reason decode ----

    [Theory]
    [InlineData(1, "low voltage")]
    [InlineData(2, "high voltage")]
    [InlineData(3, "low voltage, high voltage")]
    [InlineData(64, "high temperature")]
    [InlineData(256, "unknown")]   // a future firmware bit must not read as silence
    public void Alarm_reasons_decode(int mask, string expected) =>
        Assert.Equal(expected, PowerReading.DescribeAlarm(mask));
}
