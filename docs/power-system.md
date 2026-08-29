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

## Recommended backup topology: DC-coupled, combiner-free

```
mains AC ──[smart plug*]──> Blue Smart IP67 12/25 charger ──> LiFePO4 battery ──> station loads
                                                                   │
                                                            SmartShunt (battery-monitor mode)
```
\* smart plug is the future charger-control hook — see BACKLOG.

Loads live on the battery bus permanently, so a mains failure is a non-event: nothing switches,
nothing drops. The charger carries the steady load and recharge; the battery buffers TX peaks.

- **Charger:** Victron Blue Smart IP67 12/25 — potted, fanless, silent; charge current limited
  in VictronConnect to the battery's rating (the current 40 Ah Bioenno wants ≤20 A). Bluetooth
  only, and that's fine: **the shunt is the app's eyes**, and charger state is inferable from
  battery current. Buying the 25 A frame current-limited means the charger survives the planned
  bigger bank.
- **Battery:** Bioenno 40 Ah LiFePO4 now (~6+ h at the bench's ~6 A draw); larger bank planned
  for extended no-charge runtime.
- **RF-silent operating mode:** charger OFF while on the air — pure battery is the quietest
  possible source, zero switching hash by construction. Charger on between sessions. This is
  the intended routine, not a workaround.

### Alternatives considered and why not

- **West Mountain PWRgate / FET combiner** — RF-silent and simple, but a dumb device: no charge
  profiles, no current limiting, no visibility, expensive for what it is. Rejected on
  flexibility.
- **MPPT charge controller fed by a PSU** ("panels don't care where the power comes from") —
  valid, but a Victron MPPT needs PV input ≥ battery + 5 V to start, so a 13.8 V shack supply
  cannot drive it; it demands a dedicated ~24 V feed. Worth remembering if the shack ever gets
  real panels; overkill today.
- **Orion XS DC-DC (keeps the 13.8 V PSU) and Phoenix Smart IP43 (AC-in, has VE.Direct)** —
  both technically ideal (the IP43 is the *only* small-ish AC charger with a VE.Direct port),
  both judged overkill for the shack; David runs both on the boat. Charger telemetry is a
  luxury when a battery shunt is present.
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
- **Charger control stays out of the VE.Direct/BLE path.** If the app ever controls charging,
  it reads the shunt it trusts and flips a local-API smart plug — never a reverse-engineered
  Bluetooth write protocol.
