using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using ShackPower.App.Services;
using ShackPower.App.Settings;
using ShackPower.Core.Cerbo;

namespace ShackPower.App.ViewModels;

/// <summary>
/// The tabbed Setup window's model: Connection / Logging / Display / Updates. Follows W2's
/// version — the reference for tab handling (clamp in the setter, <c>ShowSetup(int? tab)</c>
/// selecting the Updates tab on update paths).
/// </summary>
public sealed class SetupViewModel : ViewModelBase
{
    /// <summary>Keep in step if a tab is added.</summary>
    public const int TabCount = 4;
    public const int UpdatesTab = 3;

    private readonly MeterService _meter;
    private readonly PowerLoggingService _logging;
    private readonly Action _exitForUpdate;
    private string? _stagedAssetUrl;

    public SetupViewModel(MeterService meter, DisplaySettings display,
        PowerLoggingService logging, Action exitForUpdate)
    {
        _meter = meter;
        Display = display;
        _logging = logging;
        _exitForUpdate = exitForUpdate;

        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        ToggleConnectCommand = new RelayCommand(ToggleConnect,
            () => _meter.IsSimulated || (_meter.IsCerbo ? CerboEndpointValid : SelectedPort is not null));
        FindCerboDevicesCommand = new RelayCommand(() => _ = FindCerboDevicesAsync(), () => !_cerboBusy && CerboEndpointValid);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
        CheckOrInstallCommand = new RelayCommand(() => _ = CheckOrInstallAsync(), () => !_updateBusy);
        OpenReleasePageCommand = new RelayCommand(() => OpenUrl(_releaseUrl));

        _meter.StateChanged += OnMeterState;
        _logging.Changed += OnLoggingChanged;
        RefreshPorts();
        OnMeterState();
        OnLoggingChanged();
    }

    public DisplaySettings Display { get; }

    // Clamped: the value comes back from config, and a stale or hand-edited one would leave the
    // TabControl with nothing selected.
    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, Math.Clamp(value, 0, TabCount - 1));
    }

    // ---- Connection ----

    public ObservableCollection<string> Ports { get; } = [];

    private string? _selectedPort;
    public string? SelectedPort
    {
        get => _selectedPort;
        set
        {
            if (SetProperty(ref _selectedPort, value))
            {
                OnPropertyChanged(nameof(PinnedSerialText));
                ToggleConnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The chip serial the selected port would be pinned by.</summary>
    public string PinnedSerialText =>
        SelectedPort is { } p && PortIdentity.SerialFor(p) is { } s
            ? $"cable serial {s}" : "no cable serial — pinned by port name only";

    public RelayCommand RefreshPortsCommand { get; }
    public RelayCommand ToggleConnectCommand { get; }

    public string ToggleConnectText => _meter.IsConnected ? "Disconnect" : "Connect";
    public string ConnectionStatusText => _meter.Status;
    public IBrush ConnectionStatusBrush => _meter.StatusIsError ? Palette.RedBrush
        : _meter.IsConnected ? Palette.GreenBrush : Palette.CardDimBrush;

    public void SelectPort(string? port)
    {
        if (port is not null && !Ports.Contains(port)) Ports.Add(port);
        SelectedPort = port;
    }

    private void RefreshPorts()
    {
        var current = SelectedPort;
        Ports.Clear();
        foreach (var p in MeterService.GetPortNames().OrderBy(PortSortKey)) Ports.Add(p);
        SelectedPort = current is not null && Ports.Contains(current) ? current
            : Ports.FirstOrDefault();
    }

    private static (int, string) PortSortKey(string port) =>
        port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(port[3..], out var n) ? (n, "") : (int.MaxValue, port);

    private void ToggleConnect()
    {
        if (_meter.IsConnected) _meter.Disconnect();
        else if (_meter.IsSimulated) _meter.Connect("SIM");
        else if (_meter.IsCerbo) { if (CerboEndpointValid) _meter.Connect(CerboHost.Trim()); }
        else if (SelectedPort is { } port) _meter.Connect(port, PortIdentity.SerialFor(port));
    }

    // ---- Source: VE.Direct cable vs Cerbo GX ----

    /// <summary>The kind this run was built with — the radio buttons edit the *saved* choice,
    /// which takes effect on the next start (the reading source is fixed per MeterService).</summary>
    public bool RunningOnCerbo => _meter.IsCerbo;

    private bool _useCerbo;
    public bool UseCerbo
    {
        get => _useCerbo;
        set
        {
            if (SetProperty(ref _useCerbo, value))
            {
                OnPropertyChanged(nameof(UseSerial));
                OnPropertyChanged(nameof(SourceRestartNote));
                OnPropertyChanged(nameof(SourceRestartNoteVisible));
            }
        }
    }

    public bool UseSerial
    {
        get => !_useCerbo;
        set => UseCerbo = !value;
    }

    public bool SourceRestartNoteVisible => !_meter.IsSimulated && UseCerbo != RunningOnCerbo;
    public string SourceRestartNote => UseCerbo
        ? "Saved. Shack Power reads from the Cerbo GX the next time it starts."
        : "Saved. Shack Power reads the VE.Direct cable the next time it starts.";

    private string _cerboHost = "";
    /// <summary>Cerbo GX address as typed — <c>host</c> or <c>host:port</c>, e.g. <c>10.0.1.30</c>.</summary>
    public string CerboHost
    {
        get => _cerboHost;
        set
        {
            if (SetProperty(ref _cerboHost, value ?? ""))
            {
                OnPropertyChanged(nameof(CerboEndpointValid));
                ToggleConnectCommand.RaiseCanExecuteChanged();
                FindCerboDevicesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CerboEndpointValid =>
        !string.IsNullOrWhiteSpace(CerboHost) && CerboSettings.ParseEndpoint(CerboHost) is not null;

    private string _cerboBatteryUnit = "";
    public string CerboBatteryUnit { get => _cerboBatteryUnit; set => SetProperty(ref _cerboBatteryUnit, value ?? ""); }

    private string _cerboVeBusUnit = "";
    public string CerboVeBusUnit { get => _cerboVeBusUnit; set => SetProperty(ref _cerboVeBusUnit, value ?? ""); }

    private string _cerboSolarUnit = "";
    public string CerboSolarUnit { get => _cerboSolarUnit; set => SetProperty(ref _cerboSolarUnit, value ?? ""); }

    /// <summary>Parse a unit-ID text box: 1–247, else null (blank = not configured).</summary>
    public static int? ParseUnit(string text) =>
        int.TryParse(text?.Trim(), out var n) && n is >= 1 and <= 247 ? n : null;

    private bool _cerboBusy;
    private string _cerboFoundText = "";
    /// <summary>Result of the last "Find devices" probe, one line per service found.</summary>
    public string CerboFoundText { get => _cerboFoundText; private set => SetProperty(ref _cerboFoundText, value); }
    public bool CerboFoundVisible => !string.IsNullOrEmpty(CerboFoundText);

    public RelayCommand FindCerboDevicesCommand { get; }

    /// <summary>
    /// Ask the GX which unit IDs answer as a battery monitor, an inverter/charger and a solar
    /// charger, and fill the unit boxes from what comes back. Unit IDs are dynamic since Venus
    /// 2.60, so this replaces reading them off the GX's Modbus services screen. Read-only probes,
    /// own connection, off the UI thread — it can run while the live link is up.
    /// </summary>
    private async Task FindCerboDevicesAsync()
    {
        var endpoint = CerboSettings.ParseEndpoint(CerboHost);
        if (endpoint is null) return;
        _cerboBusy = true;
        FindCerboDevicesCommand.RaiseCanExecuteChanged();
        CerboFoundText = $"Probing {CerboHost.Trim()}…";
        OnPropertyChanged(nameof(CerboFoundVisible));
        try
        {
            var found = await Task.Run(() =>
            {
                using var modbus = new FluentModbusCerbo(endpoint, connectTimeoutMs: 3000, readTimeoutMs: 1500);
                modbus.Connect();
                return CerboDiscovery.Probe(modbus);
            });

            if (found.Count == 0)
            {
                CerboFoundText = "Connected, but no Victron devices answered. Is the shunt plugged into the GX's VE.Direct port?";
            }
            else
            {
                var lines = found.Select(s => $"unit {s.UnitId}: {s.Summary}");
                CerboFoundText = string.Join("\n", lines);
                if (found.FirstOrDefault(s => s.Kind == CerboServiceKind.Battery) is { } b) CerboBatteryUnit = b.UnitId.ToString();
                if (found.FirstOrDefault(s => s.Kind == CerboServiceKind.VeBus) is { } v) CerboVeBusUnit = v.UnitId.ToString();
                if (found.FirstOrDefault(s => s.Kind == CerboServiceKind.Solar) is { } p) CerboSolarUnit = p.UnitId.ToString();
            }
        }
        catch (Exception ex)
        {
            CerboFoundText = ex is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused }
                ? "Connection refused — enable Modbus TCP on the GX (Settings → Integrations → Modbus TCP server)."
                : $"Could not reach the GX: {ex.Message}";
        }
        finally
        {
            _cerboBusy = false;
            FindCerboDevicesCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CerboFoundVisible));
        }
    }

    private void OnMeterState()
    {
        OnPropertyChanged(nameof(ToggleConnectText));
        OnPropertyChanged(nameof(ConnectionStatusText));
        OnPropertyChanged(nameof(ConnectionStatusBrush));
    }

    // ---- Logging ----

    public bool LogEnabled
    {
        get => _logging.Enabled;
        set { _logging.Enabled = value; OnPropertyChanged(); }
    }

    public string LogDirText => _logging.LogDirectory;
    public string LoggedCountText => $"{_logging.LoggedCount:N0} rows written this session";
    public string? LogErrorText => _logging.LastError is { } e ? $"Logging error: {e}" : null;
    public bool LogErrorVisible => _logging.LastError is not null;

    public RelayCommand OpenLogsFolderCommand { get; }

    private void OnLoggingChanged()
    {
        OnPropertyChanged(nameof(LoggedCountText));
        OnPropertyChanged(nameof(LogErrorText));
        OnPropertyChanged(nameof(LogErrorVisible));
    }

    private void OpenLogsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _logging.LogDirectory, UseShellExecute = true });
        }
        catch { /* a file manager may not exist (headless test box); nothing useful to do */ }
    }

    // ---- Updates ----

    private bool _updateBusy;
    private string _releaseUrl = $"https://github.com/{UpdateService.Repo}/releases/latest";

    private string _updateStatus = $"Shack Power {UpdateService.CurrentVersion}";
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }

    private bool _updateAvailable;
    public bool UpdateAvailable { get => _updateAvailable; private set => SetProperty(ref _updateAvailable, value); }

    private string _updateButtonLabel = "Check for updates";
    public string UpdateButtonLabel { get => _updateButtonLabel; private set => SetProperty(ref _updateButtonLabel, value); }

    public bool CheckUpdatesAtStartup { get; set; }

    public RelayCommand CheckOrInstallCommand { get; }
    public RelayCommand OpenReleasePageCommand { get; }

    public async Task CheckUpdatesAsync()
    {
        _updateBusy = true;
        CheckOrInstallCommand.RaiseCanExecuteChanged();
        UpdateStatus = "Checking for updates…";
        try
        {
            var info = await UpdateService.CheckAsync();
            _releaseUrl = info.ReleaseUrl;
            if (info.Error is { } err)
            {
                UpdateStatus = $"Update check failed: {err}";
                UpdateAvailable = false;
                UpdateButtonLabel = "Check for updates";
            }
            else if (info.UpdateAvailable && info.AssetUrl is { } asset)
            {
                _stagedAssetUrl = asset;
                UpdateAvailable = true;
                UpdateStatus = $"{info.LatestTag} is available (running {info.CurrentVersion}).";
                UpdateButtonLabel = $"Install {info.LatestTag}";
            }
            else
            {
                UpdateAvailable = false;
                UpdateStatus = $"Up to date — Shack Power {info.CurrentVersion}.";
                UpdateButtonLabel = "Check for updates";
            }
        }
        finally
        {
            _updateBusy = false;
            CheckOrInstallCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task CheckOrInstallAsync()
    {
        if (!UpdateAvailable || _stagedAssetUrl is null)
        {
            await CheckUpdatesAsync();
            return;
        }

        _updateBusy = true;
        CheckOrInstallCommand.RaiseCanExecuteChanged();
        try
        {
            UpdateStatus = "Downloading update…";
            var staged = await UpdateService.DownloadAndStageAsync(_stagedAssetUrl);
            UpdateStatus = "Restarting to apply…";
            UpdateService.ApplyAndRestart(staged);
            _exitForUpdate();
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Update failed: {ex.Message}";
            _updateBusy = false;
            CheckOrInstallCommand.RaiseCanExecuteChanged();
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { /* no browser is a shrug, not a crash */ }
    }
}
