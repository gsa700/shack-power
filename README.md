# Shack Power

Desktop monitor for a Victron SmartShunt over VE.Direct serial — live volts / amps / watts,
daily CSV power logging, and history charts. Windows, Linux, and Raspberry Pi (arm64).

Built because VictronConnect is a configuration tool, not a monitor: it grabs every free COM
port while open and offers no logging. Shack Power opens exactly one port (pinned to the
VE.Direct cable's chip serial, so COM renumbering doesn't matter), parses the SmartShunt's
1 Hz broadcast with checksum validation, and reconnects by itself across unplugs and sleep/resume.

Part of the AB0R station-tools family alongside
[W2 Monitor](https://github.com/gsa700/w2-monitor-x) and
[LP-100A Monitor](https://github.com/gsa700/lp100a-monitor), and shares their architecture:
.NET 10 + Avalonia, self-contained single-file releases, self-install (`--install` /
`--uninstall`), and an in-app updater.

## Features

- Live V / A / W cards in a VictronConnect-inspired look, with min/max voltage, cumulative kWh,
  and alarm decode; voltage colors warn at configurable thresholds
- Daily CSV logs (`power-YYYYMMDD.csv`) — archived, never deleted, and readable by anything
- History charts: live tail plus browsing past days
- Tabbed Setup: connection, logging, display, updates
- Optional minimize-to-tray
- `--sim` mode for running without hardware

## Building

Needs the .NET 10 SDK (`global.json` pins it).

```sh
dotnet build
dotnet run --project src/ShackPower.App -- --sim
dotnet test
```

GPLv3 — see [LICENSE](LICENSE). By David Erickson (AB0R).
