using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ShackPower.App.Services;
using ShackPower.App.Settings;
using ShackPower.App.ViewModels;
using ShackPower.App.Views;

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
            _logging = new PowerLoggingService(_meter, ConfigStore.LogDir, _config.LogEnabled);
            // Created at startup, not on first window open, so the live tail exists by the time
            // the Chart window is opened rather than starting empty.
            _chartHistory = new ChartHistoryService(_meter, ConfigStore.LogDir);
            _setupVm = new SetupViewModel(_meter, _display, _logging, ExitForUpdate)
            {
                CheckUpdatesAtStartup = _config.CheckUpdatesAtStartup,
                SelectedTabIndex = _config.SetupTab,   // clamped in the setter
            };

            if (simulated)
            {
                _meter.Connect("SIM");
            }
            else
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

            _mainWindow.Opened += async (_, _) =>
            {
                if (openSetup) ShowSetup();
                if (openChart) ShowChart();
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
            _chartVm = new ChartViewModel(_chartHistory);
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
