using System.Text.RegularExpressions;

namespace ShackPower.Core;

/// <summary>
/// Pulls the adapter's chip serial out of a Windows PnP device id. Pure string work — kept here
/// (rather than beside the WMI query) so it can be tested without Windows or a device attached.
/// </summary>
public static partial class UsbSerial
{
    // FTDIBUS\VID_0403+PID_6001+A10KMB4VA\0000  ->  A10KMB4VA
    [GeneratedRegex(@"FTDIBUS\\VID_[0-9A-Fa-f]{4}\+PID_[0-9A-Fa-f]{4}\+([^\\]+)\\")]
    private static partial Regex Ftdi();

    // USB\VID_10C4&PID_EA60\0001  ->  0001
    [GeneratedRegex(@"USB\\VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}\\([^\\]+)$")]
    private static partial Regex Usb();

    /// <summary>
    /// The adapter's serial, or null when the device doesn't expose one.
    ///
    /// An adapter with no serial burned in gets a synthesised, location-based id instead
    /// (<c>6&amp;122B2E46&amp;0&amp;1&amp;1</c>) — that identifies the USB *socket*, not the cable, so it
    /// changes the moment the cable is moved. Pinning to one would silently break the
    /// follow-the-cable behaviour it's meant to provide, so those are rejected: an '&amp;' is the
    /// giveaway, since real serials are plain alphanumeric.
    /// </summary>
    public static string? Extract(string pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId)) return null;

        var f = Ftdi().Match(pnpDeviceId);
        if (f.Success) return Clean(f.Groups[1].Value);

        var u = Usb().Match(pnpDeviceId);
        return u.Success ? Clean(u.Groups[1].Value) : null;
    }

    private static string? Clean(string candidate) =>
        candidate.Contains('&') || string.IsNullOrWhiteSpace(candidate) ? null : candidate;
}
