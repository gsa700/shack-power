using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ShackPower.App.Services;
using ShackPower.App.Settings;
using ShackPower.App.ViewModels;
using ShackPower.App.Views;
using ShackPower.Core;

namespace ShackPower.App;

public partial class App : Application
{
    private AppConfig _config = new();
    private MeterService _meter = null!;
    private PowerLoggingService _logging = null!;
    private DisplaySettings _display = null!;
    private ChartHistoryService _chartHistory = null!;
    private SetupViewModel _setupVm = null!;
    private ChartViewModel? _chartVm;
    private MainWindow _mainWindow = null!;
    private SetupWindow? _setupWindow;
    private ChartWindow? _chartWindow;

    public bool IsExiting { get; private set; }

    /// <summary>An uninstall is in flight; don't write settings back out on the way down.</summary>
    private bool _uninstalling;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _config = ConfigStore.Load();
            _display = new DisplaySettings();
            _config.ApplyTo(_display);

            var simulated = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--sim", StringComparison.OrdinalIgnoreCase));
            var openSetup = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase));
            var openChart = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--chart", StringComparison.OrdinalIgnoreCase));
            _meter = new MeterService(simulated);
            // A --sim run NEVER writes the real log: synthetic rows interleaved into genuine
            // operating history are pollution nothing downstream can reliably separate out
            // (learned the hard way on cutover day — dev sim sessions salted the live CSV).
            // Sim runs log to a sibling logs-sim directory instead, so the writer path still
            // gets exercised and the Logging tab stays honest about where rows are going.
            var logDir = simulated
                ? System.IO.Path.Combine(ConfigStore.DataDir, "logs-sim")
                : ConfigStore.LogDir;
            _logging = new PowerLoggingService(_meter, logDir, _config.LogEnabled);
            // Created at startup, not on first window open, so the live tail exists by the time
            // the Chart window is opened rather than starting empty. Reads the same directory
            // logging writes, so a sim chart browses sim history, never the real files.
            _chartHistory = new ChartHistoryService(_meter, logDir);
            _setupVm = new SetupViewModel(_meter, _display, _logging, ExitForUpdate)
            {
                CheckUpdatesAtStartup = _config.CheckUpdatesAtStartup,
                SelectedTabIndex = _config.SetupTab,   // clamped in the setter
            };

            // A hand-installed copy is adopted where it stands; never block startup over this.
            if (!simulated) try { InstallService.EnsureRegistered(); } catch { /* recorded in its log */ }

            if (simulated)
            {
                _meter.Connect("SIM");
            }
            else if (!Program.PendingUninstall)   // don't take the port for a run that only uninstalls
            {
                // Follow the cable by its chip serial across COM renumbering, then auto-connect.
                var startupPort = PortIdentity.ResolvePort(_config.Port, _config.Serial);
                _setupVm.SelectPort(startupPort);
                if (startupPort is not null && MeterService.GetPortNames().Contains(startupPort))
                    _meter.Connect(startupPort, _config.Serial);
            }

            _mainWindow = new MainWindow { DataContext = new MainWindowViewModel(_meter, _display) };
            RestoreMainBounds(_mainWindow);
            _mainWindow.Topmost = _display.AlwaysOnTop;
            desktop.MainWindow = _mainWindow;
            // Hiding to the tray must not end the app: OnMainWindowClose fires on Close(), and a
            // Hide() from the minimize intercept below keeps the window alive, just invisible.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            _display.PropertyChanged += OnDisplayChanged;
            _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            _mainWindow.Closing += (_, _) => SaveAndCleanup();
            SyncTrayIcon();

            var updateFailed = !simulated && UpdateService.ConsumeUpdateFailed();

            _mainWindow.Opened += async (_, _) =>
            {
                // This run exists only to uninstall: ask, act, and go. Nothing else should start.
                if (Program.PendingUninstall)
                {
                    await RunUninstallAsync();
                    return;
                }

                if (openSetup) ShowSetup();
                if (openChart) ShowChart();

                // A copy running from wherever it was unzipped offers to install itself.
                if (!simulated && InstallService.Mode == InstallMode.Loose && await OfferInstallAsync()) return;

                if (updateFailed) ShowSetup(SetupViewModel.UpdatesTab);

                if (_config.CheckUpdatesAtStartup && !simulated)
                {
                    await _setupVm.CheckUpdatesAsync();
                    if (_setupVm.UpdateAvailable) ShowSetup(SetupViewModel.UpdatesTab);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Open Setup, optionally forcing a tab — the update paths pass the Updates tab so
    /// the window explains why it opened (the fix LP-100A still owes W2).</summary>
    public void ShowSetup(int? tab = null)
    {
        if (tab is not null) _setupVm.SelectedTabIndex = tab.Value;
        if (_setupWindow is null)
        {
            _setupWindow = new SetupWindow { DataContext = _setupVm, Topmost = _display.AlwaysOnTop };
            RestoreSetupBounds(_setupWindow);
            _setupWindow.Show(_mainWindow);   // owned by main -> closes with it
        }
        else
        {
            _setupWindow.Show();
        }
        _setupWindow.Activate();
    }

    /// <summary>Open the Chart window (LP-100A's Vector/Log-window pattern: owned by main,
    /// view model built on demand and detached on close).</summary>
    public void ShowChart()
    {
        if (_chartWindow is null)
        {
            _chartVm = new ChartViewModel(_chartHistory) { Combined = _config.ChartCombined };
            _chartWindow = new ChartWindow { DataContext = _chartVm, Topmost = _display.AlwaysOnTop };
            RestoreChartBounds(_chartWindow);
            _chartWindow.Show(_mainWindow);   // owned by main -> closes with it
        }
        else
        {
            _chartWindow.Show();
        }
        _chartWindow.Activate();
    }

    public void NotifyChartClosing(ChartWindow w)
    {
        _config.ChartX = w.Position.X;
        _config.ChartY = w.Position.Y;
        _config.ChartW = w.Width;
        _config.ChartH = w.Height;
        if (_chartVm is not null) _config.ChartCombined = _chartVm.Combined;
        // Unhook so a closed window isn't still re-decimating on every reading.
        _chartVm?.Dispose();
        _chartVm = null;
        _chartWindow = null;
    }

    private void RestoreChartBounds(Window w)
    {
        if (_config.ChartW is > 400) w.Width = _config.ChartW.Value;
        if (_config.ChartH is > 300) w.Height = _config.ChartH.Value;
        if (_config is { ChartX: not null, ChartY: not null })
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)_config.ChartX.Value, (int)_config.ChartY.Value);
        }
    }

    /// <summary>Close the app so the staged update helper can swap the executable and relaunch.</summary>
    public void ExitForUpdate() => _mainWindow.Close();

    /// <summary>Minimal modal confirm; <paramref name="negative"/> null makes it a one-button notice.</summary>
    private Task<bool> ConfirmAsync(string title, string message,
        string affirmative = "Continue", string? negative = "Cancel", string? detail = null) =>
        new ConfirmWindow(title, message, affirmative, negative, detail).ShowDialog<bool>(_mainWindow);

    /// <summary>
    /// Offer to install a loose copy. Returns true if the app is handing over to the installed
    /// copy and the caller should stop starting things up.
    /// </summary>
    private async Task<bool> OfferInstallAsync()
    {
        var accepted = await ConfirmAsync(
            "Install Shack Power",
            "Install Shack Power on this computer?",
            affirmative: "Install",
            negative: "Not now",
            detail: $"Copies the program to {InstallService.InstallDirectory} and lists it in "
                  + "Settings → Apps → Installed apps, with a Start Menu shortcut. Your settings and "
                  + "power logs are untouched either way.\n\n"
                  + $"To run from here permanently without being asked again, put a file named "
                  + $"{InstallLayout.PortableMarker} beside the program.");

        if (!accepted) return false;

        try
        {
            var installed = InstallService.Install();

            // Installed but not listed is a real outcome, not a detail: the program works, yet the
            // usual way to remove it is missing. Say so here rather than leave it to be discovered.
            if (!installed.Registered)
            {
                await ConfirmAsync("Installed, with one problem",
                    $"Shack Power is installed in {InstallService.InstallDirectory} and will run "
                    + "normally, but it could not add itself to Settings → Apps → Installed apps.",
                    affirmative: "OK", negative: null,
                    detail: "Starting the installed copy again usually adds the entry. Failing that, "
                          + "run it once with --install from a command prompt.");
            }

            InstallService.LaunchDetached(installed.ExePath);
            // Closing runs the normal save path on purpose, so settings carry over to the
            // installed copy, which reads the same per-user data directory.
            _mainWindow.Close();
            return true;
        }
        catch (Exception ex)
        {
            await ConfirmAsync("Install failed",
                "Shack Power could not install itself.", affirmative: "OK", negative: null,
                detail: ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Interactive uninstall. Settings and the power logs are asked about separately, both
    /// defaulting to keep: they share a directory but not their stakes, and the logs are
    /// operating history nothing can bring back.
    /// </summary>
    private async Task RunUninstallAsync()
    {
        var confirmed = await ConfirmAsync(
            "Uninstall Shack Power",
            "Remove Shack Power from this computer?",
            affirmative: "Uninstall",
            negative: "Cancel",
            detail: $"Deletes the program from {InstallService.ExeDirectory} and removes its "
                  + "Start Menu shortcut and Installed apps entry.");

        if (!confirmed)
        {
            _mainWindow.Close();
            return;
        }

        var removeSettings = await ConfirmAsync(
            "Settings",
            "Also delete your settings?",
            affirmative: "Delete settings",
            negative: "Keep settings",
            detail: "Serial port pinning, display rows, thresholds and window positions. Keeping "
                  + "them means a later reinstall picks up exactly where you left off.");

        var removeLogs = await ConfirmAsync(
            "Power logs",
            "Also delete your power logs?",
            affirmative: "Delete the logs",
            negative: "Keep the logs",
            detail: PowerLogWarning());

        _uninstalling = true;
        InstallService.Uninstall(new UninstallOptions(removeSettings, removeLogs));
        _mainWindow.Close();
    }

    /// <summary>Spell out what deleting the logs costs, in days of history rather than filenames —
    /// "213 days of readings" is a decision someone can make; "power-*.csv" is not.</summary>
    private static string PowerLogWarning()
    {
        var days = InstallService.CountLogDays();
        var what = days > 0
            ? $"This is {days:N0} day{(days == 1 ? "" : "s")} of voltage, current and power history "
            + "at one reading per second."
            : "This is your recorded voltage, current and power history.";
        return what + $" It cannot be recovered.\n\nKeeping it leaves the files in "
             + $"{ConfigStore.LogDir}, where a later install will find them again.";
    }

    /// <summary>A child window is closing; capture its bounds and drop the reference.</summary>
    public void NotifySetupClosing(SetupWindow w)
    {
        _config.SetupX = w.Position.X;
        _config.SetupY = w.Position.Y;
        _setupWindow = null;
    }

    // ---- tray ----

    private void OnTrayClicked(object? sender, EventArgs e) => RestoreFromTray();
    private void OnTrayShow(object? sender, EventArgs e) => RestoreFromTray();
    private void OnTrayExit(object? sender, EventArgs e) => _mainWindow.Close();

    private void RestoreFromTray()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void SyncTrayIcon()
    {
        // The tray icon only exists for people who asked for tray behavior.
        var icons = TrayIcon.GetIcons(this);
        if (icons is { Count: > 0 }) icons[0].IsVisible = _display.MinimizeToTray;
    }

    private void OnMainWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty
            && _mainWindow.WindowState == WindowState.Minimized
            && _display.MinimizeToTray && !IsExiting)
        {
            _mainWindow.Hide();   // to the tray; the tray icon is visible whenever this path is on
        }
    }

    private void OnDisplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DisplaySettings.AlwaysOnTop):
                _mainWindow.Topmost = _display.AlwaysOnTop;
                if (_setupWindow is not null) _setupWindow.Topmost = _display.AlwaysOnTop;
                if (_chartWindow is not null) _chartWindow.Topmost = _display.AlwaysOnTop;
                break;
            case nameof(DisplaySettings.MinimizeToTray):
                SyncTrayIcon();
                // Turning the option off while hidden would strand an invisible window with no
                // tray icon to find it by — bring it back before removing the way back.
                if (!_display.MinimizeToTray && !_mainWindow.IsVisible) RestoreFromTray();
                break;
        }
    }

    private void RestoreMainBounds(Window w)
    {
        // Width is fixed and height auto-fits content, so only the position is restored.
        if (_config is { X: not null, Y: not null })
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)_config.X.Value, (int)_config.Y.Value);
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void RestoreSetupBounds(Window w)
    {
        if (_config is { SetupX: not null, SetupY: not null })
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Position = new PixelPoint((int)_config.SetupX.Value, (int)_config.SetupY.Value);
        }
    }

    private void SaveAndCleanup()
    {
        if (IsExiting) return;   // main.Closing fires once; guard against re-entry
        IsExiting = true;

        // An uninstall must not write config.json back out on its way down — the user may have
        // just asked for it to be deleted, and recreating it here would undo that answer.
        if (_uninstalling)
        {
            _chartVm?.Dispose();
            _chartHistory.Dispose();
            _logging.Dispose();
            _meter.Dispose();
            return;
        }

        try
        {
            _config.X = _mainWindow.Position.X;
            _config.Y = _mainWindow.Position.Y;
            if (_setupWindow is not null)
            {
                _config.SetupX = _setupWindow.Position.X;
                _config.SetupY = _setupWindow.Position.Y;
            }
            if (_chartWindow is not null)
            {
                _config.ChartX = _chartWindow.Position.X;
                _config.ChartY = _chartWindow.Position.Y;
                _config.ChartW = _chartWindow.Width;
                _config.ChartH = _chartWindow.Height;
                if (_chartVm is not null) _config.ChartCombined = _chartVm.Combined;
            }
            // Don't let a --sim run overwrite the real connection identity.
            if (!_meter.IsSimulated)
            {
                var port = _meter.CurrentPort ?? _setupVm.SelectedPort;
                if (port is not null)
                {
                    _config.Port = port;
                    if (PortIdentity.SerialFor(port) is { } serial) _config.Serial = serial;
                }
            }
            _config.SetupTab = _setupVm.SelectedTabIndex;
            _config.CheckUpdatesAtStartup = _setupVm.CheckUpdatesAtStartup;
            _config.LogEnabled = _logging.Enabled;
            _config.CaptureFrom(_display);
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
        _chartVm?.Dispose();
        _chartHistory.Dispose();
        _logging.Dispose();
        _meter.Dispose();
    }
}
