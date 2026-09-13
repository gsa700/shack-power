# Shack Power (shack-power)

Cross-platform desktop monitor for a ham shack's Victron power system — live V/A/W readout,
battery SOC, charger state, daily CSV power logging, history charts. **.NET 10 + Avalonia
12.1.1**, MVVM. Windows / Linux / Raspberry Pi (arm64). GPLv3. By David Erickson (AB0R).

Third app in the station-tools family. **LP-100A Monitor** (`~/Documents/Programming/lp100a-monitor`)
is the family's reference template and **W2 Monitor** (`~/Documents/Programming/w2-monitor-x`) its
most refined descendant; this repo was ported from both (W2's installer/updater/config, LP's
single-meter service shape and CSV log pattern). Their CLAUDE.md files carry rationale that still
applies here — read them before "fixing" anything that looks odd.

This app replaced the Python prototype at `~/shack-power-monitor/shack_power_monitor.py` at the
2026-08-28 cutover. The prototype's daily CSVs were byte-compatible by design and were copied
into the data dir, so history is continuous across the handover; its folder is kept for reference.

## What this app IS — the rule that outranks every roadmap item (David, 2026-09-12)

**Shack Power is a lightweight, basic app that tells you what your DC power is doing.** One
window, three numbers, a chart, a CSV. That is the product, and the Cerbo/Modbus work is an
*additional source* for it, not a new identity. Protect this deliberately:

- **The cable mode is the baseline, not a legacy mode.** A SmartShunt on a VE.Direct USB cable
  with no hub must keep working exactly as v0.1.8 did, and it is the default on a fresh install.
  Every release is smoke-tested in `--sim` and judged against that: same window, same speed,
  same clarity.
- **Nothing hub-shaped is visible unless a hub is configured.** No CHARGER/SOLAR rows, no
  Modbus vocabulary, no extra tabs in the simple case. New capability arrives as an *optional row
  or panel that appears only when its data exists*, never as a mode the user must understand.
- **Stay lightweight.** No background services, schedulers, web servers, message brokers or
  databases inside this GUI. Anything that wants to run 24/7 without a window (watchdog
  heartbeats, exporters, SOC-window automation) is a separate headless project or a Node-RED
  flow on the GX — see BACKLOG. One small dependency added for the hub (FluentModbus); adding
  another needs a reason written here.
- **Don't fork it.** The temptation to spin off "the simple one" was considered and rejected
  2026-09-12: v0.1.8-beta is preserved as a tagged release, the seam keeps both sources honest in
  one binary, and a fourth repo would double the family's already-lagging shared maintenance.
  If the product ever stops satisfying the first sentence above, that is the moment to split
  by *shape* (desktop viewer vs headless agent), not by source.

## Scope and architecture (revised 2026-09-08/09)

**Shack Power monitors and automates shack-specific things; VictronConnect (phone, Bluetooth)
and the Cerbo's remote console configure.** Not a configurator, not a VRM/VictronConnect
replacement, not general Modbus tooling.

Two reading sources behind one seam (`IReadingSource` → `MeterService` → views/logging/chart):

- **Cerbo GX over Modbus TCP** — the current shack architecture. A Cerbo GX MKII owns the
  SmartShunt (VE.Direct), the MultiPlus 12/1200 (VE.Bus) and, in Phase 2, the MPPT; the app is a
  network client (`ShackPower.Core/Cerbo/*`). Everything about registers, unit IDs, the charge-
  inhibit knob and its unsolved watchdog, and the hardware-day checklist is in
  **`docs/cerbo-modbus.md`**. Why the system looks like this: **`docs/power-system.md`**.
- **VE.Direct USB cable on the PC** — the original design, kept for shacks without a hub, for
  the Linux testbed, and as the `--sim` baseline. Protocol notes below.

Setup → Connection has the Source radio; it applies on the next start (a `MeterService` is built
around one source). `--cerbo <host[:port]>` on the command line switches a run (and the saved
choice) to the hub. `--sim` never persists the source.

## Build / run / test

```sh
dotnet build                                     # needs the .NET 10 SDK (pinned in global.json)
dotnet run --project src/ShackPower.App          # run the app (needs a desktop/DISPLAY)
dotnet run --project src/ShackPower.App -- --sim # no hardware: synthetic SmartShunt data
dotnet run --project src/ShackPower.App -- --cerbo 10.0.1.x   # point at a Cerbo GX
dotnet test                                      # xUnit — all pure ShackPower.Core logic
```

Solution: `ShackPower.sln`. Output assembly is `ShackPower` (`ShackPower.exe` on Windows).

**Develop against `--sim`** (or the FluentModbus loopback server the Cerbo tests spin up —
`CerboReadingSourceTests` is the closest thing to hardware on a dev box). A live cable is held
by whichever monitor is live; never fight over it.

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
  ShackPower.Core/        # NO UI. Protocol + pure logic — this is where the tests live.
    Cerbo/                # Modbus TCP path: registers, decoder, reading source, discovery, ChargeInhibit
  ShackPower.App/         # Avalonia MVVM shell (Services/ ViewModels/ Views/ Controls/)
tests/ShackPower.Core.Tests/   # xUnit — Core only. Put new parsing/decision logic in Core with tests.
  Cerbo/                  # FakeCerboModbus + a real FluentModbus loopback server test
tools/IconGen/            # SVG -> .ico + 256px PNG (from LP-100A)
docs/                     # power-system.md (why), cerbo-modbus.md (how), screenshots/
```

**Design rule (family-wide):** all non-UI logic lives in `ShackPower.Core` and is unit-tested;
the App project is only the Avalonia shell. Third-party: `System.IO.Ports`, `FluentModbus` (MIT).

## Cerbo / Modbus gotchas (the ones that bite)

- **Units differ from VE.Direct**: SOC %×10 (not ‰), TTG seconds×0.01 (not minutes), vebus AC
  power W×0.1, `/ConsumedAmphours` scale −10. `CerboRegisters` is copied from Victron's
  `attributes.csv`; don't "correct" it from the VE.Direct table.
- **Unit IDs are dynamic** (Venus ≥ 2.60); unit 100 is always system/settings and mirrors the
  battery, so discovery skips it. Use Setup's Find devices or the GX's Available services list.
- **An unpublished register fails the whole read block** — hence the narrow reads in
  `CerboDecoder`. Modbus exceptions → null field; socket/IO errors → reconnect.
- **Charge inhibit = DVCC limit (2705) = 0**, never `/Mode` inverter-only (kills pass-through)
  and never the AC input. Needs DVCC on. **No GX-side watchdog exists yet** — do not expose the
  inhibit as a routine control until the Node-RED flow in `docs/cerbo-modbus.md` is built.

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
- A shunt in DC energy-meter mode (`MON 1`) reports `SOC`/`CE`/`TTG` as `---`; in battery-monitor
  mode (`MON 0`, the shack's shunt since 2026-09-07) they are live. Both must parse.
- Lines starting `:` are async HEX-protocol messages that can interleave; skip them after framing.
- Alarm reason `AR` is a bitmask: 1 low V, 2 high V, 4 low SOC, 8/16 low/high starter V,
  32/64 low/high temperature, 128 mid voltage. The Cerbo path maps its per-alarm registers onto
  the same bits so `DescribeAlarm` serves both.

## Cable identity (VE.Direct-USB path)

Each VE.Direct USB cable is an FTDI FT-X (`PID_6015`); pin by chip serial, never by COM number
(all COM numbers on this box changed across a Windows reinstall once):

| Serial | Device | Note |
|---|---|---|
| **`VEAUI3T2A`** | SmartShunt 300A | on TestbedLinux 10.0.1.193 since 2026-09-07 evening; moves to the Cerbo's VE.Direct port in the new design |
| **`VEB32G93A`** | SmartSolar MPPT 100/30 | on TestbedLinux with the shunt cable; Phase 2 hardware |

The station's other FTDI adapters, from W2 Monitor's table: `A10KMB4VA` W2 #1, `AG0JFX7UA` W2 #2,
`ABSCDI99A` LP-100A, `AD0JLU2FA` TM-V71A. **Never probe unknown adapters to identify them — two
of those are transmitters.** VE.Direct needs no probe anyway: the protocol is receive-only.

Also on Windows boxes: **VictronConnect holds every free COM port while open.** If ports look
taken, close VictronConnect before suspecting anything else. (Irrelevant on the Cerbo path.)

## Notes travel through this repo, not through memory

Claude's saved memory is per machine — this repo is the primary channel between sessions and
machines (HAMBENCH builds and releases; Techbench does design sessions and can edit docs; the
Linux testbed and a CM5/Pi run the app). Nothing about the project may live only in memory: it
goes in `CLAUDE.md`, `BACKLOG.md`, `CHANGELOG.md`, `docs/`, or a commit message. Write the
reasoning, not just the conclusion — the next session did not run the experiment. **Pull before
editing the shared docs; push when done** — the 2026-09-08 redesign sat only in Techbench's
memory for a day and this repo said the opposite. As a backstop, every machine's Claude memory
is readable (a day stale) from the NAS: `\\10.0.1.4\NAS_data\Hambench\_config\.claude\projects\…`
and `\\10.0.1.4\NAS_data\Techbench\.claude\projects\…` (hidden folders).

## Field notes from the Fedora box (from the W2 session, 2026-09-09)

Cross-project findings that apply here because all three station apps share the same Avalonia path.
Evidence lives in `w2-monitor-x/BACKLOG.md`; this is the pointer.

- **"Always on top": keep the checkbox visible everywhere and add a one-line note — do not hide it
  on Wayland.** Tested on three boxes: works on Windows and on **Fedora 44 / GNOME** (this app
  included, both set and unset), fails only on the Pi CM5 under labwc. It isn't a Wayland question: on
  GNOME the app runs as an X11 client under XWayland and Mutter honours `_NET_WM_STATE_ABOVE`; labwc's
  XWayland WM doesn't. There is no honest runtime test — an X11 client can set the hint but can't read
  back whether it took — so hiding the control on "Wayland" would take a working feature away from
  every GNOME user. *"May not take effect on some Linux desktops"* beside the checkbox is true, cheap,
  and wrong nowhere.
- **Fedora 44 x64 rendering is smooth and, by eye, indistinguishable from Windows.** W2 ran 45 h
  continuous there with no crash. First real data on Linux render performance for this toolkit stack.
- **A VE.Direct cable is enumerated on that box** — `usb-VictronEnergy_BV_VE_Direct_cable_VEAUI3T2`
  on `/dev/ttyUSB3` as of 2026-09-07 (`TestbedLinux`, 10.0.1.193, user `derickson`, SSH by key from
  HAMBENCH). Recorded only as "seen"; which device is behind it, and whether it's the shunt this app
  owns on COM13 here or a second cable, wasn't checked from the W2 session. **Resolved 2026-09-09
  (Shack Power session):** it is the shunt's cable — both Victron cables (`VEAUI3T2A` shunt,
  `VEB32G93A` MPPT) moved from HAMBENCH to the testbed on the evening of 2026-09-07, which is why
  COM13/COM14 are gone here; nothing is on their far ends until the shunt is wired to the battery
  again, and in the Cerbo design the shunt cable moves to the GX's VE.Direct port anyway.
- **Testbed handoff from Techbench (2026-09-07, `~/Documents/briefing-2026-09-07-techbench.md`,
  also on the NAS as `Loadbench\Documents\Testbed-Apps-Handoff-2026-09-07.md`):** all three apps
  self-installed and ran fine on Fedora 44 x64 (SELinux 0 denials, XWayland, .desktop valid). Items
  for this app: port picker should show the VE.Direct chip serial like W2's SerialDisplay; the
  self-installer leaves the original copy running; **SIGTERM is not handled** (a stray instance
  needed SIGKILL — logout/systemd stop use SIGTERM, treat it like a window close). Tracked in
  `BACKLOG.md`. The LP-100A pinning item in the same file belongs to that repo.

## Release workflow

`gh` is authed as `gsa700`; repo is `gsa700/shack-power`. A release = git tag + three
self-contained zips (`ShackPower-win-x64.zip`, `-linux-x64.zip`, `-linux-arm64.zip`) attached to
a **full "Latest"** GitHub release — `/releases/latest` excludes pre-releases, so a pre-release
is invisible to the in-app updater. `<1.0` versions carry `-beta`. Two ordering traps (learned by
W2): commit the version bump **before** publishing (binaries embed the sha), and smoke-test a
published single-file binary before uploading (build/run can't surface single-file breaks).
Update `CHANGELOG.md` every release. Release titles are the version only.

## Milestones

- **2026-08-28:** all six build phases landed, live cutover on COM13, eight releases to
  v0.1.8-beta (combined chart, progressive zoom, zoom buttons). Real raw capture committed as
  `tests/Fixtures/vedirect-capture.bin` (`RealCaptureTests`).
- **2026-09-09:** Cerbo GX Modbus TCP device layer built and tested hardware-free ahead of the
  2026-09-10 delivery; Source switch in Setup; CHARGER/SOLAR rows on the main window. Unreleased
  until verified on the real hub (see `docs/cerbo-modbus.md` → Thursday checklist).
- Still open: Linux/CM5 hardware pass, physical unplug/replug and sleep/resume checks on the
  cable path, tray interactive check.
