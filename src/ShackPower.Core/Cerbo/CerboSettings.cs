using System.Net;
using System.Net.Sockets;

namespace ShackPower.Core.Cerbo;

/// <summary>
/// Where the GX is and which unit IDs its services answer on. Only the battery (shunt) unit is
/// required — a Cerbo with just the SmartShunt plugged in is a complete setup; the MultiPlus
/// and MPPT units are optional and simply add rows when present.
/// </summary>
public sealed record CerboSettings
{
    public const int DefaultPort = 502;

    public string Host { get; init; } = "";
    public int Port { get; init; } = DefaultPort;

    /// <summary>Unit ID of the SmartShunt's <c>com.victronenergy.battery</c> service.</summary>
    public byte BatteryUnit { get; init; }

    /// <summary>Unit ID of the MultiPlus's <c>com.victronenergy.vebus</c> service, if wired.</summary>
    public byte? VeBusUnit { get; init; }

    /// <summary>Unit ID of the MPPT's <c>com.victronenergy.solarcharger</c> service, if wired.</summary>
    public byte? SolarUnit { get; init; }

    /// <summary>Also read the DVCC charge-current limit from unit 100 each cycle.</summary>
    public bool ReadDvccLimit { get; init; } = true;

    /// <summary>Parse <c>host</c> or <c>host:port</c> (IPv4 or a resolvable name); null when it
    /// isn't a usable endpoint. Resolution happens here, once, not on every reconnect.</summary>
    public static IPEndPoint? ParseEndpoint(string? text, int defaultPort = DefaultPort)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        var port = defaultPort;
        var host = text;
        var colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon)   // exactly one colon: host:port (not IPv6)
        {
            if (!int.TryParse(text[(colon + 1)..], out port) || port is < 1 or > 65535) return null;
            host = text[..colon];
        }
        if (host.Length == 0) return null;
        if (IPAddress.TryParse(host, out var ip)) return new IPEndPoint(ip, port);
        try
        {
            var addresses = Dns.GetHostAddresses(host);
            var v4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                     ?? addresses.FirstOrDefault();
            return v4 is null ? null : new IPEndPoint(v4, port);
        }
        catch
        {
            return null;
        }
    }

    public IPEndPoint? Endpoint => ParseEndpoint(Describe());

    public string Describe() => Port == DefaultPort ? Host : $"{Host}:{Port}";
}
