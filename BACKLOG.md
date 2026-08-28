# Backlog

Parked ideas, with reasoning, so no session re-litigates them cold.

- **Chart hover crosshair / zoom.** The sibling custom controls have no pointer input at all;
  this is genuinely new ground. v1 ships fixed windows (1 h / 6 h / 24 h) + day paging instead.
- **Passive VE.Direct detect.** Listening 3 s for a checksum-valid block is safe (the protocol
  is receive-only — contrast W2's Detect, which can key a radio). Unnecessary while the cable
  serial `VEAUI3T2A` pins the port; worth adding only if a second Victron device ever appears.
- **Alerts (toast/sound) on voltage thresholds or link loss.** Discussed pre-plan (2026-08-28);
  David deferred the scope decision. The color thresholds in Display settings are the v1 answer.
- **Flex TX-state tagging of log rows** (voltage-sag-during-transmit forensics). Deliberately out
  of v1; would follow LP-100A's rule of not growing radio-specific code (their answer was
  rigctld/MultiCAT upstream — the same debate applies here before building anything).
- **SOC/battery-monitor mode support is parsed but unexercised** — this station's shunt runs as a
  DC energy meter (`MON 1`). If it's ever reconfigured, the SOC/CE/TTG fields light up; the UI
  rows for them exist but have only been seen with `--sim` data.
- **Uninstall leaves the single-file extraction dir** (`%TEMP%\.net\ShackPower` / `$HOME/.net/…`)
  — inherited family-wide issue, documented at length in lp100a-monitor's CLAUDE.md. Fix belongs
  in the uninstall trampoline; fix Linux first if separated.
