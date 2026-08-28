using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

public class RegistrationLogTests
{
    private static RegistrationAttempt Sample(bool ok = true, string version = "0.7.1-beta",
        string detail = "reg import exit 0") =>
        new(new DateTime(2026, 7, 31, 22, 53, 14, DateTimeKind.Utc), version, "update", ok, detail);

    [Fact]
    public void RoundTripsThroughFormatAndParse()
    {
        var a = Sample();
        var back = RegistrationLog.Parse(RegistrationLog.Format(a));
        Assert.Equal(a, back);
    }

    [Fact]
    public void RoundTripsAFailure()
    {
        var a = Sample(ok: false, detail: "reg import exit 1");
        var back = RegistrationLog.Parse(RegistrationLog.Format(a));
        Assert.NotNull(back);
        Assert.False(back!.Value.Succeeded);
        Assert.Equal("reg import exit 1", back.Value.Detail);
    }

    [Fact]
    public void ATabInTheDetailCannotShiftTheColumns()
    {
        // The detail is free text built from exception messages, so it is the field most likely to
        // carry a separator. Losing column alignment would misreport every field after it.
        var a = Sample(detail: "threw\tIOException\there");
        var back = RegistrationLog.Parse(RegistrationLog.Format(a));
        Assert.NotNull(back);
        Assert.Equal("0.7.1-beta", back!.Value.Version);
        Assert.Equal("update", back.Value.Trigger);
        Assert.True(back.Value.Succeeded);
    }

    [Fact]
    public void ANewlineInTheDetailCannotForgeASecondEntry()
    {
        // One attempt is one line; a newline would read back as an extra, invented attempt.
        var line = RegistrationLog.Format(Sample(detail: "threw\nIOException"));
        Assert.DoesNotContain("\n", line);
        Assert.DoesNotContain("\r", line);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a log line")]
    [InlineData("2026-07-31T22:53:14Z\t0.7.1-beta")]      // truncated mid-write
    [InlineData("never\tmind\tthe\tdate\there")]           // unparseable timestamp
    public void UnparseableLinesAreNullRatherThanThrowing(string? line) =>
        Assert.Null(RegistrationLog.Parse(line));

    [Fact]
    public void TailKeepsTheMostRecent()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"line {i}");
        var kept = RegistrationLog.Tail(lines, 5);
        Assert.Equal(5, kept.Count);
        Assert.Equal("line 26", kept[0]);
        Assert.Equal("line 30", kept[4]);
    }

    [Fact]
    public void TailLeavesAShortLogAloneAndDropsBlanks()
    {
        var kept = RegistrationLog.Tail(["a", "", "  ", "b"], 5);
        Assert.Equal(["a", "b"], kept);
    }

    [Fact]
    public void DescribeSaysSoWhenNothingHasRun() =>
        Assert.Contains("not yet checked", RegistrationLog.Describe(null, "0.7.1-beta"));

    [Fact]
    public void DescribeReportsAFailureWithItsDetail()
    {
        var s = RegistrationLog.Describe(Sample(ok: false, detail: "reg import exit 1"), "0.7.1-beta");
        Assert.Contains("FAILED", s);
        Assert.Contains("reg import exit 1", s);
    }

    [Fact]
    public void DescribeCallsOutAnEntryLeftOnAnOlderVersion()
    {
        // The reported fault exactly: the attempt that should have refreshed the entry never ran, so
        // the newest successful attempt on record is an older version's. A plain "ok" would hide it.
        var s = RegistrationLog.Describe(Sample(version: "0.6.2-beta"), "0.7.1-beta");
        Assert.Contains("0.6.2-beta", s);
        Assert.Contains("0.7.1-beta", s);
    }

    [Fact]
    public void DescribeConfirmsAMatchingVersion() =>
        Assert.Contains("up to date", RegistrationLog.Describe(Sample(), "0.7.1-beta"));
}
