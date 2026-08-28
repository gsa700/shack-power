# Changelog

## v0.1.1-beta — 2026-08-28

- Chart window: new **Combined** view (toolbar toggle, persisted) overlaying all three channels
  on one tall plot — each channel independently scaled to full height, with color-coded max/min
  range labels down the right edge; the stacked Split view remains
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
