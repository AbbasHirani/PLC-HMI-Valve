# Hardware BOM — MV WESTERLY valve remote control system

Supplied by the user 2026-08-30 and recorded here because item 40 could not be closed without it:
an external audit had reported order-number and firmware mismatches against "what you are buying",
and **no parts list existed anywhere in this repository** to check that against. It is checkable now.

---

## 1. The BOM as supplied

The first version supplied carried descriptions only, with no order numbers. The real part list
followed the same day and is what should be read:

| Order number | Part | Qty |
|---|---|---|
| `6AG1214-1AG40-4XB0` | **SIPLUS** S7-1200 CPU 1214C DC/DC/DC | 1 |
| `6AV2128-3QB06-0AX1` | HMI MTP1500 Unified Comfort Panel | 1 |
| `6ES7155-6AU02-0BN0` | ET 200SP PROFINET interface module IM 155-6 PN | 3 |
| `6ES7131-6BH01-0BA0` | ET 200SP digital input module (DI 16 x 24 VDC) | 23 |
| `6ES7132-6BH01-0BA0` | ET 200SP digital output module (DQ 16 x 24 VDC) | 13 |
| `6EP1334-2BA20` | Power supply unit | 1 |
| — | Phoenix interposing relays | 176 → **178** |

---

## 2. Cross-check against the configured project

Read from `inspect_hw.log` (hardware) and `temp_valve_channels_db.xml` (channel assignments),
2026-08-30.

### Matches — these are right

| Item | BOM | Configured | |
|---|---|---|---|
| DI 16x24VDC modules | 23 | 23 × `6ES7 131-6BH01-0BA0` | OK |
| DQ 16x24VDC modules | 13 | 13 × `6ES7 132-6BH01-0BA0` | OK |
| ET200SP interface modules | 3 | 3 × `6ES7 155-6AU02-0BN0` @ V6.2 | OK |

The DI/DQ split matches the per-station layout exactly: AFT 7 DI + 4 DQ, MID 7 DI + 4 DQ,
FWD 9 DI + 5 DQ = 23 and 13.

*(The interface-module row reads "2 x …" in its description but carries Qty 3. The quantity is
correct — there are three stations. The description is a typo and should be fixed before ordering,
because a reader could act on either number.)*

### The CPU is SIPLUS, and that is fine — correcting an earlier claim here

An earlier version of this file said "the CPU is NOT SIPLUS — the audit was wrong about this".
**That was wrong**, and it was written from the first BOM, which carried no order numbers at all.
The real list confirms the external audit: the CPU being bought is
**`6AG1214-1AG40-4XB0`, a SIPLUS S7-1200 CPU 1214C DC/DC/DC**.

**It is not a problem, and it changes nothing in this project.** Compare the two numbers:

```
6ES7 214-1AG40-0XB0    project, standard
6AG1 214-1AG40-4XB0    buying,  SIPLUS
```

The core — `214-1AG40` — is identical. That is exactly how SIPLUS numbering works: the same
device with conformal coating for humidity, salt air, vibration and a wider temperature range,
carrying a `6AG1` prefix instead of `6ES7`. Same firmware, same instruction set, same memory, same
everything the program touches. It is the right choice for a vessel.

Siemens' own practice is to configure SIPLUS using the **standard** article number, because the
devices are identical from the engineering side and many SIPLUS parts have no separate TIA catalog
entry. So `6ES7 214-1AG40-0XB0` in the project against a physical `6AG1214-1AG40-4XB0` is expected
rather than a mistake.

**Confirm it at first download rather than assume it** — on the item 41 checklist, not the worry
list. If TIA does object, it is a device swap in the hardware catalog: minutes, and **zero code
change**. Nothing in the program, the DBs, the HMI or the channel map depends on it.

**HMI matches exactly.** Buying `6AV2128-3QB06-0AX1`; `GenerateHmiLayout.cs:44` already names that
very part number in a comment. No question here at all — see 3.6.

Firmware readings, all from `inspect_hw.log` and solid:
CPU **V4.0**, the three IMs **V6.2**, HMI **20.0.0.0**. The physical CPU will likely ship newer than
V4.0. Configuring a *lower* firmware than the hardware is normal and fine — but nobody should bump
it in the project without a reason, since that can lock out downloading to an older CPU.

---

## 3. Discrepancies — act on these before ordering

### 3.1 Two interposing relays short (176 supplied, 178 needed) — RESOLVED 2026-08-30

Every valve drives **two** outputs, open and close, through its own relay. `Valve_Channels_DB`
carries **89 non-zero `OpenCmdChannel` and 89 non-zero `CloseCmdChannel` values** — verified by
counting, not assumed:

```
OpenCmdChannel    values=89  nonzero=89
CloseCmdChannel   values=89  nonzero=89
```

**89 × 2 = 178.** The BOM's 176 is **88 × 2**.

This is the valve-count saga (section 4 of `SESSION_HANDOFF.md`) surfacing in the purchase order.
The client's instruction was "in total there will be 88 valves"; the real figure settled at **89**
once the Fuel Oil rows were excluded, and the relay quantity was never revised to follow. Two
relays is a trivial cost now and one un-commandable valve later.

**Confirmed 2026-08-30: two extra will be ordered, bringing it to 178.**

**Still worth deciding: 178 is exact, with no spare relay at all.** A relay is a consumable and
the most likely item on this list to fail in service — a stuck or welded contact on a valve's open
coil is a valve that will not open on command. Suggest **4–6 shelf spares beyond the 178**. They
are cheap, they need no configuration, and the alternative is mobilising someone to the vessel for
a part the size of a matchbox.

### 3.2 BaseUnits are missing entirely

Every ET200SP I/O module sits on a BaseUnit. **36 modules (23 DI + 13 DQ) need 36 BaseUnits**, and
none appear on the BOM. They are also not modelled in the TIA project, so neither source catches
this on its own.

Light vs dark BaseUnits also decide where each new potential group starts, which is exactly the
load-group reasoning item 39 depends on — so this is a design decision, not just a line item.

### 3.3 Bus adapters and server modules are missing

Both **are configured in the project** and therefore must be bought:

| Part | Order number | Qty configured | On BOM |
|---|---|---|---|
| BusAdapter BA 2xRJ45 | `6ES7 193-6AR00-0AA0` | 3 | **no** |
| Server module | `6ES7 193-6PA00-0AA0` | 3 | **no** |

A server module terminates every ET200SP station. Without one the station is incomplete.

### 3.4 No spare I/O modules — this is item 40's entire point

23 DI and 13 DQ is **exactly** what the project consumes, leaving:

| Station | DI free | DQ free | Spare valves |
|---|---|---|---|
| AFT | 4 | 10 | **1** |
| MID | 4 | 10 | **1** |
| FWD | 4 | 10 | **1** |

**Three spare valve slots on the whole vessel — 3.4%.** A valve needs 4 DI and 2 DQ, so DI is the
binding constraint. A marine retrofit would normally carry 10–20%.

Item 3 records **seven valves drawn on the client's P&ID that appear in no schedule row**. If any
are in scope they need 28 DI and 14 DQ; there are 12 DI free across all three stations. **They do
not fit.**

Recommended addition: **one DI16 per station minimum** (takes each to ~18% spare), plus a DQ16 on
AFT and MID if matching output headroom is wanted — FWD's DQ already has it. Each added module
needs its BaseUnit per 3.2.

**Append spare modules at the END of each station.** `FC_PhysicalIoCopy` holds 368 hardcoded DI and
208 DQ address assignments; a module inserted mid-station shifts every address behind it and
invalidates the 534-entry channel map — discovered after the cables are terminated. Appending after
the last module gives fresh addresses above everything existing and moves nothing.

### 3.5 No power supply on the BOM

The `6EP1334-2BA20` discussed in item 42 does not appear here. Item 42's three open questions —
one PSU feeding three distributed cabinets, single point of failure, and no load calculation
existing anywhere — are unresolved, so **do not order power on this BOM's authority**.

### 3.6 Panel resolution — RESOLVED, and the earlier worry here was wrong

An earlier version of this file warned that the project was authored at 1920 x 1080 and that an
MTP1200 would mean re-exporting every drawing. **The premise was wrong on both counts.**

The panel is confirmed as **MTP1500 Unified Comfort, `6AV2128-3QB06-0AX1`** — 15.6 inch, and its
real resolution is **1366 x 768**, not 1920 x 1080.

**The builder already targets exactly that, and always has.** `GenerateHmiLayout.cs:41-58`:

```csharp
private const int SCREEN_W = 1920;   // virtual design canvas
private const int SCREEN_H = 1080;
// Real target panel (MTP1500, 6AV2128-3QB06-0AX1) is 1366x768, not 1920x1080.
private const int TARGET_W = 1366;
private const int TARGET_H = 768;
private static int SX(int v) { return (int)Math.Round(v * (double)TARGET_W / SCREEN_W); }
private static int SY(int v) { return (int)Math.Round(v * (double)TARGET_H / SCREEN_H); }
```

Screens are authored against a 1920 x 1080 virtual canvas and every coordinate is scaled to real
panel pixels at the moment it is written to a screen item. X and Y scale by slightly different
factors on purpose, because 1366 x 768 is not exactly 16:9. `SFont()` scales type with a floor of
10 px so scaled-down text stays legible.

**Nothing to do.** The code even names the exact order number being bought.

---

## 4. Conclusion — nothing on this BOM affects the software

**Matches the project exactly:** DI modules 23 = 23, DQ modules 13 = 13, interface modules 3 = 3,
and the HMI part number is the same one already named in the builder's source.

**Found and fixed:** relays 176 → **178**. 176 was 88 valves x 2; the system has **89**. Confirmed
2026-08-30 that two extra will be ordered.

**Two worries raised here were wrong, and are corrected above:** the panel resolution (the builder
has always targeted 1366 x 768) and the SIPLUS CPU (normal practice, zero code impact). Both came
from reading the first BOM, which carried no order numbers.

### Out of scope — hardware team, not this project

BaseUnits, bus adapters, server modules, the power supply, spare I/O modules and shelf spare
relays. Raised here originally and withdrawn: there is a full hardware team and these are theirs.
The notes remain in 3.2–3.5 as reference only, not as actions on us.

### The ONE thing that is ours, and must reach the hardware team

> **If any I/O module is added or moved, tell us first — and append it at the END of its station,
> never in the middle.**

`FC_PhysicalIoCopy` holds **368 hardcoded DI and 208 DQ address assignments**, mapping
`DI[n] -> %I(2 + (n-1)/8).((n-1) mod 8)` across exactly `%I2.0-%I47.7` and `%Q2.0-%Q27.7`. A module
inserted mid-station shifts every address behind it and silently invalidates the 534-entry channel
map. Appending after the last module gives fresh addresses above everything existing and moves
nothing.

Nobody finds that by looking at the panel. It surfaces as valves responding to the wrong commands.

**If a module is ever added:** re-run `InspectAddresses` and confirm every existing `%I`/`%Q`
address is **unchanged**, with the new module's range entirely above them. If any existing address
moved, the module went in at the wrong position — reposition before touching `FC_PhysicalIoCopy`.

### For the commissioning checklist (item 41), not the worry list

- Confirm TIA downloads to the SIPLUS CPU configured as the standard article number. If it objects,
  swap the device in the hardware catalog — minutes, no code change.
- The project configures firmware **V4.0**; the CPU will likely ship newer. Configuring lower is
  normal and fine. Do not let anyone bump it without a reason — that can lock out downloading to an
  older CPU.
