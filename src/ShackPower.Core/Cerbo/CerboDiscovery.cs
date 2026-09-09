namespace ShackPower.Core.Cerbo;

/// <summary>Which Victron service kind a Modbus unit ID answers as.</summary>
public enum CerboServiceKind { Battery, VeBus, Solar }

public sealed record CerboService(byte UnitId, CerboServiceKind Kind, string Summary);

/// <summary>
/// Finds the unit IDs of the services on a GX by probing each candidate with a register that
/// only that service kind publishes: battery <c>/Dc/0/Voltage</c> (259), vebus <c>/State</c>
/// (31), solarcharger <c>/State</c> (775). Unit IDs are dynamic since Venus 2.60 (equal to the
/// device instance; VE.Direct ports on a Cerbo land in the 220s, USB cables in the 280s–290s),
/// so the candidate set is the whole documented range rather than a fixed table. Read-only and
/// harmless: a wrong unit just answers a Modbus exception. Runs synchronously — call it off the
/// UI thread; ~60 probes take a second or two on a LAN.
/// </summary>
public static class CerboDiscovery
{
    /// <summary>Preferred/typical unit IDs from Victron's mapping sheet plus the dynamic range.</summary>
    public static IEnumerable<byte> DefaultCandidates
    {
        get
        {
            for (var u = 223; u <= 247; u++) yield return (byte)u;   // Cerbo/CCGX/Venus GX ports (preferred IDs)
            for (var u = 1; u <= 31; u++) yield return (byte)u;      // low instances some setups use
            for (var u = 200; u <= 222; u++) yield return (byte)u;
            yield return 100;                                        // system aggregate (never a device, but harmless)
        }
    }

    public static IReadOnlyList<CerboService> Probe(ICerboModbus modbus, IEnumerable<byte>? candidates = null)
    {
        var found = new List<CerboService>();
        foreach (var unit in (candidates ?? DefaultCandidates).Distinct())
        {
            if (unit == CerboRegisters.SystemUnit) continue;   // unit 100 answers battery registers as the *system* battery; skip

            if (TryRead(modbus, unit, CerboRegisters.Battery.Voltage.Address, 1) is { } v)
            {
                var volts = CerboRegisters.Battery.Voltage.Decode(v[0]);
                var soc = TryRead(modbus, unit, CerboRegisters.Battery.Soc.Address, 1);
                var socText = soc is null ? "" : $", SOC {CerboRegisters.Battery.Soc.Decode(soc[0]):0.0} %";
                found.Add(new CerboService(unit, CerboServiceKind.Battery, $"battery monitor — {volts:0.00} V{socText}"));
                continue;
            }
            if (TryRead(modbus, unit, CerboRegisters.VeBus.State.Address, 1) is { } s)
            {
                found.Add(new CerboService(unit, CerboServiceKind.VeBus,
                    $"inverter/charger — {CerboRegisters.VeBus.DescribeState(s[0])}"));
                continue;
            }
            if (TryRead(modbus, unit, CerboRegisters.Solar.State.Address, 1) is { } p)
            {
                found.Add(new CerboService(unit, CerboServiceKind.Solar,
                    $"solar charger — {CerboRegisters.Solar.DescribeState(p[0])}"));
            }
        }
        return found;
    }

    private static ushort[]? TryRead(ICerboModbus modbus, byte unit, ushort address, ushort count)
    {
        try { return modbus.ReadHolding(unit, address, count); }
        catch (Exception ex) when (CerboDecoder.IsTransportFailure(ex)) { throw; }
        catch { return null; }
    }
}
