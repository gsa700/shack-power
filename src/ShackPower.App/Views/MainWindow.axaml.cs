using Avalonia.Controls;
using ShackPower.App.ViewModels;

namespace ShackPower.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (DataContext as MainWindowViewModel)?.Dispose();
    }
}
