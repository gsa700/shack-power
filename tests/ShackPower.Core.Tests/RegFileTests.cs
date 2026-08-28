using ShackPower.Core;
using Xunit;

namespace ShackPower.Core.Tests;

public class RegFileTests
{
    private const string Key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\ShackPower";

    [Fact]
    public void StartsWithTheRequiredHeaderAndTheKeyInBrackets()
    {
        var s = RegFile.Build(Key, [RegFile.Sz("DisplayName", "Shack Power")]);
        Assert.StartsWith(RegFile.Header, s);
        Assert.Contains($"[{Key}]", s);
    }

    [Fact]
    public void AValueIsAQuotedNameEqualsItsLiteral()
    {
        var s = RegFile.Build(Key, [RegFile.Sz("DisplayName", "Shack Power")]);
        Assert.Contains("\"DisplayName\"=\"Shack Power\"", s);
    }

    [Fact]
    public void BackslashesInAPathAreDoubled()
    {
        // Every path this file carries is a Windows path, so this is the rule that matters most.
        var s = RegFile.Sz("InstallLocation", @"C:\Users\ab0r\AppData\Local\Programs\Shack Power").Literal;
        Assert.Equal("\"C:\\\\Users\\\\ab0r\\\\AppData\\\\Local\\\\Programs\\\\Shack Power\"", s);
    }

    [Fact]
    public void EmbeddedQuotesAreEscaped()
    {
        // UninstallString is a quoted exe path followed by a switch — the value most likely to break
        // a naive writer, and the one that made the old per-value reg.exe approach fragile.
        var exe = @"C:\Program Files\Shack Power\ShackPower.exe";
        var s = RegFile.Sz("UninstallString", $"\"{exe}\" --uninstall").Literal;
        Assert.Equal("\"\\\"C:\\\\Program Files\\\\Shack Power\\\\ShackPower.exe\\\" --uninstall\"", s);
    }

    [Fact]
    public void ANewlineInAValueCannotForgeAnotherLine()
    {
        // A value spans exactly one line; a stray newline would make the remainder read as a
        // malformed line, and reg import rejects the whole file rather than the one value.
        var s = RegFile.Build(Key, [RegFile.Sz("DisplayName", "Shack\r\nPower")]);
        var valueLines = s.Split("\r\n").Where(l => l.StartsWith("\"")).ToList();
        Assert.Single(valueLines);
        Assert.Contains("\"DisplayName\"=\"Shack  Power\"", s);
    }

    [Theory]
    [InlineData(0, "dword:00000000")]
    [InlineData(1, "dword:00000001")]
    [InlineData(101831, "dword:00018dc7")]   // a real EstimatedSize, in KB
    public void DwordsAreEightLowerCaseHexDigits(long value, string expected)
    {
        // reg import is strict about the width and rejects a short one rather than padding it.
        Assert.Equal(expected, RegFile.Dword("EstimatedSize", value).Literal);
    }

    [Fact]
    public void EveryLineIsTheHeaderTheKeyAValueOrBlank()
    {
        var s = RegFile.Build(Key,
        [
            RegFile.Sz("DisplayName", "Shack Power"),
            RegFile.Sz("UninstallString", "\"C:\\x\\ShackPower.exe\" --uninstall"),
            RegFile.Dword("NoModify", 1),
        ]);

        foreach (var line in s.Split("\r\n"))
        {
            var ok = line.Length == 0
                     || line == RegFile.Header
                     || (line.StartsWith('[') && line.EndsWith(']'))
                     || (line.StartsWith('"') && line.Contains('='));
            Assert.True(ok, $"stray line: {line}");
        }
    }

    [Fact]
    public void UsesCrlfSoTheFileReadsAsDosText()
    {
        var s = RegFile.Build(Key, [RegFile.Sz("DisplayName", "Shack Power")]);
        Assert.DoesNotContain(s.Replace("\r\n", ""), "\n");
    }
}
