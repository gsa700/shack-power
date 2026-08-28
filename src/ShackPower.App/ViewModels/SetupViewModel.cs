using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using ShackPower.App.Services;
using ShackPower.App.Settings;

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
        ToggleConnectCommand = new RelayCommand(ToggleConnect, () => _meter.IsSimulated || SelectedPort is not null);
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
        else if (SelectedPort is { } port) _meter.Connect(port, PortIdentity.SerialFor(port));
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
