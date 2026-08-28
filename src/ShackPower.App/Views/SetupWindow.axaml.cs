using Avalonia.Controls;

namespace ShackPower.App.Views;

public partial class SetupWindow : Window
{
    public SetupWindow() => InitializeComponent();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        (Avalonia.Application.Current as App)?.NotifySetupClosing(this);
    }
}
