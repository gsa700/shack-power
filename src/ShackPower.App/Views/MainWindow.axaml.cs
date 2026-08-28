using Avalonia.Controls;
using ShackPower.App.ViewModels;

namespace ShackPower.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnSetupClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        (Avalonia.Application.Current as App)?.ShowSetup();

    private void OnChartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        (Avalonia.Application.Current as App)?.ShowChart();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (DataContext as MainWindowViewModel)?.Dispose();
    }
}
