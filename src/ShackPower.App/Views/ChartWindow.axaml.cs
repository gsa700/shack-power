using Avalonia.Controls;

namespace ShackPower.App.Views;

public partial class ChartWindow : Window
{
    public ChartWindow() => InitializeComponent();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (Avalonia.Application.Current as App)?.NotifyChartClosing(this);
    }
}
