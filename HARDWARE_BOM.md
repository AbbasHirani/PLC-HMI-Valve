# Hardware BOM — MV WESTERLY valve remote control system

Supplied by the user 2026-08-30 and recorded here because item 40 could not be closed without it:
an external audit had reported order-number and firmware mismatches against "what you are buying",
and **no parts list existed anywhere in this repository** to check that against. It is checkable now.

---

## 1. The BOM as supplied

| Description | Qty |
|---|---|
| 1 x S7-1200 CPU 1214C | 1 |
| 1 x Unified Comfort HMI (MTP1500 main / MTP1200 VE) | 1 |
| 2 x ET200SP interface modules | 3 |
| ET200SP DI 16x24VDC modules | 23 |
| ET200SP DQ 16x24VDC modules | 13 |
| Phoenix interposing relays | 176 |

**No order numbers are given.** That is a gap in its own right — a 1214C exists in DC/DC/DC,
AC/DC/RLY and DC/DC/RLY variants, and the BOM cannot be ordered from or variant-checked as written.

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

### The CPU is NOT SIPLUS — the audit was wrong about this

The project is configured as **`6ES7 214-1AG40-0XB0` @ V4.0**, a standard S7-1200 CPU 1214C.
The external audit claimed a SIPLUS `6AG1214-1AG40-4XB0`. The BOM's own text says "S7-1200 CPU
1214C", which agrees with the project and not with the audit. Since the BOM carries no order
numbers, it cannot confirm the exact variant — but nothing anywhere in this project or this BOM
says SIPLUS.

Firmware readings, all from `inspect_hw.log` and solid:
CPU **V4.0**, the three IMs **V6.2**, HMI **20.0.0.0**.

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

### 3.6 HMI: MTP1500 or MTP1200? The screens only work on one

The row reads "Unified Comfort HMI (MTP1500 main / MTP1200 VE)" at Qty 1, which is ambiguous.

The project is configured as **`6AV2 128-3QB06-0AXx`**. The `3Q` in that number is the **MTP1500**
— 15.6 inch, **1920 × 1080**. An MTP1200 is 12.1 inch, **1280 × 800**.

**Every screen in this project is authored at 1920 × 1080.** Artwork was exported to match those
exact pixel boxes so overlay coordinates map 1:1 with no runtime rescaling (see item 32). Putting
this project on an MTP1200 is not a settings change — it is a re-measure and re-export of all four
drawings plus every overlay coordinate.

Confirm which panel is actually being bought before anything else on this list.

---

## 4. Summary of what to add before ordering

| | Item | Qty |
|---|---|---|
| 1 | ~~Interposing relays — correct 176 to **178**~~ **confirmed, +2 being ordered** | done |
| 1b | Shelf spare relays beyond the 178 — open question | +4–6 |
| 2 | BaseUnits for every I/O module | 36 + spares |
| 3 | BusAdapter BA 2xRJ45 `6ES7 193-6AR00-0AA0` | 3 |
| 4 | Server module `6ES7 193-6PA00-0AA0` | 3 |
| 5 | Spare DI16 `6ES7 131-6BH01-0BA0` | 3 (one per station) |
| 6 | Spare DQ16 `6ES7 132-6BH01-0BA0` | 2 (AFT, MID) |
| 7 | Power supply — **hold** pending item 42 | — |
| 8 | Add order numbers to every line | — |
| 9 | Confirm MTP1500 vs MTP1200 | — |

**After the spares are added to the TIA project**, re-run `InspectAddresses` and confirm every
existing `%I`/`%Q` address is **unchanged**, with the new modules' ranges entirely above them. If
any existing address moved, the module went in at the wrong position — reposition before touching
`FC_PhysicalIoCopy`.
