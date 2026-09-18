# The shack power system this app is designed around

First decided 2026-08-28 (David + Claude), **substantially redesigned 2026-09-08** in a Techbench
session and ported into this repo 2026-09-09. The reasoning is recorded so a later session (or a
later David) doesn't re-litigate it cold. The app-side protocol details for the current design
live in `cerbo-modbus.md`; the 2026-08-28 design is kept below as history because its bench log
explains hardware that is still in the shack.

## Scope statement

**Shack Power monitors; VictronConnect (phone, Bluetooth) and the Cerbo's remote console
configure.** This app is real-time monitoring plus shack-specific automation of a ham shack's
power system — not a Victron ecosystem tool that happens to run in a shack, never a device
configurator, and not a VictronConnect or VRM replacement.

- **Data path (since 2026-09-08):** a **Cerbo GX MKII** owns the Victron devices; the app is a
  Modbus TCP client of it (see `cerbo-modbus.md`). The original VE.Direct-USB-cable path stays
  as the no-hub / Linux-testbed / `--sim` mode.
- **Deliberately out:** device configuration, Bluetooth, the wider Victron dashboard (VRM does
  that), and general Modbus tooling.

## Phase 1 topology (building 2026-09-10 — MultiPlus + Cerbo arriving)

```
mains AC ──> MultiPlus 12/1200/50-16 ──AC out──> rack power conditioner (PC, one LG 4K, dbx, MOTU, RT-21D…)
                 │ charger / inverter / ATS           (backup circuit; other 4K + subwoofer stay on house AC)
                 │ 4 AWG, 100 A MRBF
        Blue Sea 2151 dual MRBF block on the battery + post
                 │ 8 AWG, 40 A MRBF
     Epoch 105 Ah LiFePO4 ──> SmartShunt (battery leg, battery-monitor mode) ──> station DC bus (~30 A max)

     Cerbo GX MKII ── VE.Direct ── SmartShunt        Cerbo ── Ethernet ── LAN ── Shack Power (Modbus TCP),
                  ── VE.Bus (blue cable) ── MultiPlus                             Grafana, VRM
```

- **Charger + PC backup in one box: Victron MultiPlus 12/1200/50-16** (1200 VA, 50 A charger,
  16 A transfer switch, VE.Bus). Normally passes mains through and charges; on an outage it
  inverts for the conditioner's loads (~650 W worst case, ~1100 W if the rotator turns). Chosen
  over a plain Phoenix inverter because backup-only + stays-on-mains needs an ATS; MultiPlus II
  rejected (ESS/grid features irrelevant, smallest 12 V one is 3 kVA class).
- **Battery: Epoch 12 V 105 Ah Essential** (heated, Bluetooth; 105 A / 100 A BMS, 14.2–14.4 V
  absorption). Sized from a week of shunt logs (~1.3 A standby ≈ 40 Ah/day, ~5–6 A operating):
  ~17 h operating or ~1.6 days total autonomy if mains *and* charger were both gone. Shares the
  bank between the station and the PC backup — the minimum-reserve SOC for backup duty is an
  open decision.
- **Protection:** MRBF at the battery post (Blue Sea 2151: 100 A MultiPlus leg, 40 A bus leg),
  MRCB where a resettable breaker is wanted — **not Class T** (David's marine standard, matches
  the boat). Size to protect the cable, not the load: the MultiPlus draws ~101 A at nameplate,
  right at the Epoch's 105 A BMS ceiling, so 4 AWG + 100 A is a "size to real load (~46 A)"
  call, 2 AWG on the next wire order.
- **Hub: Cerbo GX MKII.** Shunt on VE.Direct, MultiPlus on VE.Bus, Ethernet to the LAN. Chosen
  over an MK3-USB because David wants VRM/remote console *and* Grafana, and the Cerbo is a
  strict superset. **VE.Bus is RJ45 but is not Ethernet — never into a switch**; the blue
  Victron cable is the visual guard.
- **Charge profile:** 14.2 V absorption / 13.5 V float (Victron's LiFePO4 numbers), short
  absorption time; temperature compensation off. The shunt's aux input carries the ordered
  temperature sensor (low-temp cutoff second opinion; relative-to-ambient drift is the app's
  early-warning opportunity).
- **RF-quiet operating:** inhibit the *charger* while on the air, via DVCC's charge-current
  limit through the Cerbo — never by cutting the MultiPlus's AC input (that fails it over to
  inverting and drains the bank into the PC). Fail-safe watchdog still to be built; details and
  the open questions are in `cerbo-modbus.md`.
- **Grounding:** the MultiPlus's chassis/PE lug bonds to the station's single-point ground bus
  (which is tied to the house service ground via the entry panel and rod), decided 2026-09-08.

## Phase 2 (solar)

**Panel chosen 2026-09-18: one JJN 425 W bifacial N-type (UL 61730), ~$279 shipped.** Voc 38.59 V,
Vmp 32.15 V, Isc 13.81 A, Imp 13.22 A, 67.8 x 44.7 in, 53.6 lb, MC4. One panel fills the MPPT
100/30 almost exactly (the controller clips at ~440 W on a 12 V bank; PV input limit 100 V / 35 A
Isc). Expected harvest in Saint Paul: ~500-600 Wh on a December day, 1,600+ Wh in summer, against
~800 Wh/day of station load - solar carries most of the year, the MultiPlus covers the dark weeks.
**One now, by decision: watch a winter of real harvest before buying a second.** A second panel is
mostly clipped and only earns in weak light; it never pays back against mains, so the only reasons
are winter-outage independence or wanting a matched pair while the model is still sold. If added:
**parallel** (27.6 A Isc, under the 35 A limit). Series is ~88 V at -30 C by estimate (typical
N-type -0.25 %/C; JJN publishes no coefficient) - inside 100 V but not a margin to lean on.
Bifacial gain needs a tilted mount with rear clearance; flush on a roof it is a heavy monofacial
panel. Inspect both glass faces on delivery and check ~38 V open-circuit at the MC4 leads.

**Mount plan (David, 2026-09-18): angled roof brackets, tilted for sun angle**, so the rear face
sees the roof - and in winter, snow (albedo ~0.8 vs ~0.1-0.2 for shingles), which is when the
front face is weakest. What decides the gain: clearance under the low edge (more air = more rear
light), a steep winter-biased tilt (~60 deg at 45 N; also sheds snow), brackets lagged into rafters
with flashed penetrations (wind uplift is the real load). Cold bright snowy days may exceed 425 W;
the 100/30 clips at ~440 W and reflection adds current, not volts. Bond frame and mount to the
station single-point ground; keep the PV pair together and ferrite it at the MPPT - next to a
full-power station those leads are an antenna.
**The roof is FLAT** (best case: wide rear view, and it holds its snow all winter). So: low edge
above the normal snow depth (drifted snow shades the bottom cells and blocks rear light),
membrane-rated sealant at every bracket foot and screws into joists, ground to the rods directly
below the eave (6 AWG, short and straight; they tie to the service ground). Install notes: panel
is live in daylight - MC4s unplugged until the MPPT end is terminated, battery side first; DC
disconnect in the PV run near the MPPT; 10 AWG PV wire to ~50 ft one way, 8 AWG beyond; the MPPT's
battery lead needs its own ~40 A fuse (the 2151 block is full). **Panel ordered 2026-09-18,
due Mon 2026-09-21; install the following week.** Placement advice given (David's call): the roof
has a 2 ft eave overhang - keep the brackets off it (cantilevered framing, worst wind zone) and put
the front feet over the outside wall line, where the joists bear; landscape orientation cuts the
sail height from ~59 in to ~39 in at 60 deg.


Add `panel → SmartSolar MPPT 100/30 → battery`, the MPPT on another Cerbo VE.Direct port. Solar
becomes a second, weather-dependent charge source; the same DVCC limit inhibits it. The original rule — **never two ~50 V-Voc 450 W panels in series** (cold-morning Voc ~58 V each
puts ~115 V into a 100 V controller) — still stands for that class of panel; see above for
this one. One panel, or two in parallel. The MPPT needs its own protection point (the 2151 is dual-circuit).
MPPT firmware ≥ 1.39. Enable VE.Smart networking between shunt and MPPT.

## Load numbers (measured)

- Station powered up: **~6 A @ 13.9 V ≈ 83 W**. Standby (most gear off): **23 W ≈ 1.65 A**,
  24/7, the dominant daily cost (~40 Ah/day). AllStar node is on homelab PoE, not this budget.
- Working figure ~800 Wh/day station-only; PC backup draw is outage-only.

## LiFePO4 care and the SOC-drift problem

Shallower DOD nominally buys cycles, but at this duty the pack dies of calendar aging first —
and LiFePO4 calendar-ages fastest **parked at 100 %**. Charger-off-while-operating is therefore
near-optimal by accident. What ages cells is time *at absorption voltage*, so tune absorption
time, not float.

The shunt is a coulomb counter with no voltage reference across 30–80 % SOC; it **only resyncs on
a genuine full charge** (charged-voltage + tail-current). Drift compounds silently, so any
SOC-triggered automation needs a scheduled forced full charge — easy in Phase 1 (the MultiPlus
charges on demand), scheduled for times the station isn't in use because inhibit and sync-charge
can't overlap. Sync on the *shunt's* synchronised/100 % flag, never on the charger's float state.

## What this means for the app

- **Roles, not arithmetic.** Each device has a role from what it is on the hub (battery service
  = Battery, vebus = Charger/Inverter, solarcharger = Solar). Load = charger current − net
  battery current; no blind summation.
- **Battery discharge is not an alarm** — in this shack it's the normal operating mode. Alerting
  (when built) keys on **SOC thresholds** and link loss, and behaves the same whether the charger
  is off by choice or mains actually failed.
- **Mains state is explicit, not inferred**: the vebus `/State` register says Inverting, and
  `/Ac/ActiveIn/ActiveInput` = 240 says disconnected.
- **Charger control goes through the hub's documented Modbus register**, with a GX-side
  watchdog, never through reverse-engineered Bluetooth writes and never through the AC input.
- **Grafana:** a separate Modbus-to-Prometheus exporter polling the Cerbo (so the app is not in
  the monitoring path) vs. the app exposing metrics — undecided; lean exporter.

---

## History: the 2026-08-28 design (superseded 2026-09-08)

```
mains AC ──[smart plug]──> Mean Well LRS-600-48 ──> SmartSolar MPPT 100/30 ──> LiFePO4 ──> loads
                                                          │ VE.Direct USB → the app     │ SmartShunt (USB → the app)
```

A DC-coupled, combiner-free UPS: a 48 V brick spoofing a panel into the MPPT so mains could
charge; loads on the battery bus permanently; smart plug on the brick's AC cord as the RF-quiet
button and future SOC-window automation; the app owning both VE.Direct USB cables directly with
roles (Load / Battery / Charger). Why it lost: David also wanted PC/rack backup, which needs an
inverter with a transfer switch — and once a MultiPlus is in the picture its own 50 A charger
makes the brick+MPPT-as-charger redundant, while a Cerbo GX gives VRM + Grafana + one network
interface for everything. **The MPPT and the LRS are still on hand**: the MPPT is Phase 2's
solar controller; the LRS-600-48 is spare.

### Bench log, 2026-09-07 (first wiring attempt under the old design)

- MPPT `PID 0xA056`, FW 1.74, VE.Direct cable serial `VEB32G93A` (COM14 on HAMBENCH).
  Configured battery-first over Bluetooth per Victron's instructions.
- **LRS-600-48 arrived with a marginal 115/230 selector** — dark until the switch was worked
  vigorously. It carries full mains current in the 115 position; contact cleaner if it recurs.
- **LRS trips instantly when the MPPT's PV input is connected**: spark at the Anderson, DC-OK
  LED out, recovers on unplug. A 90 s VE.Direct capture during a hot-plug showed `VPV` never
  above 0.04 V — the supply collapsed before the MPPT saw one frame (`ERR 0`, `OR 0x1`). Cause:
  the MPPT's PV-input capacitor bank looks like a short for a few ms and the LRS's hiccup-mode
  overload protection latches. No MPPT setting affects it. An NTC inrush limiter (Ametherm
  SL32 5R020) was ordered as the fix and is now moot — noted here because it is the answer if a
  bench supply ever feeds the MPPT again (HRP-series supplies with constant-current limiting
  avoid the problem outright).
- **Shunt wiring lesson (still true):** the shunt measures only current crossing BATTERY MINUS →
  SYSTEM MINUS. Charger negative on the *system* side or charge current is invisible; with no
  battery the supply's negative takes the BATTERY MINUS post or the shunt reads 0.000 A.
- **The Astron RS-35M linear died** (transformer/cap noise, RF hash) — third supply lost this
  year after two Samlex SEC-1235M; mains quality or heat suspected, the homelab UPS's line log
  is the instrument. Interim: Astron SS-50 switcher on the bus, trimmed toward 13.8 V. All bench
  wiring is 10 AWG with PP45 contacts.

### Alternatives considered on 2026-08-28 and why not (kept for the record)

- **West Mountain PWRgate / FET combiner** — RF-silent and simple, but dumb: no profiles, no
  current limit, no visibility. Rejected on flexibility.
- **Blue Smart IP67 12/25** — the near-pick then; lost to the MPPT+brick on VE.Direct telemetry.
- **Orion XS DC-DC, Phoenix Smart IP43** — fine, overkill; David runs both on the boat.
- **Inverter/charger (MultiPlus-style)** — rejected *then* as answering a different question.
  It became the answer once PC backup entered scope on 2026-09-08.
