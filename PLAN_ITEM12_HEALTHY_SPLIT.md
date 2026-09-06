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
| **Retentive data loss.** A structural DB change forces a download with re-initialization | **Small, and irrelevant to testing.** Of 132 members in `Valves_DB`, exactly one is `Retain`: `ConfiguredRet`. All 89 of its start values are already FALSE in the offline project. In PLCSIM this is a non-issue — `Configured` resets on every download anyway and is set from a watch table. On the panel it costs three taps of Configure-All |
| **Full download = PLC STOP** | Does not apply to validation: the whole test plan runs against PLCSIM, so nothing real stops. Applies only at commissioning, and the panel is on a bench with nothing wired |
| **Alarm regeneration.** Changing `_Unhealthy`'s text means regenerating and re-downloading the panel | Precedent: the `_Conflict` → `_DoubleInd` rename required deleting the old alarms first. Expect the same |
| **One-scan lag** on the command guard — `PositionFault` is computed in §3 and read at L158 next scan | **Not a regression.** The `Healthy` trip had exactly the same lag. Re-confirm in test anyway |
| **Stale sources** | Handled. Live export taken 2026-09-06 and verified token-identical to the repo copy before any edit. Note `src/UpdatePlcBlocks.cs` still has the pre-machine-move hardcoded path `C:\Users\Admin\...` and needs fixing before use |

---

## 7. Test plan

**This needs no hardware.** PLCSIM covers the logic and HMI Runtime simulation covers everything
the operator sees — including the alarm list, which is where this item's whole claim lands. The
panel is needed to *commission* this, not to *validate* it.

- PLCSIM drives the real `%I` addresses via its **SIM table** (see §6a of `SESSION_HANDOFF.md` —
  TIA watch-table Modify and Force both fail here, don't re-derive the dead ends).
- HMI Runtime simulation raises genuine alarms on this laptop; confirmed working 2026-08-21, alarm
  history populated and the logging database grew on disk.
- **Setup gotcha:** `Valves_DB.Valve[i].Configured` resets FALSE on every PLCSIM download and
  `FC_IoMapper` skips unconfigured valves. Set it TRUE first or everything looks broken. This
  already cost a session once.

Test valve **CM79 = slot 21**: `I12.0` OpenFB, `I12.1` ClosedFB, `I12.2` Healthy, `I12.3` Local,
`Q7.0/Q7.1` commands. Its alarm bit is **word 1, bit 4**.

| # | Test | Expected |
|---|---|---|
| 1 | SIM table: `I12.0` **and** `I12.1` TRUE | **Alarm list shows exactly ONE row** — "CM79 Double indication - both limit switches made." No actuator-FAULT row beside it. `PositionFault[21]`=TRUE, `Healthy` **still TRUE** (tracking `I12.2`), `FaultCode`=2, `StateCode`=1, `W_Conflict[1]`bit4=TRUE, **`W_Unhealthy[1]`bit4=FALSE** |
| 2 | Then also set `I12.2` FALSE | `_Unhealthy` raises **as well**, reading "CM79 actuator FAULT (health signal lost)" — two distinct, correct alarms. `FaultCode` stays 2 (double indication still wins) |
| 3 | `I12.2` TRUE, clear both limits | `PositionFault` self-clears, `_DoubleInd` clears, buttons re-enable |
| 4 | Repeat test 1 on a simulated valve (channel 0) | No longer latches unhealthy forever — the §2.2 trap is gone |
| 5 | Press OPEN in the popup while `PositionFault` is true | Buttons greyed; command refused **and discarded**, not queued. The 2026-08-15 latched-command regression must not come back |
| 6 | Normal open/close stroke, real-channel valve | Unaffected |

Test 1 is the entire item in one observation. Before this change it produced **two** alarm rows,
the second one blaming the actuator for a limit-switch fault.

**Ignore in simulation:** the "Storage medium not available" system alarm is correct on a laptop
with no USB-X61 and is unrelated to this work.

### What genuinely waits for the panel

Only commissioning: the download to real hardware, and re-enabling `Configured` there. Neither
validates the change — they deploy it.

---

## 8. Run order

Steps 1-8 are reversible on the laptop. Only step 9 touches the panel.

1. Re-export `FB_ValveLoop`, `FC_IoMapper`, `Valves_DB`; verify against this plan — **done 2026-09-06**
2. Commit this plan **before** any change — **done, `00b5f75`**
3. `Valves_DB` — add the 2 members — **done**
4. `FB_ValveLoop` (6 edits), `FC_IoMapper` (1 edit) — **done**
5. **Compile the PLC** — **done, 0 errors 0 warnings**
6. `src/GenerateHmiLayout.cs` (3 edits), regenerate tags + popup + alarms — **done**
7. **Compile the HMI** — **done, 0 errors** (1 pre-existing warning, item 41's language mismatch)
8. Verify in-project with `scratch_probe/VerifyItem12.exe` — **done, all checks pass**
9. **Start PLCSIM and download** — **done 2026-09-06**
10. Set `Valves_DB.Valve[21].Configured` TRUE — **done**
11. **Start HMI Runtime simulation** — **done**
12. Run the tests in §7 — **done: 1, 2, 3, 5, 6 PASS; 4 not applicable (no channel-0 valve exists)**
13. Commit, mark item 12 done in `SESSION_HANDOFF.md` with the evidence — **done**

**Item 12 is closed.** Everything above ran on the laptop; no hardware was needed at any point.
What remains is deployment at commissioning — download with reinit and re-enable `Configured` —
which deploys the change rather than validating it.

Observed during testing, both correct and neither a defect:
- Un-forcing a limit switch raises **Unexpected Movement**. A switch dropping with no command
  running is exactly what alarm F exists to catch; a bench test cannot avoid looking like one.
  It is latched by design and needs Reset Fault.
- Stepping the limits by hand during a stroke raises **Direction/Limit Fault** (~10 s seat-break
  grace) and then **Fail to Open** (travel timeout). Both are correct responses to a slow human.

**Scheduling:** the *decision* this touches — what a fault should actually do to a valve — belongs
with item 9's fail-safe review. The change itself is already built and can be validated now.
