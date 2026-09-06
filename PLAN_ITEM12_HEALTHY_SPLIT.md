# Item 12 — Split `Healthy` into a raw signal and a computed `PositionFault`

**Status:** planned, not yet built. Written 2026-09-06.
**Verified against:** live TIA export of `FB_ValveLoop`, `FC_IoMapper`, `Valves_DB` taken 2026-09-06.

---

## 1. The principle

The four feedbacks (`OpenFB`, `ClosedFB`, `LocalMode`, `Healthy`) are **valve-owned signals**. The
valve reports its true physical state on them, including while it is being worked by hand in Local
Mode. The PLC must only ever read them, never write them.

`Healthy` breaks that rule in two places, both deliberately, both to force a fault when both limit
switches are made at once (physically impossible — a wiring or sensor fault):

| Where | Statement |
|---|---|
| `FB_ValveLoop` §3, L309-311 | `IF OpenFB AND ClosedFB THEN Healthy := FALSE` |
| `FC_IoMapper` L51-52 | The same trip, re-applied |

`FC_IoMapper` needs its copy because OB1 runs `FB_ValveLoop` **then** `FC_PhysicalIoCopy` **then**
`FC_IoMapper`; the mapper refreshes `Healthy` from the real DI *after* FB_ValveLoop's check and would
otherwise silently undo it.

---

## 2. What this actually costs today

The handoff described this as lost diagnostic information. Reading the code, the concrete cost is
sharper than that, and worse.

### 2.1 Every double-indication event raises two alarms, one of them false

`W_Conflict` **already exists** — packed from raw `OpenFB AND ClosedFB` at `FB_ValveLoop` L568 — and
already drives a per-valve alarm `Vxxx_DoubleInd`: *"CMxx Double indication - both limit switches
made."*

But because the trip also forces `Healthy := FALSE`, the same physical event **also** sets the
`W_Unhealthy` bit and raises `Vxxx_Unhealthy`: *"CMxx reported Unhealthy status."*

That second alarm is factually wrong. The actuator never reported anything — the PLC concluded it.
It sends a technician to check the actuator when the fault is a limit switch or its wiring.

Across 89 valves this is a duplicated, misleading alarm on a fault type that tends to arrive in
groups (a shared cable, a wet junction box). It feeds directly into item 39 (alarm flood).

### 2.2 A simulated valve latches unhealthy forever

For a valve with `HealthyChannel = 0`, **nothing ever restores `Healthy`**. The trip latches it FALSE
permanently:

- `ResetFault` deliberately does not write `Healthy` (correct — see `GenerateHmiLayout.cs` L1513).
- `FC_IoMapper` only refreshes `Healthy` when `HealthyChannel > 0`.

The only escape is toggling Configured OFF then ON. Real valves self-heal because the mapper re-reads
the DI every scan. The asymmetry is undocumented and was found by reading the code, not by testing.

### 2.3 What is NOT broken (and does not need touching)

Worth recording, because it shrinks the job considerably:

- **`StateCode` (L428) already ORs `(OpenFB AND ClosedFB)` in independently.** It does not rely on the
  `Healthy` overwrite.
- **`FaultCode` (L394) already tests double-indication *first***, so the popup already prints
  `DOUBLE INDICATION — BOTH LIMITS MADE` and not `UNHEALTHY — ACTUATOR FAULT`.
- Three of the nine HMI `Healthy` references are **dead code** — `BuildOverviewScreen`,
  `AddMasterResetScript` (`GenerateHmiLayout.cs`) and `CountScript` (`MarineScreens.cs`) have no
  callers.

The display half of the problem is already solved. **Only the alarm half is broken.**

---

## 3. Design decisions

| Decision | Choice | Reasoning |
|---|---|---|
| Where does `PositionFault` live? | Flat array `Array[1..89] of Bool` in `Valves_DB` | The `Valve_IO` UDT holds *valve-owned* signals; the flat arrays (`DirFault`, `UnexpMove`, `TimeoutOpenAlarm`) hold *PLC-computed* flags. Putting it in the UDT would repeat the exact category error this item exists to fix |
| Latched or live? | **Live** — plain assignment every scan | It mirrors a physically present condition and must clear itself when the limits stop conflicting. Consistent with the existing rule that a condition still physically true cannot be acknowledged away |
| Does `W_Unhealthy` include it? | **No** | That is the entire point. `_DoubleInd` already covers it. One event, one alarm |
| Per-valve HMI tags? | **No — one tag, not 89** | Nothing per-valve on the HMI needs it (the mimic reads `DispCode`, the tables read `StateCode`). Only the popup does, via the `Sel` mirror |
| `_Unhealthy` alarm wording | `"CMxx actuator FAULT (health signal lost)."` | Confirmed by the user 2026-09-06. Without it the split is invisible to the operator, who is the person it is for |

---

## 4. The change

### 4.1 `Valves_DB` — 2 new members

```
PositionFault    : Array[1..89] of Bool
SelPositionFault : Bool
```

### 4.2 `FB_ValveLoop` — 6 edits

| # | Line | Change |
|---|---|---|
| 1 | L309-311 | `Healthy := FALSE` becomes `PositionFault[#i] := OpenFB AND ClosedFB` (assignment, not latch) |
| 2 | L158 | Command guard becomes `NOT LocalMode AND Healthy AND NOT PositionFault[#i]` |
| 3 | L211 | Run-latch drop becomes `LocalMode OR NOT Healthy OR PositionFault[#i]` |
| 4 | L394 | `FaultCode` chain uses `PositionFault[#i]` instead of recomputing. Cosmetic — single source of truth |
| 5 | L428 | `StateCode` uses `PositionFault[#i]` instead of recomputing. **Zero behaviour change** |
| 6 | L890 / L901 | Add `SelPositionFault := PositionFault[#sel]` / `:= FALSE` |

`W_Unhealthy` (L555) needs **no code change** — it stays `NOT Healthy`. Its *meaning* changes: it now
genuinely means the actuator's own health contact.

### 4.3 `FC_IoMapper` — 1 edit

- L51-52: the re-applied trip becomes `PositionFault[#i] := OpenFB AND ClosedFB`.
  It must stay, for the same ordering reason as today. Because it is an assignment rather than a
  latch, the later write simply wins with the fresher data.
- L41-42, the raw `Healthy := IO_Buffer_DB.DI[HealthyChannel[#i]]` read, is **untouched**. It becomes
  the only writer of `Healthy` anywhere in the program. That is the goal of this item.

### 4.4 HMI — `src/GenerateHmiLayout.cs`, 3 edits

| # | Line | Change |
|---|---|---|
| 7 | ~L2289 | Add `CreateSummaryTag(hmi, "Valves_DB_SelPositionFault", ...)` — **+1 tag total** |
| 8 | L1819 | `AddRemoteLockStyling`: `locked = local \|\| !cfg \|\| !healthy \|\| posfault` |
| 9 | L2188 | `_Unhealthy` message text to `"CMxx actuator FAULT (health signal lost)."` |

Nothing else. The fault-text array (L1027), the popup, the mimic and the tables all stay as they are.

### 4.5 Deliberately NOT changed

- The three dead-code sites — a comment line each, no edits.
- `ToggleService` / `ConfigToggleScript` / `ZoneConfigureAllScript` writing `_Healthy := true` on
  enable. Still the same principle violation, but it belongs to item 9's scope.
  **Note:** the §2.2 sim-valve trap disappears by itself once nothing writes `Healthy := FALSE`.

---

## 5. Why this is low risk

**The refactor is behaviour-neutral by construction.** Every edit except #1 and #9 substitutes an
equivalent expression: `StateCode`, `FaultCode`, the popup, the mimic and the tables all produce
identical output before and after.

The only behavioural changes in the whole thing are:
1. the duplicate `_Unhealthy` alarm on a double-indication event goes away, and
2. a sim valve no longer latches unhealthy forever.

If something breaks in test, it is a typo, not a design error — and there is a short, enumerable list
of places to look.

---

## 6. Risks

| Risk | Assessment |
|---|---|
| **Retentive data loss.** A structural DB change forces a download with re-initialization | **Small.** Of 132 members in `Valves_DB`, exactly one is `Retain`: `ConfiguredRet`. All 89 of its start values are already FALSE in the offline project. Cost = re-enabling whatever valves were on for bench testing (three taps of Configure-All) |
| **Full download = PLC STOP** | Free right now. The panel is on a bench, valves are not wired, nothing is aboard. This is *why the work should happen now* — every one of these risks gets more expensive after install |
| **Alarm regeneration.** Changing `_Unhealthy`'s text means regenerating and re-downloading the panel | Precedent: the `_Conflict` → `_DoubleInd` rename required deleting the old alarms first. Expect the same |
| **One-scan lag** on the command guard — `PositionFault` is computed in §3 and read at L158 next scan | **Not a regression.** The `Healthy` trip had exactly the same lag. Re-confirm in test anyway |
| **Stale sources** | Handled. Live export taken 2026-09-06 and verified token-identical to the repo copy before any edit. Note `src/UpdatePlcBlocks.cs` still has the pre-machine-move hardcoded path `C:\Users\Admin\...` and needs fixing before use |

---

## 7. Test plan

Watch table, one real-channel valve (CM79) and one simulated valve.

| # | Test | Expected |
|---|---|---|
| 1 | Force both limits on the real valve | **Exactly one alarm** (`_DoubleInd`). `PositionFault` TRUE. `Healthy` **unchanged**, still tracking the real DI. `FaultCode` = 2, `StateCode` = 1. Popup reads DOUBLE INDICATION. OPEN/CLOSE greyed |
| 2 | Then also force the real Healthy DI false | `_Unhealthy` raises **as well** — two distinct, correct alarms. `FaultCode` stays 2 (double-indication still wins) |
| 3 | Release both limits | `PositionFault` self-clears, `_DoubleInd` clears, buttons re-enable |
| 4 | Repeat test 1 on the simulated valve (channel 0) | No longer latches unhealthy forever — the §2.2 trap is gone |
| 5 | Command a valve while `PositionFault` is true | Refused **and discarded**, not queued. The 2026-08-15 latched-command regression must not come back |
| 6 | Normal open/close stroke, real valve | Unaffected |

---

## 8. Run order

Steps 1-8 are reversible on the laptop. Only step 9 touches the panel.

1. Re-export `FB_ValveLoop`, `FC_IoMapper`, `Valves_DB`; verify against this plan — **done 2026-09-06**
2. Record the current `Configured` pattern
3. Commit this plan **before** any change
4. `Valves_DB` — add the 2 members
5. `FB_ValveLoop` (6 edits), `FC_IoMapper` (1 edit)
6. **Compile the PLC.** Stop here if it does not compile clean
7. `src/GenerateHmiLayout.cs` (3 edits), regenerate HMI tags + alarms
8. **Compile the HMI**
9. Download PLC (with reinit) + HMI to the panel
10. Restore `Configured`, verify against step 2
11. Run the 7 tests above
12. Commit, update item 12 in `SESSION_HANDOFF.md` to done with the test evidence

**Scheduling:** do this with item 9's fail-safe review, which has to answer "what should each fault
actually *do* to a valve" anyway — and `PositionFault` versus `Healthy` is precisely one of those
answers. Not during the hardware wiring window.
