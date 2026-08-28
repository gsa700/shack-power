# Shack Power (shack-power)

Cross-platform desktop monitor for a **Victron SmartShunt 300A** over VE.Direct serial —
live V/A/W readout, daily CSV power logging, history charts. **.NET 10 + Avalonia 12.1.1**,
MVVM. Windows / Linux / Raspberry Pi (arm64). GPLv3. By David Erickson (AB0R).

Third app in the station-tools family. **LP-100A Monitor** (`~/Documents/Programming/lp100a-monitor`)
is the family's reference template and **W2 Monitor** (`~/Documents/Programming/w2-monitor-x`) its
most refined descendant; this repo was ported from both (W2's installer/updater/config, LP's
single-meter service shape and CSV log pattern). Their CLAUDE.md files carry rationale that still
applies here — read them before "fixing" anything that looks odd.

This app replaces the Python prototype at `~/shack-power-monitor/shack_power_monitor.py`
(VictronConnect itself was retired for monitoring because it grabs every free COM port). The
prototype keeps running and logging until cutover; its daily CSVs are **byte-compatible** with
this app's and are copied into the data dir at cutover.

## Build / run / test

```sh
dotnet build                                     # needs the .NET 10 SDK (pinned in global.json)
dotnet run --project src/ShackPower.App          # run the app (needs a desktop/DISPLAY)
dotnet run --project src/ShackPower.App -- --sim # no hardware: synthetic SmartShunt data
dotnet test                                      # xUnit — all pure ShackPower.Core logic
```

Solution: `ShackPower.sln`. Output assembly is `ShackPower` (`ShackPower.exe` on Windows).

**Develop against `--sim`.** The real shunt's port is held by whichever monitor is live (the
prototype until cutover, this app after) — never fight over COM13.

Publish a self-contained build (per platform):

```sh
dotnet publish src/ShackPower.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
# swap -r for linux-x64 or linux-arm64 (Raspberry Pi)
```

Icons regenerate from `assets/icon.svg` (never edit `app.ico`/`app-icon.png` by hand):

```sh
dotnet run --project tools/IconGen -- assets/icon.svg src/ShackPower.App/Assets/app.ico src/ShackPower.App/Assets/app-icon.png
```

## Layout

```
src/
  ShackPower.Core/  # NO UI. Protocol + pure logic — this is where the tests live.
  ShackPower.App/   # Avalonia MVVM shell (Services/ ViewModels/ Views/ Controls/)
tests/ShackPower.Core.Tests/   # xUnit — Core only. Put new parsing/decision logic in Core with tests.
tools/IconGen/    # SVG -> .ico + 256px PNG (from LP-100A)
```

**Design rule (family-wide):** all non-UI logic lives in `ShackPower.Core` and is unit-tested;
the App project is only the Avalonia shell.

## VE.Direct protocol (validated against the real SmartShunt, 2026-08-28)

- **19200 8N1, unsolicited broadcast** — the device streams; we never send anything. This is why
  `IReadingSource` has no `Send()` and the reader has no command queue (contrast both siblings).
- Text protocol: blocks of `LABEL<TAB>VALUE` lines separated by `\r\n`, each block ending
  `Checksum<TAB><byte>`; the sum of every byte in the block (checksum byte included) ≡ 0 mod 256.
  **The checksum byte is raw binary** and can legally be `\r`, `\n`, `\t`, or any other value —
  which is why `VeDirectFramer` works on bytes, not decoded strings.
- The shunt emits **two blocks per second**: main (`PID V I P CE SOC TTG Alarm AR BMV FW MON`) and
  history (`H1..H18`). Neither alone is a complete reading — `ReadingAccumulator` merges and emits
  once per main block, keeping the pipeline at 1 Hz.
- Units: `V`/`I` in mV/mA, `P` in W, `H17`/`H18` in 0.01 kWh (a probe once misread 225 as
  22.5 kWh — it is 2.25), `SOC` in ‰, `TTG` in minutes (−1 = infinite). `---` means "not
  available" and parses to null.
- **This station's shunt is configured as a DC energy meter (`MON 1`)**, so `SOC`/`CE`/`TTG` are
  `---` on this hardware. That is a device setting, not a fault; the fields must still parse for
  installs where the shunt is in battery-monitor mode.
- Lines starting `:` are async HEX-protocol messages that can interleave; skip them after framing.
- Alarm reason `AR` is a bitmask: 1 low V, 2 high V, 4 low SOC, 8/16 low/high starter V,
  32/64 low/high temperature, 128 mid voltage.

## Cable identity

The VE.Direct USB cable is an FTDI FT-X (`PID_6015`), chip serial **`VEAUI3T2A`** — pin by that,
never by COM number (all COM numbers on this box changed across a Windows reinstall once).
The station's other FTDI adapters, from W2 Monitor's table: `A10KMB4VA` W2 #1, `AG0JFX7UA` W2 #2,
`ABSCDI99A` LP-100A, `AD0JLU2FA` TM-V71A. **Never probe unknown adapters to identify them — two
of those are transmitters.** VE.Direct needs no probe anyway: the protocol is receive-only.

Also on this box: **VictronConnect holds every free COM port while open.** If ports look taken,
close VictronConnect before suspecting anything else.

## Notes travel through this repo, not through memory

Claude's saved memory does not cross machines — this repo is the only channel between sessions
(the CM5/Pi will work this repo too, like the siblings). Nothing about the project may live only
in memory: it goes in `CLAUDE.md`, `BACKLOG.md`, `CHANGELOG.md`, or a commit message. Write the
reasoning, not just the conclusion — the next session did not run the experiment. Pull before
editing the shared docs.

## Release workflow

`gh` is authed as `gsa700`; repo is `gsa700/shack-power`. A release = git tag + three
self-contained zips (`ShackPower-win-x64.zip`, `-linux-x64.zip`, `-linux-arm64.zip`) attached to
a **full "Latest"** GitHub release — `/releases/latest` excludes pre-releases, so a pre-release
is invisible to the in-app updater. `<1.0` versions carry `-beta`. Two ordering traps (learned by
W2): commit the version bump **before** publishing (binaries embed the sha), and smoke-test a
published single-file binary before uploading (build/run can't surface single-file breaks).
Update `CHANGELOG.md` every release.

## Build phases (2026-08-28 plan)

Phased build per the approved plan: 1 scaffold+plumbing (done), 2 VE.Direct core, 3 app shell in
`--sim`, 4 logging+Setup+tray, 5 strip chart + Chart window, 6 install/update/release/cutover.
Cutover is the only step that touches COM13, and includes capturing a real raw stream into test
fixtures (`tools/Capture-VeDirect.ps1`) — until then protocol fixtures are constructed per spec.
