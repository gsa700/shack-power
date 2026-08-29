using Avalonia.Media;
using ShackPower.App.Services;
using ShackPower.App.Settings;
using ShackPower.Core;

namespace ShackPower.App.ViewModels;

/// <summary>
/// Main readout: three white cards (volts / amps / watts) over the Victron-blue ground, with
/// the secondary rows below. One private <see cref="Render"/> recomputes every bound property
/// from the latest reading (the family's LP-100A shape — single meter, no manager).
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly MeterService _meter;
    private PowerReading? _last;

    public MainWindowViewModel(MeterService meter, DisplaySettings display)
    {
        _meter = meter;
        Display = display;

        Display.PropertyChanged += (_, e) =>
        {
            // Threshold moves recolor the volts figure without waiting for the next reading.
            if (e.PropertyName is nameof(DisplaySettings.VoltLowWarn)
                or nameof(DisplaySettings.VoltLowAlarm)
                or nameof(DisplaySettings.VoltHighAlarm) && _last is not null)
                Render(_last);
            if (e.PropertyName is nameof(DisplaySettings.ShowBattery))
                OnPropertyChanged(nameof(BatteryRowVisible));
        };

        _meter.ReadingReceived += Render;
        _meter.StateChanged += OnStateChanged;
        OnStateChanged();
    }

    public DisplaySettings Display { get; }

    // Not the app name — the title bar already says that right above. This header names what
    // the numbers are: the DC side of the station.
    public string TitleText => "DC POWER";

    /// <summary>Dim the readouts when the feed goes stale so frozen values don't read as live.</summary>
    public double ReadoutOpacity => _meter is { IsConnected: true, IsStale: true } ? 0.55 : 1.0;

    // --- the three cards ---
    private string _voltsText = "--";
    public string VoltsText { get => _voltsText; private set => SetProperty(ref _voltsText, value); }

    private IBrush _voltsBrush = Palette.CardTextBrush;
    public IBrush VoltsBrush { get => _voltsBrush; private set => SetProperty(ref _voltsBrush, value); }

    private string _ampsText = "--";
    public string AmpsText { get => _ampsText; private set => SetProperty(ref _ampsText, value); }

    private string _wattsText = "--";
    public string WattsText { get => _wattsText; private set => SetProperty(ref _wattsText, value); }

    // --- secondary rows ---
    private string _minMaxText = "--";
    public string MinMaxText { get => _minMaxText; private set => SetProperty(ref _minMaxText, value); }

    private string _energyText = "--";
    public string EnergyText { get => _energyText; private set => SetProperty(ref _energyText, value); }

    private string _batteryText = "--";
    public string BatteryText { get => _batteryText; private set => SetProperty(ref _batteryText, value); }

    private bool _hasBattery;
    /// <summary>SOC row only exists when the shunt reports SOC at all — this station's DC-meter
    /// shunt never does, and an always-"--" row would just look broken.</summary>
    public bool BatteryRowVisible => Display.ShowBattery && _hasBattery;

    private string _deviceText = "--";
    public string DeviceText { get => _deviceText; private set => SetProperty(ref _deviceText, value); }

    // --- alarm + status ---
    private bool _alarmVisible;
    public bool AlarmVisible { get => _alarmVisible; private set => SetProperty(ref _alarmVisible, value); }

    private string _alarmText = "";
    public string AlarmText { get => _alarmText; private set => SetProperty(ref _alarmText, value); }

    private string _statusText = "Disconnected";
    /// <summary>The connection dot's hover tooltip — the only place the main window spells the
    /// connection out; the full picture lives in Setup.</summary>
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private IBrush _connDotBrush = Palette.DimBrush;
    public IBrush ConnDotBrush { get => _connDotBrush; private set => SetProperty(ref _connDotBrush, value); }

    private void OnStateChanged()
    {
        var stale = _meter is { IsConnected: true, IsStale: true };
        StatusText = stale ? $"{_meter.Status} — no data" : _meter.Status;
        // Soft tints on the blue ground; full-strength red/green vibrate against it.
        ConnDotBrush = _meter.StatusIsError ? Palette.RedSoftBrush
            : _meter is { IsConnected: true, IsStale: false, Current: not null } ? Palette.GreenSoftBrush
            : _meter.IsConnected ? Palette.OrangeBrush : Palette.DimBrush;
        OnPropertyChanged(nameof(ReadoutOpacity));
        if (!_meter.IsConnected) BlankReadouts();
    }

    private void Render(PowerReading r)
    {
        _last = r;
        ConnDotBrush = Palette.GreenSoftBrush;

        VoltsText = r.Volts is { } v ? $"{v:0.00}" : "--";
        AmpsText = r.Amps is { } a ? $"{a:+0.00;-0.00;0.00}" : "--";
        WattsText = r.Watts is { } w ? $"{w:0}" : "--";

        VoltsBrush = r.Volts switch
        {
            null => Palette.CardTextBrush,
            { } x when x < Display.VoltLowAlarm || x > Display.VoltHighAlarm => Palette.RedBrush,
            { } x when x < Display.VoltLowWarn => Palette.OrangeDeepBrush,
            _ => Palette.CardTextBrush,
        };

        MinMaxText = r is { VminHistory: { } lo, VmaxHistory: { } hi } ? $"{lo:0.00} / {hi:0.00} V" : "--";
        EnergyText = r is { TotalKwhDrawn: { } d, TotalKwhCharged: { } c }
            ? $"{d:0.00} / {c:0.00} kWh" : "--";

        _hasBattery = r.Soc is not null;
        OnPropertyChanged(nameof(BatteryRowVisible));
        if (r.Soc is { } soc)
        {
            // Long TTG reads better in days; h:mm past ~4 days is a wall of digits.
            var ttg = r.TtgMinutes switch
            {
                null => "",
                -1 => " · TTG ∞",
                { } m when m >= 5760 => $" · TTG {m / 1440:0.#} d",
                { } m => $" · TTG {(int)(m / 60)}:{(int)m % 60:00}",
            };
            BatteryText = $"{soc:0.0} %{ttg}";
        }

        DeviceText = r.DeviceName is { } name ? $"{name} · FW {r.Firmware}" : "--";

        AlarmVisible = r.AlarmOn;
        if (r.AlarmOn) AlarmText = $"ALARM: {PowerReading.DescribeAlarm(r.AlarmReasons)}";
    }

    private void BlankReadouts()
    {
        _last = null;
        VoltsText = AmpsText = WattsText = "--";
        VoltsBrush = Palette.CardTextBrush;
        MinMaxText = EnergyText = BatteryText = DeviceText = "--";
        _hasBattery = false;
        OnPropertyChanged(nameof(BatteryRowVisible));
        AlarmVisible = false;
    }

    public void Dispose()
    {
        _meter.ReadingReceived -= Render;
        _meter.StateChanged -= OnStateChanged;
    }
}
