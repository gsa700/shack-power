# Backlog

Parked ideas, with reasoning, so no session re-litigates them cold. The scope and the shack
power architecture these serve are in `docs/power-system.md` (why) and `docs/cerbo-modbus.md`
(how the app talks to the Cerbo GX) — read those first. Revised 2026-09-09 for the Cerbo/Modbus
architecture; the pre-pivot items are at the bottom for the record.

## Roadmap (ordered)

1. **Hardware-day verification (2026-09-10).** Run the checklist in `docs/cerbo-modbus.md`:
   Modbus TCP + DVCC enabled on the GX, Find devices discovers the shunt and the MultiPlus,
   readings match VictronConnect, CHARGER row flips to `Inverting · MAINS LOST` on an AC pull.
   Record unit IDs and any register surprises in that doc. Then release **v0.2.0-beta**.
2. **Charge-inhibit fail-safe (blocks exposing "quiet mode").** `ChargeInhibit` (DVCC 2705 = 0)
   exists and is tested, but a register write has no timeout. Build the GX-side watchdog as a
   Node-RED flow on Venus OS Large (restore −1 if the app's 30 s re-assert stops for ~90 s),
   verify on the bench with a person watching, **listen on HF for whether the charger stage at
   0 A is actually quiet**, then add the Quiet button + auto "operating = inhibit". Decide
   blanket vs HF-only (band via CAT, see virtual-flex) and the SOC floor below which inhibit is
   refused — the 105 Ah bank also backs the PC.
3. **SOC-window charging + forced sync charge.** Hold 85–90 % day-to-day via the DVCC limit,
   top to 100 % on a schedule (shunt resync — coulomb counters drift) or on a "storm watch"
   button; never while operating. Sync on the shunt's synchronised flag, not the charger's
   float state. Thresholds re-decided for 105 Ah with the PC-backup reserve in mind.
4. **Alerts.** Key on **SOC thresholds** and link loss, never on "discharging" — discharge is
   normal here. Toast/sound when unattended.
5. **Grafana route.** Lean: a separate Modbus-to-Prometheus exporter polling the Cerbo (the app
   stays out of the monitoring path; Prometheus is on NetMon 10.0.1.23). Alternative: the app
   exposes a small metrics endpoint. Decide after hardware day.
6. **Phase 2 solar.** When the 450 W panel is bought: MPPT on the Cerbo's VE.Direct port, SOLAR
   row already renders when a solarcharger unit is configured; extend the CSV (PV power) and the
   chart pickers (PV watts, charger amps, SOC — two-at-a-time constraint stays).
7. **CSV columns for the Cerbo era.** Today's `timestamp,volts,amps,watts` stays byte-compatible;
   add an optional sibling file or new columns (soc, charger_state, ac_in, pv_w) without
   breaking the reader. Decide format before hardware day's first real logging.

## Smaller / standing

- **"Open Cerbo console" button (David, 2026-09-12).** The GX's remote console web page already
  covers most configuration and general monitoring; the app's job is the shack-specific view.
  Keep the current layout and add one button (main window or Setup → Connection, shown only when
  a Cerbo host is configured) that opens `http://<cerbo-host>/` in the default browser via
  `Process.Start(UseShellExecute)`. **No embedded WebView** — that would break the lightweight
  rule for something the real browser does better. Direction, not a decision: nothing hard gets
  decided until the gear is wired and we see how it feels.

- **SIGTERM handling on Linux (testbed handoff 2026-09-07):** a stray instance did not exit
  within 2 s of SIGTERM and needed SIGKILL. Logout and `systemd stop` send SIGTERM; handle it
  like a window close (flush config, release the port/socket, exit). `PosixSignalRegistration`
  for SIGTERM/SIGINT in Program.cs → `Dispatcher.UIThread.Post(mainWindow.Close)`. Family-wide.
- **"Always on top" note beside the checkbox (W2 field notes 2026-09-09):** keep the checkbox
  everywhere, add *"May not take effect on some Linux desktops"* — works on Windows and Fedora
  GNOME (XWayland honours `_NET_WM_STATE_ABOVE`), fails only under labwc on the CM5; no honest
  runtime test exists, so don't hide it.
- **Installer UX (from the 2026-09-07 testbed review):** after self-install, start the
  installed copy and exit the original — today the Downloads copy stays alive with no port.
  Family-wide; fix here first, port to the siblings.
- **Cable-path port picker lacks W2's Detect/serial display** (same review). Passive VE.Direct
  detect (listen 3 s for a checksum-valid block) is safe — receive-only protocol. Low priority
  now that the shack's shunt lives on the Cerbo; still useful on the Linux testbed.
- **Sim-row cleanup decision (David's call, never-delete policy):** the real
  `power-20260828.csv` carries interleaved synthetic rows from dev sim sessions ~18:29–18:58.
  Options: leave it as one known-messy day, or filter by stated heuristic with the original
  archived aside.
- **Chart hover crosshair on touch / keyboard** — pointer-only today.
- **Uninstall leaves the single-file extraction dir** (`%TEMP%\.net\ShackPower` / `$HOME/.net/…`)
  — inherited family-wide issue, documented in lp100a-monitor's CLAUDE.md.
- **Linux/CM5 hardware pass** — the testbed (10.0.1.193) has the installed app holding the
  shunt cable; the Cerbo path is untested on Linux.
- **Auto-start with Windows** — undecided; a Startup shortcut is the mechanism when wanted.

## Superseded on 2026-09-08 (kept for the record)

- **Multi-device VE.Direct-USB with roles** (N cables pinned by serial, a manager owning N
  MeterServices). The Cerbo makes this one Modbus connection with per-service unit IDs instead;
  the *role* idea survives as service kind. If a no-hub multi-cable shack ever needs it, the
  design is in the git history of this file.
- **Smart-plug charger control** (Shelly-class local HTTP on the charger's AC cord). Replaced by
  the DVCC register write — but its hardware `auto_on` fail-safe is exactly what item 2 above
  has to rebuild on the GX side.
