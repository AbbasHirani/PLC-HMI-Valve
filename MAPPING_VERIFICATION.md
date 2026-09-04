# Valve I/O mapping verification — before submission

Run 2026-08-31 against `D:\Downloads\Annex_B_IO_List_Full_Mapped (1).xlsx` (26,573 bytes, modified
04/09/2026 16:00), checked programmatically rather than by eye because the submission is
irreversible.

Every check below was done by comparing files, not by reading them. The generated
**`MAPPING_VERIFIED.csv`** carries all 89 rows with CM number, valve tag, system, location,
function, station, the client's own RIO Location, all six channel numbers and all six PLC
addresses.

---

## VERDICT — the mapping is VERIFIED against the live project and the client's file

The PLC was compiled clean (0 errors, 0 warnings) on 2026-08-31, which allowed the live
`Valve_Meta_DB` to be exported and compared directly. **Everything reconciles with zero
discrepancies.**

| # | Check | Result |
|---|---|---|
| 1 | Valve set = the client's 89 in-scope valves | **PASS** — no missing, no extra, no duplicates |
| 2 | Channel integrity (534 assignments) | **PASS** — 0 zeros, 0 duplicates, 0 out-of-range |
| 3 | Channel blocks contiguous per valve | **PASS** — 4 DI consecutive, 2 DQ consecutive, all 89 |
| 4 | No channel crosses a station boundary | **PASS** — 0 violations |
| 5 | Station assignment vs client `RIO Location` | **PASS** — 0 mismatches of 89 |
| 6 | PLC address arithmetic | **PASS** — spans `%I2.0-%I47.3`, `%Q2.0-%Q26.5` |
| 7 | **Live** `Valve_Meta_DB` CM numbers | **PASS** — 0 differences vs the asserted mapping |
| 8 | **Live** Valve Tag vs client Excel | **PASS** — 0 mismatches of 89 |
| 9 | **Live** System vs client Excel | **PASS** — 0 mismatches of 89 |
| 10 | Live zone blocks contiguous | **PASS** — Aft 1-27, Bilge 28-54, Fwd 55-89 |
| 11 | Client file internal consistency | **2 defects in THEIR file** — see §6 |

Live export vs the asserted mapping:

```
LIVE vs HOME_DIAGRAM   : 0 differences
LIVE vs repo meta file : 54 differences   <- the repo copy was the wrong one
```

**The repo's stale `temp_valve_meta_db.xml` has been replaced with the live export** (structurally
identical: 29 members, same names, same types). Repo, live project and alarm naming now all agree.
The landmine described in §5 is defused.

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

## 5. RESOLVED — the slot → CM divergence (kept, because the root cause is still open)

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

### How it was resolved, 2026-08-31

1. STEP 7 licence restored, **PLC compiled clean** (0 errors, 0 warnings).
2. `HmiBuilder.exe --export-block=Valve_Meta_DB` succeeded.
3. **The live DB matched the asserted mapping exactly — 0 differences** (slot 1 = CM25,
   slot 21 = CM79, slot 28 = CM01), and differed from the repo copy on 54 slots.
4. **The repo copy was replaced with the live export.** It was the wrong one, not the project.
   The stale version is preserved outside the repo for reference.

So the panel has been correct all along; only the repo's import source was wrong.

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

## 7. The blocker that briefly stopped this, now cleared

The first attempt to export the live `Valve_Meta_DB` failed with:

```
Inconsistent blocks and PLC data types (UDT) cannot be exported.
```

The PLC was in an inconsistent state and needed compiling, which needed the STEP 7 licence
(item 46). Both were resolved on 2026-08-31 — licence restored, PLC compiled 0 errors 0 warnings —
and the export then succeeded.

Worth keeping in mind for next time: **a project that will not export is telling you it has not
been compiled**, and on this project that has twice traced back to licensing rather than to the
code.

---

## Recommendation

**The mapping is verified and safe to submit.** `MAPPING_VERIFIED.csv` is the artefact: 89 rows,
every one reconciled against the live PLC data block and the client's own schedule.

**Two things to put to the client alongside it**, neither of which changes our mapping:

1. Their `IO Summary` sheet contradicts their `Valve list` sheet (101 vs 102 valves, 34 vs 35
   Ballast Fwd). Ask which is authoritative before anyone signs.
2. Eleven in-scope valves are marked `Not Found` in their own file. Channels are reserved for all
   eleven, so if any prove not to exist those channels simply go unused — no re-addressing. Flag
   it so the reservation is a recorded decision rather than an assumption.

**One engineering task remains open** and is worth doing before the next change to either list:
`Valve_Meta_DB` and `HOME_DIAGRAM` are still two hand-maintained copies of the same fact with
nothing comparing them. A 54-slot divergence survived undetected between them. Either generate one
from the other, or add a build-time assertion that they agree.
