# Valve I/O mapping verification — before submission

Run 2026-08-31 against `D:\Downloads\Annex_B_IO_List_Full_Mapped (1).xlsx` (26,573 bytes, modified
04/09/2026 16:00), checked programmatically rather than by eye because the submission is
irreversible.

Every check below was done by comparing files, not by reading them. The generated
**`MAPPING_VERIFIED.csv`** carries all 89 rows with CM number, valve tag, system, location,
function, station, the client's own RIO Location, all six channel numbers and all six PLC
addresses.

---

## VERDICT

**The mapping itself is correct.** Channels, stations and the client's RIO assignment all
reconcile with zero discrepancies.

**But it cannot be certified as submitted-ready yet**, for one reason: the live `Valve_Meta_DB`
could not be read, and a copy of it in this repo is wrong. Details in §5.

| # | Check | Result |
|---|---|---|
| 1 | Valve set = the client's 89 in-scope valves | **PASS** — no missing, no extra |
| 2 | Channel integrity (534 assignments) | **PASS** — 0 zeros, 0 duplicates, 0 out-of-range |
| 3 | Channel blocks contiguous per valve | **PASS** — 4 DI consecutive, 2 DQ consecutive, all 89 |
| 4 | No channel crosses a station boundary | **PASS** — 0 violations |
| 5 | Station assignment vs client `RIO Location` | **PASS** — 0 mismatches of 89 |
| 6 | PLC address arithmetic | **PASS** — spans `%I2.0-%I47.3`, `%Q2.0-%Q26.5` |
| 7 | Slot → CM mapping | **FAIL in this repo** — see §5 |
| 8 | Client file internal consistency | **2 defects** — see §6 |

---

## 1. Valve set

The client sheet holds **102 data rows**: 27 Bilge, 27 Ballast Aft, 35 Ballast Fwd, 13 Fuel Oil.
The 13 Fuel Oil rows are the ones the client instructed be ignored, and they are exactly the 13
rows with a blank `Status` — a clean, independent confirmation that the right rows were excluded.

**102 − 13 = 89**, matching the project exactly. Set comparison against the project's CM numbers:
**no missing, no extra.**

## 2-3. Channel integrity

```
DI assignments 356   DQ assignments 178   total 534
DI: zeros=0  out-of-range(1..368)=0  duplicates=0  distinct=356
DQ: zeros=0  out-of-range(1..208)=0  duplicates=0  distinct=178
slots with non-consecutive DI block: none
slots with non-consecutive DQ pair : none
```

Every valve owns 4 consecutive DI and 2 consecutive DQ. No channel is shared by two valves.

## 4. Station boundaries

```
AFT  valves=27  DI 1-108   of 1-112     free 4    DQ 1-54    free 10
MID  valves=27  DI 113-220 of 113-224   free 4    DQ 65-118  free 10
FWD  valves=35  DI 225-364 of 225-368   free 4    DQ 129-198 free 10
cross-station violations: 0
```

No valve's channels straddle two stations. This is what `FC_IoMapper`'s index-range guard depends
on. Free channels match item 40's independent audit exactly.

## 5. THE ONE REAL PROBLEM — slot → CM

**There are two independent slot → CM mappings in this system, and nothing checks them against
each other:**

| Source | Used by | Slot 1 | Slot 21 | Slot 28 |
|---|---|---|---|---|
| `HOME_DIAGRAM` (`MarineScreens.cs`) | `CmForSlot()` → **all 718 alarm texts** | CM25 | CM79 | CM01 |
| `Valve_Meta_DB.CmNo[]` (repo copy) | **every screen table, the popup, Config** | CM01 | CM21 | CM25 |

**They disagree on 54 of 89 slots** — the whole AFT and BILGE range. Slots 55-89 agree because
FWD is identical in both orderings.

**`HOME_DIAGRAM` is the correct one.** Three independent confirmations:

- Checked each slot's CM against the client's `System` column and the PLC zone split
  (AFT 1-27, BILGE 28-54, FWD 55-89): **`HOME_DIAGRAM` 0 mismatches, repo `Valve_Meta_DB` 54.**
- Checked each slot's station against the client's `RIO Location` column: **0 mismatches of 89.**
- It matches live behaviour: the 2026-08-29 CM79 test drove `%I12.0-12.3` (= DI 81-84 = slot 21)
  and the panel raised the alarm for **CM79**, which is what `HOME_DIAGRAM` says slot 21 is. The
  BILGE/ER screen (slots 28-54) shows Bilge valves, which only holds if slot 28 is CM01.

**So the repo's `temp_valve_meta_db.xml` has the AFT and BILGE blocks swapped.** The live project
is almost certainly correct; this file is stale (unchanged since commit `6c9bf71`).

### Why this is dangerous rather than cosmetic

`ImportPlcBlocks` runs **unconditionally at the start of every builder run**. It is only failing
today because the STEP 7 licence is missing (item 46). **The moment that licence is fixed, the next
builder run will import this file and silently swap 54 valves' identities.**

Channels live in `Valve_Channels_DB` and would not move. So the screen would label slot 1 as
"CM01" while its channels still drive the valve the client calls **CM25**. An operator pressing
OPEN on a valve labelled CM01 would open CM25. On a ballast system that is a serious
mis-operation, and nothing on screen would reveal it — except that the alarm list, which uses
`HOME_DIAGRAM`, would name a different valve than the table for the same slot.

### What must happen before any builder run

1. Restore the STEP 7 licence (item 46) and **compile the PLC** — it is currently inconsistent
   (§7), which is why the live DB could not be exported.
2. `HmiBuilder.exe --export-block=Valve_Meta_DB` and compare `CmNo[1..89]` against
   `MAPPING_VERIFIED.csv`.
3. If the live DB matches: **replace `temp_valve_meta_db.xml` with the export.** The repo copy is
   the wrong one, not the project.
4. If it does not match: stop. The live project has the same swap and 54 valves are mislabelled on
   the panel today.

**Longer term:** `Valve_Meta_DB` and `HOME_DIAGRAM` should not be two hand-maintained lists.
Either generate the DB from `HOME_DIAGRAM`, or add a build-time assertion that they agree. Nothing
currently detects a divergence, which is how a 54-slot difference survived.

## 6. Defects in the client's own file — raise before submitting

**The `IO Summary` sheet contradicts the `Valve list` sheet.**

| `IO Summary` says | `Valve list` actually has |
|---|---|
| Total mapped valves **101** | **102** rows |
| Ballast Forward valves **34** | **35** |

Our 89 is derived from the Valve list, which is the detailed sheet and is self-consistent
(27 + 27 + 35 + 13 = 102). But the client should reconcile their own summary before signing
anything, or the two numbers will be quoted against each other later.

The summary's `Total DO required 202` / `Total DI required 404` are computed from 101 valves
including Fuel Oil, so they do not describe this scope either. Our scope is **178 DO and 356 DI**.

**Eleven in-scope valves are marked `Not Found`** by the client. They are included in the mapping
because they are in the schedule, but the client flagged them:

```
CM31 11-034-A2 Ballast Fwd   CM77 11-014-A1 Ballast Fwd
CM36 11-047-A2 Ballast Fwd   CM82 11-012-A3 Ballast Aft
CM43 11-054-A2 Ballast Fwd   CM94 20-023-A1 Bilge (Fire main)
CM49 11-043-A1 Ballast Fwd   CM95 20-025-A1 Bilge (Fire line)
CM61 11-043-A2 Ballast Fwd   CM96 20-022-A1 Bilge (Bilge Fire Pump 2)
                             CM97 20-024-A1 Bilge (Bilge Fire Pump 1)
```

Six overlap with questions already sent (Q1/Q2 of the client set). **Channels are reserved for all
eleven**, so if any turn out not to exist, those channels are simply unused — no re-addressing
needed. That is the safe direction. The unsafe direction is the reverse: a valve that exists but is
not in the schedule, which is Q2.

## 7. What could not be checked, and why

**The live `Valve_Meta_DB` could not be read.** Export failed with:

```
Inconsistent blocks and PLC data types (UDT) cannot be exported.
```

The PLC program is in an inconsistent state and needs compiling, which needs the STEP 7 licence
(item 46). Note the failed imports from that licence gap may have contributed to the inconsistency;
either way a successful PLC compile should clear it.

**Everything in §1-§6 is independent of that** — it was checked against the repo's channel DB, the
builder source and the client's Excel, none of which need TIA.

---

## Recommendation

**Do not submit until §5 step 2 is done.** The mapping is right; what is unproven is that the
*project* holds the right one, and the check takes minutes once the licence is in.

`MAPPING_VERIFIED.csv` is the mapping being asserted, and is suitable as the submission artefact
once that confirmation lands.
