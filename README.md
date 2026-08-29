# Shack Power

Desktop monitor for a Victron SmartShunt over VE.Direct serial — live volts / amps / watts,
daily CSV power logging, and history charts. Windows, Linux, and Raspberry Pi (arm64).

Built because VictronConnect is a configuration tool, not a monitor: it grabs every free COM
port while open and offers no logging. Shack Power opens exactly one port (pinned to the
VE.Direct cable's chip serial, so COM renumbering doesn't matter), parses the SmartShunt's
1 Hz broadcast with checksum validation, and reconnects by itself across unplugs and sleep/resume.

**Scope:** Shack Power monitors; VictronConnect (on your phone, over Bluetooth — they coexist)
configures. The baseline is one SmartShunt in DC energy-meter mode watching the station supply.
For shacks with battery backup, the roadmap adds multiple VE.Direct devices by role — a
battery-monitor shunt bringing state-of-charge and time-to-go, and eventually charge management.
The recommended backup topology this serves (AC charger → LiFePO4 → loads, no combiner, charger
off while operating for total RF silence) is written up in
[docs/power-system.md](docs/power-system.md).

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
- `--sim` mode: the full app on a synthetic SmartShunt — no hardware, no serial port, and its
  logs go to a separate `logs-sim` folder so demo data can never mix into real history

## Building

Needs the .NET 10 SDK (`global.json` pins it).

```sh
dotnet build
dotnet run --project src/ShackPower.App -- --sim
dotnet test
```

GPLv3 — see [LICENSE](LICENSE). By David Erickson (AB0R).
