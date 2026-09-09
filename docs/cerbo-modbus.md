# Cerbo GX over Modbus TCP — the app's device layer since 2026-09

Written 2026-09-09 (HAMBENCH session), ahead of the Cerbo GX MKII + MultiPlus arriving
2026-09-10, so the first hardware day is *verification*, not design. Companion to
`power-system.md` (why the system looks like this) — this file is *how the app talks to it*.

## Why a hub, and what changed in the app

The 2026-09-08 redesign (Techbench session) made the **Victron Cerbo GX MKII** the owner of every
Victron cable: the SmartShunt on a native VE.Direct port, the MultiPlus on the VE.Bus port, the
MPPT on another VE.Direct port in Phase 2. The GX publishes all of it over Ethernet as
**Modbus TCP** (plus VRM, remote console, MQTT). Shack Power therefore becomes one **network
client** of the hub instead of the process that owns a serial port.

What that removes from this app: COM-port pinning by FTDI serial, `VictronConnect grabs the
port` collisions, USB replug/renumber following, sleep/resume port recovery. What it adds:
ordinary TCP reconnect, and a *write* path (the charge-inhibit knob) the receive-only VE.Direct
design never had.

**The VE.Direct-over-USB path is kept, not deleted.** It is still the right answer for a shunt
on a PC with no GX (the Linux testbed at 10.0.1.193 runs it today), it is the `--sim` baseline,
and it is the fallback if the hub is down. Setup → Connection has a Source radio: *VE.Direct
cable on this PC* or *Cerbo GX over the network*. The choice applies on the next start, because
a `MeterService` is built around one `IReadingSource` for its lifetime.

## Wire protocol facts (verified against Victron's own register list, 2026-09-09)

Source of truth: `attributes.csv` and `CCGX-Modbus-TCP-register-list.xlsx` in
github.com/victronenergy/dbus_modbustcp — copied into `CerboRegisters.cs` verbatim. **Do not
assume VE.Direct units carry over**: SOC is %×10 here (not ‰), TTG is seconds×0.01 (not
minutes), vebus AC power is W×0.1 (i.e. the word is tens of watts), `/ConsumedAmphours` has a
*negative* scale (a positive word decodes to Victron's negative Ah).

- Port **502**, function 3 (read holding) and 6 (write single). Big-endian 16-bit words.
  Modbus TCP is **off by default** on the GX: Settings → Integrations → Modbus TCP server.
- **Unit ID = D-Bus service instance.** Dynamic since Venus 2.60. The GX lists them under
  *Available services* on that same screen; the app's **Find devices** button probes for them
  (battery `/Dc/0/Voltage` 259, vebus `/State` 31, solarcharger `/State` 775). Typical Cerbo
  values from Victron's table: VE.Direct port 1 → 226, port 2 → 224, port 3 → 223, VE.Bus →
  227; USB VE.Direct cables land at 239 and 231–238. **Unit 100 is always the system
  aggregate** (`com.victronenergy.system` / `.settings`) and answers battery registers as the
  *system* battery, so discovery skips it.
- **A register the device doesn't publish answers a Modbus exception, and one bad word fails
  the whole block it sits in.** That is why `CerboDecoder` reads narrow groups (P/V/I as one
  block, SOC/CE as one, TTG alone, alarms alone…): a shunt without a temperature sensor, or one
  that isn't discharging (no TTG), must not zero out volts and amps. Modbus exceptions become
  nulls; only transport failures (socket/IO) end the session and trigger a reconnect.

### Registers the app uses

| Service (unit) | Path | Reg | Type | Scale | Meaning |
|---|---|---|---|---|---|
| battery (shunt) | /Dc/0/Power | 258 | int16 | 1 | W |
| | /Dc/0/Voltage | 259 | uint16 | 100 | V |
| | /Dc/0/Current | 261 | int16 | 10 | A, + = charging |
| | /Dc/0/Temperature | 262 | int16 | 10 | °C, only with the aux sensor |
| | /ConsumedAmphours | 265 | uint16 | −10 | Ah (negative) |
| | /Soc | 266 | uint16 | 10 | % |
| | /Alarms/Alarm, LowVoltage, HighVoltage, LowSoc | 267, 268, 269, 272 | uint16 | 1 | 0 none, 2 alarm → mapped onto VE.Direct's `AR` bits 1/2/4 |
| | /History/MinimumVoltage, MaximumVoltage | 287, 288 | uint16 | 100 | V |
| | /History/DischargedEnergy, ChargedEnergy | 301, 302 | uint16 | 10 | kWh |
| | /TimeToGo | 303 | uint16 | 0.01 | seconds; absent when not discharging → app shows ∞ |
| vebus (MultiPlus) | /Ac/ActiveIn/L1/V, I, P | 3, 6, 12 | uint16/int16/int16 | 10/10/0.1 | mains in |
| | /Ac/Out/L1/V, P | 15, 23 | uint16/int16 | 10/0.1 | the backed-up AC |
| | /Dc/0/Voltage, Current | 26, 27 | uint16/int16 | 100/10 | charger DC side |
| | /Ac/ActiveIn/ActiveInput | 29 | uint16 | 1 | 0 AC-in 1, 1 AC-in 2, **240 disconnected** |
| | /State | 31 | uint16 | 1 | 0 Off 1 Low power 2 Fault 3 Bulk 4 Absorption 5 Float 6 Storage 7 Equalize 8 Passthru 9 **Inverting** 10 Power assist 11 Power supply 252 External control |
| | /Mode | 33 | uint16 | 1 | **writable** — 1 charger only, 2 inverter only, 3 on, 4 off. *Not* used for inhibit (see below) |
| solarcharger (MPPT) | /Dc/0/Voltage, Current | 771, 772 | uint16/int16 | 100/10 | battery side |
| | /State | 775 | uint16 | 1 | same enum family as vebus |
| | /Pv/V | 776 | uint16 | 100 | V |
| | /History/Daily/0/Yield | 784 | uint16 | 10 | kWh today |
| | /ErrorCode | 788 | uint16 | 1 | Victron MPPT error codes |
| | /Yield/Power | 789 | uint16 | 10 | W |
| settings (unit 100) | /Settings/SystemSetup/MaxChargeCurrent | 2705 | int16 | 1 | **DVCC "Limit charge current"**, writable, −1 = no limit |
| system (unit 100) | /Dc/Battery/Voltage, Current, Power, Soc, State | 840–844 | | | aggregate view; not used, the shunt's own service is more precise |

## Charge inhibit ("quiet mode") — the design, and the open watchdog problem

**Need:** the MultiPlus's charger (and later the MPPT) off while operating, for HF hash, *without*
cutting the MultiPlus's AC input — that would make it invert and run the PC off the battery.

**Mechanism chosen: DVCC "Limit charge current" = 0**, register 2705 on unit 100. Victron applies
that ceiling to inverter/chargers *and* solar chargers system-wide, so one knob covers Phase 1
and Phase 2, and AC pass-through is untouched. Rejected: `/Hub4/DisableCharge` (register 38)
needs the ESS or PV-inverter assistant; `/Mode` = inverter-only drops pass-through; the
MultiPlus's own charge current has no Modbus register at all. **DVCC must be enabled on the
GX** (Settings → DVCC) or 2705 does nothing. `ChargeInhibit` (Core) implements engage /
re-assert / release with the previous limit remembered, and never restores *to* 0 — a stale 0
from a crashed session restores to "no limit".

**Open before this is wired to a button:** a register write has no timeout. If the app dies
while inhibited, the GX sits at 0 A until someone notices. The old smart-plug design had the
fail-safe in hardware (Shelly `auto_on`). The equivalent here has to live **on the GX**:

- **Venus OS Large + Node-RED** (Settings → Firmware → Online updates → Image type *Large*;
  Cerbo GX MKII is supported; editor at `http://<gx>:1881`). The pre-installed
  `node-red-contrib-victron` palette exposes the needed writable paths — verified in its
  `services.json`: `settings /Settings/SystemSetup/MaxChargeCurrent`, `vebus /Hub4/DisableCharge`,
  `vebus /Mode`, `solarcharger /Mode`.
- **Watchdog flow (to build Thursday or after):** the app re-writes 2705 = 0 every 30 s while
  inhibited (`ChargeInhibit.Reassert`). A Node-RED flow watches 2705; whenever it *becomes* 0 it
  starts a 90 s timer, and every fresh write of 0 restarts the timer (the RegistersChanged
  event on the GX side, or simply polling the value at 5 s and noting the app's heartbeat via a
  small MQTT topic if the raw value can't distinguish a re-write). If the timer expires, the
  flow writes −1. Charging resumes by itself within two minutes of the app dying, the network
  dropping, or the PC sleeping. The app, on start, also calls `ForceNoLimit()` if it finds 2705
  at 0 and it wasn't the one that set it.
- Also decide: blanket "operating = inhibit" vs HF-only (band from CAT, see virtual-flex);
  SOC floor below which inhibit is refused (the bank also backs the PC — reserve threshold not
  yet chosen for 105 Ah).

Until the watchdog exists the inhibit is **not** exposed as a routine control. It is safe to
exercise on the bench with a person watching.

## Thursday 2026-09-10 checklist (hardware day)

1. Cerbo on the LAN, Ethernet (the VE.Bus port is RJ45 too — **blue cable only, never into a
   switch**). Give it a DHCP reservation; note the IP.
2. Shunt → VE.Direct port 1; MultiPlus → VE.Bus. Power up. Remote console reachable.
3. Enable **Modbus TCP** (Settings → Integrations) and read *Available services* — record the
   unit IDs here. Enable **DVCC** (Settings → DVCC) and confirm register 2705 reads −1.
4. HAMBENCH: `ShackPower.exe --cerbo <ip>` (or Setup → Connection → Cerbo GX) → **Find
   devices** → expect battery + inverter/charger lines with the unit IDs from step 3. Connect.
   Volts/amps/SOC should match the shunt's Bluetooth readout in VictronConnect; the CHARGER row
   should say `Float · on mains · +x.x A`.
5. Pull the MultiPlus's AC plug for ten seconds: CHARGER row goes `Inverting · MAINS LOST`
   in orange, the PC stays up. Plug back in.
6. Bench-test inhibit by hand (a person watching): write 2705 = 0 from Node-RED or a Modbus
   tool, confirm the MultiPlus's DC current goes to ~0 while AC out stays live, **listen on HF
   for whether the charger stage at 0 A is actually quiet**, then write −1. Only then build the
   watchdog flow.
7. Capture a few minutes of readings to `logs/` and compare the CSV against the same interval
   on the shunt's own history. Commit the unit IDs and any register surprises to this file.

## The Linux testbed

TestbedLinux (Fedora 44, 10.0.1.193) has both VE.Direct cables plugged in and the installed
Shack Power holding the shunt's cable — with nothing on the far end until the shunt is wired to
a battery again. That box stays on the *cable* source and is the regression target for the
serial path; the Cerbo path is verified from HAMBENCH. Sources of all family apps live on the
NAS at `/NAS/data/Hambench/Documents/Programming`.
