using Avalonia.Media;

namespace ShackPower.App;

/// <summary>
/// Shared palette — an homage to VictronConnect, with the header blue and action orange sampled
/// from the real app on this station (2026-08-28). Unlike the dark siblings, Shack Power is a
/// light theme: the window ground is the Victron blue, readings sit on white cards with dark
/// text, and status text on the blue uses soft tints (full-strength red/green vibrate against
/// this blue). XAML pulls the same colors from App.axaml resources; this is the code-side mirror
/// for view-models and custom-drawn controls.
/// </summary>
public static class Palette
{
    // The blue ground and the white panels that sit on it.
    public static readonly Color Bg = Color.FromRgb(0x38, 0x7D, 0xC5);       // VictronConnect header blue
    public static readonly Color Panel = Color.FromRgb(0xFF, 0xFF, 0xFF);
    public static readonly Color Track = Color.FromRgb(0x2A, 0x62, 0xA0);    // borders on the blue
    public static readonly Color Shadow = Color.FromRgb(0x1E, 0x4E, 0x83);   // card drop shadow

    // Text on the blue ground.
    public static readonly Color Text = Color.FromRgb(0xFF, 0xFF, 0xFF);
    public static readonly Color Dim = Color.FromRgb(0xCF, 0xE2, 0xF4);

    // Text on white cards/panels.
    public static readonly Color CardText = Color.FromRgb(0x25, 0x38, 0x4C);
    public static readonly Color CardDim = Color.FromRgb(0x82, 0x96, 0xA8);

    // Accents. Orange is VictronConnect's action color; the deep variant reads better on white.
    public static readonly Color Orange = Color.FromRgb(0xFD, 0x87, 0x45);
    public static readonly Color OrangeDeep = Color.FromRgb(0xE8, 0x77, 0x22);
    public static readonly Color Red = Color.FromRgb(0xD8, 0x3A, 0x22);      // on white
    public static readonly Color RedSoft = Color.FromRgb(0xFF, 0xB3, 0xA6);  // on the blue
    public static readonly Color Green = Color.FromRgb(0x2E, 0x9E, 0x44);    // on white
    public static readonly Color GreenSoft = Color.FromRgb(0xB8, 0xF0, 0xC0);// on the blue
    public static readonly Color Blue = Color.FromRgb(0x38, 0x7D, 0xC5);

    public static readonly IBrush BgBrush = new SolidColorBrush(Bg);
    public static readonly IBrush PanelBrush = new SolidColorBrush(Panel);
    public static readonly IBrush TrackBrush = new SolidColorBrush(Track);
    public static readonly IBrush ShadowBrush = new SolidColorBrush(Shadow);
    public static readonly IBrush TextBrush = new SolidColorBrush(Text);
    public static readonly IBrush DimBrush = new SolidColorBrush(Dim);
    public static readonly IBrush CardTextBrush = new SolidColorBrush(CardText);
    public static readonly IBrush CardDimBrush = new SolidColorBrush(CardDim);
    public static readonly IBrush OrangeBrush = new SolidColorBrush(Orange);
    public static readonly IBrush OrangeDeepBrush = new SolidColorBrush(OrangeDeep);
    public static readonly IBrush RedBrush = new SolidColorBrush(Red);
    public static readonly IBrush RedSoftBrush = new SolidColorBrush(RedSoft);
    public static readonly IBrush GreenBrush = new SolidColorBrush(Green);
    public static readonly IBrush GreenSoftBrush = new SolidColorBrush(GreenSoft);
    public static readonly IBrush BlueBrush = new SolidColorBrush(Blue);
}
