namespace ShackPower.Core.Cerbo;

/// <summary>
/// The "charger off while operating" control for the Cerbo architecture: writes DVCC's
/// system-wide charge-current limit (unit 100, register 2705) to 0 A, and restores the previous
/// limit when released. DVCC applies that ceiling to the MultiPlus and every MPPT at once, so one
/// knob covers Phase 1 and Phase 2 — and, unlike cutting the MultiPlus's AC input, it leaves
/// pass-through intact so the PC stays on mains.
///
/// <b>Fail-safe design.</b> A register write has no timeout: if this app dies while inhibited,
/// the GX would sit at 0 A forever. So this class is only half the mechanism —
/// <see cref="Reassert"/> is meant to be called on a heartbeat while inhibited, and a watchdog
/// <i>on the GX</i> (a Node-RED flow on Venus OS Large, see docs/cerbo-modbus.md) restores the
/// limit by itself if the heartbeat stops. Without the GX-side watchdog this is not safe to
/// leave engaged unattended, and the app says so in Setup.
///
/// Pure orchestration over <see cref="ICerboModbus"/>; every method throws on transport failure
/// so the caller can surface "couldn't inhibit" instead of silently operating with the charger on.
/// </summary>
public sealed class ChargeInhibit
{
    /// <summary>Victron's "no limit" sentinel for register 2705.</summary>
    public const short NoLimit = -1;

    private readonly ICerboModbus _modbus;

    public ChargeInhibit(ICerboModbus modbus) => _modbus = modbus;

    /// <summary>True between a successful <see cref="Engage"/> and <see cref="Release"/>.</summary>
    public bool IsEngaged { get; private set; }

    /// <summary>The limit that was in force before <see cref="Engage"/>, restored on release.
    /// −1 means "no limit". Null until first engaged.</summary>
    public short? PreviousLimitAmps { get; private set; }

    /// <summary>Read the current DVCC limit (−1 = none).</summary>
    public short ReadLimit()
    {
        var raw = _modbus.ReadHolding(CerboRegisters.SystemUnit, CerboRegisters.System.DvccMaxChargeCurrent.Address, 1);
        return (short)raw[0];
    }

    /// <summary>Inhibit charging: remember the present limit, write 0. Idempotent while engaged.</summary>
    public void Engage()
    {
        if (!IsEngaged) PreviousLimitAmps = ReadLimit();
        if (PreviousLimitAmps == 0) PreviousLimitAmps = NoLimit;   // 0 was already "inhibited" (e.g. after a crash); restore to no-limit, never to 0
        WriteLimit(0);
        IsEngaged = true;
    }

    /// <summary>Heartbeat: re-write 0 so a GX-side watchdog that restores the limit on a stale
    /// heartbeat sees the app is still alive and still wants the charger off.</summary>
    public void Reassert()
    {
        if (!IsEngaged) return;
        WriteLimit(0);
    }

    /// <summary>Restore the remembered limit. Safe to call when not engaged.</summary>
    public void Release()
    {
        if (!IsEngaged) return;
        WriteLimit(PreviousLimitAmps ?? NoLimit);
        IsEngaged = false;
    }

    /// <summary>Emergency path for a caller that only knows charging must resume (e.g. SOC below
    /// the reserve floor): clears the inhibit to "no limit" regardless of remembered state.</summary>
    public void ForceNoLimit()
    {
        WriteLimit(NoLimit);
        IsEngaged = false;
    }

    private void WriteLimit(short amps) =>
        _modbus.WriteSingle(CerboRegisters.SystemUnit, CerboRegisters.System.DvccMaxChargeCurrent.Address, unchecked((ushort)amps));
}
