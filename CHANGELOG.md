# Changelog

## v0.1.7-beta — 2026-08-28

- The zoom pair became round Victron-blue buttons floating over the chart's bottom-right
  corner (VictronConnect-style), replacing the toolbar buttons from v0.1.6

## v0.1.6-beta — 2026-08-28

- Zoom − / + buttons in the chart toolbar (center-anchored, chunkier steps than the wheel)

## v0.1.5-beta — 2026-08-28

- Progressive zoom and pan on both chart views: mouse wheel zooms ~30% per notch, from the
  full day down to a one-minute window (60 real points at 1 Hz). Live view stays pinned to
  now and zooms its tail length; browsing a past day, zoom anchors at the cursor and drag
  pans through the day. Presets remain as quick jumps; time labels gain seconds below ten
  minutes

## v0.1.4-beta — 2026-08-28

- Main window: the status line is gone — the connection dot is the whole story at a glance,
  its hover tooltip carries the detail (port, reconnecting, no data), and Setup has the rest

## v0.1.3-beta — 2026-08-28

- The hover readout on both chart views now always shows all three channels (V / A / W, each
  in its fixed color) at the cursor's moment, regardless of which channels are on display —
  every channel is decimated on every refresh anyway, so the tooltip simply stopped hiding
  what it already knew

## v0.1.2-beta — 2026-08-28

- **Combined chart view rebuilt as VictronConnect Trends** (modelled on a screenshot of the real
  app): two channels at a time, chosen from color-matched dropdown pickers, overlaid full-height
  with the primary's value axis down the left and the secondary's down the right, each axis in
  its trace's color, sharing one set of gridlines. Channel picks persist. The three-band
  arrangement from v0.1.1 is gone — two-at-a-time is what keeps it readable. Split view
  unchanged.
- `--sim` runs now log to a separate `logs-sim` folder — synthetic rows can no longer mix into
  real operating history (dev sim sessions had salted the live CSV on cutover day)
- Main window header reads "DC POWER" instead of repeating the app name from the title bar

## v0.1.1-beta — 2026-08-28

- Chart window: new **Combined** view (toolbar toggle, persisted) putting all three channels on
  one tall plot in VictronConnect's trends arrangement — volts anchored to the top band, amps to
  the bottom, watts between, each independently scaled so the traces grow toward one another
  under load. Each band carries a real value scale in its channel's color (labels and faint
  gridlines), per the VictronConnect trends treatment; the stacked Split view remains
- Hover crosshair on both chart views: a dashed cursor line with a readout box showing the time
  and the value(s) under the cursor, color-coded per channel; decimated buckets show their
  min…max envelope instead of pretending to a single value; no readout inside data gaps
- Split view y-axis labels now carry the decimals the gridline step needs (a 13.84–13.99 V axis
  used to read as a wall of "14"s)
- Fixed a crash when a chart rendered mid-layout at near-zero size (label clamp inverted its
  bounds; caught by the ported crash log on its first day)

## v0.1.0-beta — 2026-08-28

First release of the .NET/Avalonia app, replacing the Python prototype for monitoring the
station's SmartShunt 300A.

- Live V / A / W readout in a VictronConnect-homage look (colors sampled from the real app),
  with min/max voltage, cumulative kWh, alarm decode, and configurable voltage color thresholds
- VE.Direct text protocol over serial (19200, receive-only), byte-level checksum framing, the
  family's self-supervising serial reader (auto-reconnect across unplug and sleep/resume), and
  FTDI chip-serial port pinning
- Daily CSV power logs (`power-YYYYMMDD.csv`, 1 Hz), byte-compatible with the prototype's files;
  archives aside on schema change, never deletes
- Chart window: min/max strip charts for volts/amps/watts, live tail plus day-by-day browsing,
  1 h / 6 h / 24 h windows
- Tabbed Setup (Connection / Logging / Display / Updates), minimize-to-tray option
- Self-install (`--install` / `--uninstall`), in-app updater against GitHub releases, `--sim`
  mode for running without hardware
- 236 unit tests over the UI-free core

Windows verified end to end on the station; linux-x64 / linux-arm64 are cross-published but not
yet run on hardware (the CM5 pass is pending, as with the sibling apps' first releases).
