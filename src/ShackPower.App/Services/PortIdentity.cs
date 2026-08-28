using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using ShackPower.Core;

namespace ShackPower.App.Services;

/// <summary>
/// Pins a USB-serial adapter to a stable identity instead of its volatile device name, so the
/// VE.Direct cable keeps its identity when the OS renumbers ports (every COM number on this
/// station changed across a Windows reinstall once). The whole thing is expressed as one map
/// {currentPortName -> stableId}; SerialFor/ResolvePort then work identically on every OS.
///
/// - Windows: id = FTDI/USB chip serial (WMI, extracted by the tested Core
///   <see cref="UsbSerial"/> — kept there rather than here so the regexes have unit tests).
/// - Linux/Pi: id = the /dev/serial/by-id/* name (stable per cable); port = the /dev/tty* it
///   currently links to.
/// - macOS/other: no map (graceful fallback to the saved port name).
/// </summary>
public static class PortIdentity
{
    private static readonly Regex ComName = new(@"\((COM\d+)\)", RegexOptions.Compiled);

    private const string ByIdDir = "/dev/serial/by-id";

    /// <summary>Current port name -> stable cable id, for every port that has one.</summary>
    public static Dictionary<string, string> GetMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (OperatingSystem.IsWindows()) PopulateWindows(map);
            else if (OperatingSystem.IsLinux()) PopulateLinux(map);
        }
        catch { /* WMI/filesystem unavailable — fall back to saved port name */ }
        return map;
    }

    /// <summary>
    /// Each /dev/serial/by-id/* entry is a stable symlink to the volatile /dev/ttyUSB*|ttyACM*.
    /// Map {resolved tty -> by-id name} so the by-id name pins the cable across renumbering.
    /// </summary>
    private static void PopulateLinux(Dictionary<string, string> map)
    {
        if (!Directory.Exists(ByIdDir)) return;
        foreach (var link in Directory.GetFileSystemEntries(ByIdDir))
        {
            // Guard each entry: a dangling symlink (or a device torn down mid-enumeration) makes
            // ResolveLinkTarget throw. Without this, one bad entry would abort the whole loop and
            // drop every cable after it from the map.
            try
            {
                var target = File.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName;
                if (target is not null) map[target] = System.IO.Path.GetFileName(link);
            }
            catch { /* skip this entry, keep the rest */ }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void PopulateWindows(Dictionary<string, string> map)
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass='Ports'");
        foreach (ManagementBaseObject o in searcher.Get())
        {
            if (o["Name"] is not string name || o["PNPDeviceID"] is not string pnp) continue;
            var cm = ComName.Match(name);
            if (!cm.Success) continue;
            if (UsbSerial.Extract(pnp) is { } serial) map[cm.Groups[1].Value] = serial;
        }
    }

    /// <summary>Serial of the adapter currently on <paramref name="port"/>, or null.</summary>
    public static string? SerialFor(string port) => GetMap().TryGetValue(port, out var s) ? s : null;

    /// <summary>The port that currently hosts <paramref name="serial"/>; falls back to savedPort.</summary>
    public static string? ResolvePort(string? savedPort, string? serial)
    {
        if (string.IsNullOrEmpty(serial)) return savedPort;
        foreach (var kv in GetMap())
            if (string.Equals(kv.Value, serial, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        return savedPort;
    }
}
