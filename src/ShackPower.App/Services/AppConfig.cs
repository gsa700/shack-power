using System.Text.Json;
using ShackPower.App.Settings;
using ShackPower.Core;

namespace ShackPower.App.Services;

/// <summary>Persisted state: window bounds, the connection identity, display flags, and misc.</summary>
public sealed class AppConfig
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? SetupX { get; set; }
    public double? SetupY { get; set; }
    public double? ChartX { get; set; }
    public double? ChartY { get; set; }
    public double? ChartW { get; set; }
    public double? ChartH { get; set; }

    /// <summary>Chart window presentation: true = all channels overlaid on one plot.</summary>
    public bool ChartCombined { get; set; }

    /// <summary>Which Setup tab was showing, so it reopens where it was left. Clamped on load.</summary>
    public int SetupTab { get; set; }

    /// <summary>Saved port plus the cable's chip serial (VE.Direct FTDI, e.g. VEAUI3T2A) — the
    /// serial is the identity, the port just the last place it was seen.</summary>
    public string? Port { get; set; }
    public string? Serial { get; set; }

    public bool LogEnabled { get; set; } = true;
    public bool CheckUpdatesAtStartup { get; set; }
    public DisplayConfig Display { get; set; } = new();

    public void ApplyTo(DisplaySettings d)
    {
        d.ShowExtremes = Display.ShowExtremes;
        d.ShowEnergy = Display.ShowEnergy;
        d.ShowBattery = Display.ShowBattery;
        d.ShowDevice = Display.ShowDevice;
        d.AlwaysOnTop = Display.AlwaysOnTop;
        d.MinimizeToTray = Display.MinimizeToTray;
        d.VoltLowWarn = Display.VoltLowWarn;
        d.VoltLowAlarm = Display.VoltLowAlarm;
        d.VoltHighAlarm = Display.VoltHighAlarm;
    }

    public void CaptureFrom(DisplaySettings d)
    {
        Display.ShowExtremes = d.ShowExtremes;
        Display.ShowEnergy = d.ShowEnergy;
        Display.ShowBattery = d.ShowBattery;
        Display.ShowDevice = d.ShowDevice;
        Display.AlwaysOnTop = d.AlwaysOnTop;
        Display.MinimizeToTray = d.MinimizeToTray;
        Display.VoltLowWarn = d.VoltLowWarn;
        Display.VoltLowAlarm = d.VoltLowAlarm;
        Display.VoltHighAlarm = d.VoltHighAlarm;
    }
}

/// <summary>Plain (serializable) mirror of <see cref="DisplaySettings"/>.</summary>
public sealed class DisplayConfig
{
    public bool ShowExtremes { get; set; } = true;
    public bool ShowEnergy { get; set; } = true;
    public bool ShowBattery { get; set; } = true;
    public bool ShowDevice { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool MinimizeToTray { get; set; }
    public double VoltLowWarn { get; set; } = 12.0;
    public double VoltLowAlarm { get; set; } = 11.5;
    public double VoltHighAlarm { get; set; } = 14.8;
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Where this app keeps its settings and logs: <c>%AppData%\ShackPower</c> on Windows, the XDG
    /// equivalent elsewhere. Public because uninstall has to be able to offer to remove what's in
    /// here — and it removes the files it names, never this directory wholesale.
    /// </summary>
    public static string DataDir
    {
        get
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ShackPower");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConfigFilePath => System.IO.Path.Combine(DataDir, "config.json");

    /// <summary>Daily power CSVs live here — <c>power-YYYYMMDD.csv</c>, byte-compatible with the
    /// Python prototype's files so its history carries straight over.</summary>
    public static string LogDir
    {
        get
        {
            var dir = System.IO.Path.Combine(DataDir, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string Path => ConfigFilePath;

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path)) ?? new AppConfig();
        }
        catch
        {
            // The file exists but couldn't be read/parsed. Preserve it as config.json.bak instead
            // of silently running with defaults that the next Save would overwrite it with — which
            // would lose the cable's serial pinning with no recovery path.
            AtomicFile.Backup(Path);
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        // Atomic (temp + rename): a crash mid-write must not truncate config.json to nothing.
        try { AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(config, Options)); }
        catch { /* best effort */ }
    }
}
