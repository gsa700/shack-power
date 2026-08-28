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
    private DisplaySettings _display = null!;
    private MainWindow _mainWindow = null!;

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
            _meter = new MeterService(simulated);

            if (simulated)
            {
                _meter.Connect("SIM");
            }
            else
            {
                // Follow the cable by its chip serial across COM renumbering, then auto-connect.
                var startupPort = PortIdentity.ResolvePort(_config.Port, _config.Serial);
                if (startupPort is not null && MeterService.GetPortNames().Contains(startupPort))
                    _meter.Connect(startupPort, _config.Serial);
            }

            _mainWindow = new MainWindow { DataContext = new MainWindowViewModel(_meter, _display) };
            RestoreMainBounds(_mainWindow);
            _mainWindow.Topmost = _display.AlwaysOnTop;
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            _display.PropertyChanged += OnDisplayChanged;
            _mainWindow.Closing += (_, _) => SaveAndCleanup();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDisplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DisplaySettings.AlwaysOnTop))
            _mainWindow.Topmost = _display.AlwaysOnTop;
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

    private void SaveAndCleanup()
    {
        if (IsExiting) return;   // main.Closing fires once; guard against re-entry
        IsExiting = true;

        try
        {
            _config.X = _mainWindow.Position.X;
            _config.Y = _mainWindow.Position.Y;
            // Don't let a --sim run overwrite the real connection identity.
            if (!_meter.IsSimulated && _meter.CurrentPort is { } port)
            {
                _config.Port = port;
                if (PortIdentity.SerialFor(port) is { } serial) _config.Serial = serial;
            }
            _config.CaptureFrom(_display);
            ConfigStore.Save(_config);
        }
        catch { /* best effort */ }
        _meter.Dispose();
    }
}
