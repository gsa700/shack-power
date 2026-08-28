using System.Globalization;
using System.Text;

namespace ShackPower.Core;

/// <summary>
/// Decodes one checksum-verified VE.Direct block body into label→value pairs, plus the unit
/// conversions for the fields this app uses. Null-tolerant throughout: "---" (the device's
/// "not available") and anything unparseable become null rather than an exception — everything
/// off the wire is untrusted, and a junk field must never tear down the read session.
/// </summary>
public static class VeDirectParser
{
    /// <summary>
    /// Split a verified block body into fields. Lines starting <c>:</c> are asynchronous
    /// HEX-protocol messages that may interleave with the text stream — skipped here (the framer
    /// already accounted for their bytes in the checksum). Returns false for a block with no
    /// <c>label\tvalue</c> lines at all.
    /// </summary>
    public static bool TryParseBlock(byte[] body, out Dictionary<string, string> fields)
    {
        fields = new Dictionary<string, string>(StringComparer.Ordinal);
        // Latin-1: a byte-preserving decode. The payload is ASCII, but a stray high byte must map
        // to *some* char rather than a multi-byte UTF-8 failure that shifts everything after it.
        foreach (var line in Encoding.Latin1.GetString(body).Split("\r\n"))
        {
            if (line.Length == 0 || line[0] == ':') continue;
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            fields[line[..tab]] = line[(tab + 1)..];
        }
        return fields.Count > 0;
    }

    /// <summary>mV/mA/mAh → V/A/Ah ("V", "I", "CE").</summary>
    public static double? Milli(string? raw) => Int(raw) / 1000.0;

    /// <summary>Plain integer field as a double ("P" watts, "TTG" minutes).</summary>
    public static double? Number(string? raw) => Int(raw);

    /// <summary>0.01 kWh → kWh ("H17", "H18").</summary>
    public static double? CentiKwh(string? raw) => Int(raw) / 100.0;

    /// <summary>‰ → percent ("SOC": 872 means 87.2%).</summary>
    public static double? Permille(string? raw) => Int(raw) / 10.0;

    /// <summary>Integer field, or null for missing / "---" / junk ("AR", "MON").</summary>
    public static int? Int(string? raw) =>
        int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)
            ? v : null;
}
