namespace ShackPower.Core.Cerbo;

/// <summary>Modbus register word type as the GX's <c>dbus-modbustcp</c> publishes it.</summary>
public enum RegType { UInt16, Int16 }

/// <summary>
/// One Victron GX Modbus TCP register: address, word type, and the scale factor the value is
/// divided by (<c>value = raw / Scale</c>, so a negative scale flips sign — Victron's own
/// convention for <c>/ConsumedAmphours</c>). Addresses and scales are copied verbatim from
/// <c>attributes.csv</c> in github.com/victronenergy/dbus_modbustcp (fetched 2026-09-09) — they
/// are documented separately from the VE.Direct text protocol and do <b>not</b> share its units
/// (SOC here is %×10, not ‰; TTG is seconds×0.01, not minutes).
/// </summary>
public readonly record struct RegisterSpec(ushort Address, RegType Type, double Scale)
{
    /// <summary>Decode one raw register word by this spec.</summary>
    public double Decode(ushort raw)
    {
        double v = Type == RegType.Int16 ? (short)raw : raw;
        return v / Scale;
    }
}

/// <summary>
/// The registers Shack Power reads from a Cerbo GX (or any Venus OS device). Grouped by D-Bus
/// service, because Modbus unit IDs select a <i>service instance</i> — the SmartShunt is a
/// <c>com.victronenergy.battery</c>, the MultiPlus a <c>.vebus</c>, the MPPT a <c>.solarcharger</c>,
/// and unit 100 is the system/settings aggregate. Unit IDs are assigned dynamically since
/// Venus 2.60 and are read off the GX (Settings → Integrations → Modbus TCP → Available
/// services) or found with <see cref="CerboDiscovery"/>.
/// </summary>
public static class CerboRegisters
{
    /// <summary>Unit ID that always addresses <c>com.victronenergy.system</c>/<c>.settings</c>.</summary>
    public const byte SystemUnit = 100;

    /// <summary>com.victronenergy.battery — the SmartShunt on a VE.Direct port.</summary>
    public static class Battery
    {
        public static readonly RegisterSpec Power = new(258, RegType.Int16, 1);          // W
        public static readonly RegisterSpec Voltage = new(259, RegType.UInt16, 100);     // V
        public static readonly RegisterSpec Current = new(261, RegType.Int16, 10);       // A, + = charging
        public static readonly RegisterSpec Temperature = new(262, RegType.Int16, 10);   // °C (aux sensor only)
        public static readonly RegisterSpec ConsumedAh = new(265, RegType.UInt16, -10);  // Ah, reported negative
        public static readonly RegisterSpec Soc = new(266, RegType.UInt16, 10);          // %
        public static readonly RegisterSpec Alarm = new(267, RegType.UInt16, 1);         // 0 none, 2 alarm
        public static readonly RegisterSpec AlarmLowVoltage = new(268, RegType.UInt16, 1);
        public static readonly RegisterSpec AlarmHighVoltage = new(269, RegType.UInt16, 1);
        public static readonly RegisterSpec AlarmLowSoc = new(272, RegType.UInt16, 1);
        public static readonly RegisterSpec MinVoltage = new(287, RegType.UInt16, 100);
        public static readonly RegisterSpec MaxVoltage = new(288, RegType.UInt16, 100);
        public static readonly RegisterSpec DischargedKwh = new(301, RegType.UInt16, 10);
        public static readonly RegisterSpec ChargedKwh = new(302, RegType.UInt16, 10);
        public static readonly RegisterSpec TimeToGoSeconds = new(303, RegType.UInt16, 0.01);
        public static readonly RegisterSpec CapacityAh = new(309, RegType.UInt16, 10);
    }

    /// <summary>com.victronenergy.vebus — the MultiPlus on the VE.Bus port.</summary>
    public static class VeBus
    {
        public static readonly RegisterSpec AcInVoltage = new(3, RegType.UInt16, 10);
        public static readonly RegisterSpec AcInCurrent = new(6, RegType.Int16, 10);
        public static readonly RegisterSpec AcInPower = new(12, RegType.Int16, 0.1);     // W (raw × 10)
        public static readonly RegisterSpec AcOutVoltage = new(15, RegType.UInt16, 10);
        public static readonly RegisterSpec AcOutCurrent = new(18, RegType.Int16, 10);
        public static readonly RegisterSpec AcOutPower = new(23, RegType.Int16, 0.1);
        public static readonly RegisterSpec DcVoltage = new(26, RegType.UInt16, 100);
        public static readonly RegisterSpec DcCurrent = new(27, RegType.Int16, 10);      // A, + = charging
        public static readonly RegisterSpec ActiveInput = new(29, RegType.UInt16, 1);    // 0 AC1, 1 AC2, 240 none
        public static readonly RegisterSpec State = new(31, RegType.UInt16, 1);
        public static readonly RegisterSpec Mode = new(33, RegType.UInt16, 1);           // writable: 1 charger only, 2 inverter only, 3 on, 4 off
        public static readonly RegisterSpec AcInCurrentLimit = new(22, RegType.Int16, 10);   // writable

        /// <summary>VE.Bus <c>/State</c> enum, from attributes.csv.</summary>
        public static string DescribeState(int state) => state switch
        {
            0 => "Off", 1 => "Low power", 2 => "Fault", 3 => "Bulk", 4 => "Absorption",
            5 => "Float", 6 => "Storage", 7 => "Equalize", 8 => "Passthru", 9 => "Inverting",
            10 => "Power assist", 11 => "Power supply", 252 => "External control",
            _ => $"State {state}",
        };
    }

    /// <summary>com.victronenergy.solarcharger — the MPPT (Phase 2).</summary>
    public static class Solar
    {
        public static readonly RegisterSpec DcVoltage = new(771, RegType.UInt16, 100);
        public static readonly RegisterSpec DcCurrent = new(772, RegType.Int16, 10);     // A into the battery
        public static readonly RegisterSpec State = new(775, RegType.UInt16, 1);
        public static readonly RegisterSpec PvVoltage = new(776, RegType.UInt16, 100);
        public static readonly RegisterSpec YieldTodayKwh = new(784, RegType.UInt16, 10);
        public static readonly RegisterSpec ErrorCode = new(788, RegType.UInt16, 1);
        public static readonly RegisterSpec PvPower = new(789, RegType.UInt16, 10);      // W

        public static string DescribeState(int state) => state switch
        {
            0 => "Off", 2 => "Fault", 3 => "Bulk", 4 => "Absorption", 5 => "Float",
            6 => "Storage", 7 => "Equalize", 11 => "Other", 252 => "External control",
            _ => $"State {state}",
        };
    }

    /// <summary>com.victronenergy.settings / .system on unit <see cref="SystemUnit"/>.</summary>
    public static class System
    {
        /// <summary>DVCC "Limit charge current" — the whole-system charger ceiling applied to the
        /// MultiPlus and every MPPT. Writable; <c>-1</c> = no limit. This is the charge-inhibit
        /// knob (write 0 while operating): <c>/Hub4/DisableCharge</c> needs the ESS assistant,
        /// and Mode "Inverter only" would drop AC pass-through. Requires DVCC enabled on the GX.</summary>
        public static readonly RegisterSpec DvccMaxChargeCurrent = new(2705, RegType.Int16, 1);
        public static readonly RegisterSpec AcInSource = new(826, RegType.Int16, 1);     // 1 grid … 240 not connected
        public static readonly RegisterSpec BatteryVoltage = new(840, RegType.UInt16, 10);
        public static readonly RegisterSpec BatteryCurrent = new(841, RegType.Int16, 10);
        public static readonly RegisterSpec BatterySoc = new(843, RegType.UInt16, 1);
        public static readonly RegisterSpec BatteryState = new(844, RegType.UInt16, 1);   // 0 idle 1 charging 2 discharging
        public static readonly RegisterSpec PvPower = new(850, RegType.UInt16, 1);
    }
}
