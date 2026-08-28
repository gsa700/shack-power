# Changelog

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
