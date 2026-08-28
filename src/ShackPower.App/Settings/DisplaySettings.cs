using ShackPower.App.ViewModels;

namespace ShackPower.App.Settings;

/// <summary>
/// Observable "what to show / how to behave" settings. Setup toggles them, the main window
/// binds row visibility and thresholds to them, and config persists them via DisplayConfig.
/// </summary>
public sealed class DisplaySettings : ViewModelBase
{
    private bool _showExtremes = true;
    /// <summary>The min/max voltage row (device history H7/H8).</summary>
    public bool ShowExtremes { get => _showExtremes; set => SetProperty(ref _showExtremes, value); }

    private bool _showEnergy = true;
    /// <summary>The cumulative kWh row (H17/H18).</summary>
    public bool ShowEnergy { get => _showEnergy; set => SetProperty(ref _showEnergy, value); }

    private bool _showBattery = true;
    /// <summary>The SOC / time-to-go row. Only rendered when the shunt actually reports SOC
    /// (battery-monitor mode) — this station's DC-meter shunt never does.</summary>
    public bool ShowBattery { get => _showBattery; set => SetProperty(ref _showBattery, value); }

    private bool _showDevice = true;
    /// <summary>The device/firmware row ("SmartShunt 300A · FW 0419").</summary>
    public bool ShowDevice { get => _showDevice; set => SetProperty(ref _showDevice, value); }

    private bool _alwaysOnTop;
    public bool AlwaysOnTop { get => _alwaysOnTop; set => SetProperty(ref _alwaysOnTop, value); }

    private bool _minimizeToTray;
    /// <summary>Minimizing hides to the system tray instead of the taskbar (Phase 4).</summary>
    public bool MinimizeToTray { get => _minimizeToTray; set => SetProperty(ref _minimizeToTray, value); }

    // Voltage coloring thresholds (prototype's defaults). Warn turns the volts figure orange;
    // the alarm bounds turn it red. These color the display only — the SmartShunt's own alarm
    // relay/bitmask is independent and always honored.
    private double _voltLowWarn = 12.0;
    public double VoltLowWarn { get => _voltLowWarn; set => SetProperty(ref _voltLowWarn, Math.Clamp(value, 0, 60)); }

    private double _voltLowAlarm = 11.5;
    public double VoltLowAlarm { get => _voltLowAlarm; set => SetProperty(ref _voltLowAlarm, Math.Clamp(value, 0, 60)); }

    private double _voltHighAlarm = 14.8;
    public double VoltHighAlarm { get => _voltHighAlarm; set => SetProperty(ref _voltHighAlarm, Math.Clamp(value, 0, 60)); }
}
