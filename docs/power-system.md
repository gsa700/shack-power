# The shack power system this app is designed around

Decided 2026-08-28, David + Claude, after walking the alternatives. This is the reference
architecture Shack Power's roadmap assumes; the reasoning is recorded so a later session (or a
later David) doesn't re-litigate it cold.

## Scope statement

**Shack Power monitors; VictronConnect configures.** This app is real-time monitoring of a ham
shack's DC power system — not a Victron ecosystem tool that happens to run in a shack, and never
a device configurator. Configure shunts and chargers with VictronConnect on a phone over
Bluetooth (which coexists fine with this app's serial connection — verified live 2026-08-28).

- **Baseline (v1, running today):** one SmartShunt 300A in DC energy-meter mode (`MON 1`) on a
  VE.Direct USB cable, watching the station's 13.8 V bus. For most hams this is the whole story.
- **Growth path:** battery backup arrives → a shunt in battery-monitor mode on the battery, and
  the app grows to N VE.Direct devices distinguished by **role** (Load / Battery / Supply /
  Charger), pinned by cable chip serial. Roles carry the meaning; no blind summation.
- **Deliberately out:** device configuration, Bluetooth, AC-side/inverter monitoring, the wider
  Victron ecosystem (MPPT/Cerbo dashboards) unless a real need shows up.

## Recommended backup topology: DC-coupled, combiner-free (decided 2026-08-28)

```
mains AC ──[smart plug*]──> Mean Well LRS-600-48 ──> SmartSolar MPPT 100/30 ──> LiFePO4 ──> loads
                            (48 V, 600 W, fanless)        │ VE.Direct              │
                                                          └──> the app          SmartShunt
                                                                        (battery-monitor mode)
```
\* smart plug is the future charger-control hook — see BACKLOG.

Loads live on the battery bus permanently, so a mains failure is a non-event: nothing switches,
nothing drops. The charger carries the steady load and recharge; the battery buffers TX peaks.

- **Charger: Victron SmartSolar MPPT 100/30, fed by a Mean Well LRS-600-48 on the PV input**
  (decided 2026-08-28, superseding the 24 V/350 W first cut). The MPPT doesn't care that the
  "panel" is a power brick — but it does need the input ~5 V above *battery* voltage to start
  (a 13.8 V shack PSU can never drive it), and 100 V is the PV-input *maximum*, not a
  requirement. Why 48 V over 24 V: it keeps a future **24 V battery bank** possible (a 24 V
  bank absorbing at ~28.8 V needs 34 V+ input, which kills a 24 V feed), and halves the input
  current; the slightly larger buck ratio costs a negligible point of efficiency. Why 600 W:
  the MPPT's full 30 A at 14.4 V absorption is ~455 W of input, and ~30% headroom keeps the
  tracker from ever dragging the supply into current-limit foldback — the one failure mode of
  the PSU-as-panel arrangement. The LRS-600 is free-air convection like the LRS-350: **no fan,
  silent** — give the case some convection room and don't entomb it at full load. Do NOT
  parallel non-current-sharing supplies to get power; one bigger unit (or a series stack of
  identical ones) is the correct shape. Charge current capped in VictronConnect at the
  battery's rating (the 40 Ah Bioenno wants ≤20 A; the full 30 A waits for the bigger bank).
  Why an MPPT over a one-box AC charger: **it has VE.Direct**, so the app sees charge state
  (bulk/absorption/float), input power and errors directly — and real panels can land on the
  PV input someday with zero re-architecture.
- **Battery:** Bioenno 40 Ah LiFePO4 now (~6+ h at the bench's ~6 A draw); **Epoch 12 V 105 Ah
  Essential (heated, Bluetooth) ordered 2026-09-04** to replace it — 105 A / 100 A BMS,
  14.2–14.4 V absorption, float preferably off (the MPPT's floor is ~13.4 V). Sized from a
  week of logs: ~1.3 A standby, ~5 A operating → ~17 h operating / 2.5–3 days idle at 85 % DOD.
  When it lands, raise the MPPT charge cap from the Bioenno's value to 30 A.
- **RF-silent operating mode:** charger chain OFF while on the air — pure battery is the
  quietest possible source, zero switching hash by construction. Charger on between sessions.
  This is the intended routine, not a workaround. (The chain is two switchers — brick + MPPT —
  which is exactly why the off-while-operating routine, and one smart plug kills both.)

### Bench log

- **2026-09-07 — first wiring attempt.** MPPT `PID 0xA056`, FW 1.74, cable serial `VEB32G93A`
  (COM14 on HAMBENCH). Configured battery-first over Bluetooth per Victron's instructions.
  - **LRS-600-48 arrived with a marginal 115/230 selector** — dark on first power-up (shipped at
    230, set to 115, still dark) until the switch was worked vigorously. If it ever goes dark
    under load again, contact cleaner into that switch; it carries the full mains current in
    the 115 position.
  - **LRS trips instantly when the MPPT is connected** — spark at the Anderson, DC-OK LED out,
    recovers the moment the MPPT is unplugged. Same result whether the MPPT is hot-plugged or
    already connected at switch-on. A 90 s VE.Direct capture during a hot-plug showed `VPV`
    never exceeding 0.04 V, i.e. the supply collapsed before the MPPT saw a single 1 Hz frame;
    `ERR 0`, `OR 0x1` (no input power). Diagnosis: the MPPT's PV-input capacitor bank looks
    like a short for a few ms and the LRS's **hiccup-mode overload protection** latches on it —
    a known LRS-series weakness with capacitive loads. No MPPT setting can affect this.
    **Fix ordered: Ametherm SL32 5R020 NTC inrush limiter** (5 Ω cold → ~10 A peak, 20 A
    steady) in series with PV+ at the MPPT-side Anderson, in free air; let it cool ~1 min
    before re-plugging. Fallback if the NTC isn't enough: exchange the LRS for a Mean Well
    HRP-600-48 (constant-current limiting, not hiccup) inside the Amazon return window
    (~2026-09-27). Cutting losses (SS-50 + battery in parallel, PWRgate-style) was rejected: it
    forfeits controlled charge current, the 14.2 V balance top-off, and the charger telemetry
    the multi-device app work depends on.
  - **Shunt wiring lesson:** the shunt measures only current crossing BATTERY MINUS → SYSTEM
    MINUS. Charger negative must land on the *system* side (bus bar) or charge current is
    invisible; with no battery, the supply's negative takes the BATTERY MINUS post or the
    shunt reads exactly 0.000 A. Shunt was in battery-monitor mode during the battery run
    (SOC/TTG showed in the app) and returned to DC-monitor mode on the supply.
  - **The Astron RS-35M linear died** (loud transformer/cap noise, RF hash) — third supply lost
    this year after two Samlex switchers; suspect mains quality or heat, check the homelab
    UPS's input-voltage log. Interim: Astron SS-50 switcher on the bus at 14.26 V → trim to
    13.8 V before any LiFePO4 sits on it. Wiring done in 10 AWG with PP45 contacts throughout.

### Alternatives considered and why not

- **West Mountain PWRgate / FET combiner** — RF-silent and simple, but a dumb device: no charge
  profiles, no current limiting, no visibility, expensive for what it is. Rejected on
  flexibility.
- **Blue Smart IP67 12/25** — was the near-pick: potted, fanless, one sealed box, charge
  current configurable. Lost to the MPPT when the MPPT + 24 V brick priced at-or-under it
  *with* VE.Direct telemetry and a future solar path. Still the right answer for someone who
  wants one silent box and doesn't care about charger visibility.
- **Orion XS DC-DC (keeps the 13.8 V PSU) and Phoenix Smart IP43 (AC-in, has VE.Direct)** —
  both technically fine (the IP43 is the only small-ish *AC* charger with a VE.Direct port),
  both judged overkill for the shack; David runs both on the boat.
- **Inverter/charger (MultiPlus-style)** — answers a different question (keeping AC alive for
  computers). RF-suspect, conversion losses, and the radio wanted DC all along. A small
  ordinary UPS for the PC is out of this app's scope.

### Battery care notes (LiFePO4)

Shallower DOD nominally buys more cycles, but at this duty the pack dies of calendar aging
first — and LiFePO4 calendar-ages fastest **parked at 100% SOC** (the opposite of lead-acid
instincts). The charger-off-while-operating routine is therefore accidentally near-optimal:
the battery works through the healthy mid-SOC range and only returns to full when charging
resumes. Refinement available: hold 85–90% day-to-day and top to 100% only when full reserve
is wanted — which the smart-plug + SOC-window idea in BACKLOG.md would automate.

## What this means for the app

- **Roles, not arithmetic.** Each VE.Direct device gets a configured role; a battery-monitor
  shunt (`MON 0`) is presumptively Battery, a DC-meter shunt Load/Supply. Derived indicators
  come from role semantics.
- **Battery discharge is not an alarm** — in this shack it's the normal operating mode.
  Alerting (when built) keys on **SOC thresholds**, never on "discharging", and then behaves
  identically whether the charger is off by choice or mains actually failed.
- **Mains state is explicit, not inferred.** With the SmartSolar on VE.Direct, the charger's
  own state field says float/bulk/off — no battery-current guesswork needed.
- **Charger control stays out of the VE.Direct/BLE path.** If the app ever controls charging,
  it reads the shunt it trusts and flips a local-API smart plug — never a reverse-engineered
  Bluetooth write protocol.
