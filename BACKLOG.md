# Backlog

Parked ideas, with reasoning, so no session re-litigates them cold. The scope and the shack
power architecture these serve are in `docs/power-system.md` — read that first.

## Roadmap (ordered)

- **Multi-device VE.Direct support with roles.** The W2 Monitor fork of the family pattern:
  a manager owning N MeterServices (port + chip serial + **role**: Load / Battery / Supply /
  Charger), `Shunts[]` config list with legacy single-port migration, add/remove list in
  Setup's Connection tab. Role-aware presentation: battery section (SOC/TTG) appears when a
  Battery-role device exists; per-role daily CSVs (today's files become the Load history,
  untouched). A `MON 0` shunt is presumptively Battery, `MON 1` Load — suggest, let the user
  override. Sequencing question deliberately open: build against `--sim` before the second
  shunt exists, or wait for hardware. **MPPT charger field parsing (VPV/PPV/CS/ERR) is
  first-class here, not a side note** — the decided topology puts a SmartSolar 100/30 on
  VE.Direct in this shack, so the Charger role gets real hardware to develop against, and
  ON MAINS becomes the charger's own state field rather than an inference.
- **Charger control via local smart plug + SOC-window automation** (post multi-device). A
  local-HTTP smart plug (Shelly-class, no cloud) on the charger's AC cord; the app gains a
  Charger toggle ("quiet mode" — charger off while operating) and SOC-window charging: on
  below ~60%, off at ~90%, with a "top to 100%" button for storm-watch reserve. The smart
  shunt plus a dumb relay equals a smart charger, and implements the LiFePO4 don't-park-at-100%
  guidance. Control never goes through reverse-engineered BLE writes — shunt SOC in, plug
  relay out.
- **Alerts.** Key on **SOC thresholds** (and link-loss), never on "battery discharging" —
  discharge is this shack's normal operating mode (charger-off-on-the-air routine). Toast/sound
  when unattended; ties into the always-on-station-box watchdog idea from the pre-plan scope
  talk.
- **Combined chart, multi-device follow-up.** The v0.1.2 rebuild (VictronConnect Trends style:
  two channels, dual color-matched axes) resolved David's "still a mess" verdict on v0.1.1's
  three-band attempt — two-at-a-time is the load-bearing lesson. When multi-device lands, the
  channel pickers grow entries per device/role (battery amps, SOC, charger watts…); the
  two-channel constraint stays.

## Smaller / standing

- **Release v0.1.2-beta**: the sim-logging isolation fix (`--sim` logs to `logs-sim`, commit
  eb78350) is merged but unreleased.
- **Sim-row cleanup decision (David's call, never-delete policy):** the real
  `power-20260828.csv` carries interleaved synthetic rows from dev sim sessions ~18:29–18:58
  (transmit-shaped dips to −21 A that never happened). Options: leave it as one known-messy
  day, or filter by stated heuristic with the original archived aside.
- **Chart hover crosshair on touch / keyboard** — pointer-only today.
- **Passive VE.Direct detect.** Listening 3 s for a checksum-valid block is safe (receive-only
  protocol — contrast W2's Detect, which can key a radio). Unnecessary while cable serials pin
  ports; revisit with multi-device.
- **SOC/battery-monitor fields are parsed but not yet exercised on real hardware** — this station's
  shunt runs as a DC meter. The planned battery shunt lights them up for real.
- **Uninstall leaves the single-file extraction dir** (`%TEMP%\.net\ShackPower` / `$HOME/.net/…`)
  — inherited family-wide issue, documented in lp100a-monitor's CLAUDE.md. Fix belongs in the
  uninstall trampoline; fix Linux first if separated.
- **Linux/CM5 hardware pass** — cross-published, never run on hardware; install/tray/serial
  all unproven there.
- **Auto-start with Windows** — undecided; a Startup shortcut is the mechanism when wanted.
