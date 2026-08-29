using Avalonia.Controls;
using ShackPower.App.Controls;
using ShackPower.App.ViewModels;

namespace ShackPower.App.Views;

public partial class ChartWindow : Window
{
    public ChartWindow()
    {
        InitializeComponent();

        // Zoom/pan gestures route from whichever chart surface the pointer is over to the one
        // shared viewport in the view model — the strips and the overlay always move together.
        foreach (var strip in new[] { VoltStrip, AmpStrip, WattStrip })
        {
            strip.ZoomRequested += (anchor, factor) => Vm?.ZoomAt(anchor, factor);
            strip.PanRequested += fraction => Vm?.PanBy(fraction);
        }
        Overlay.ZoomRequested += (anchor, factor) => Vm?.ZoomAt(anchor, factor);
        Overlay.PanRequested += fraction => Vm?.PanBy(fraction);
    }

    private ChartViewModel? Vm => DataContext as ChartViewModel;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (Avalonia.Application.Current as App)?.NotifyChartClosing(this);
    }
}
