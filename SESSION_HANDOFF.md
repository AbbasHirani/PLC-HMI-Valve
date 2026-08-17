# Session Handoff — valveDemo2 (MV WESTERLY Ballast/Bilge Valve Control)

**Read this file first, in full, before doing anything else on this project.** It captures a very long
prior session's worth of decisions, dead ends, and hard-won technical findings that aren't obvious
from the code alone. Skipping it risks re-deriving things that already cost real time to work out.

---

## 1. What this project is

A PLC + WinCC Unified HMI system to remotely monitor/control motorized ballast/bilge valves on the
vessel **MV WESTERLY**, for the client **Burhani Technologies**. S7-1200 CPU (1214C), 3x ET200SP remote
I/O stations (AFT/MID/FWD), MTP1500 Unified Comfort Panel HMI. Built almost entirely by driving TIA
Portal V20 programmatically via the Openness API (C# tools compiled with `csc.exe`), not by hand-editing
in the TIA UI. See `CLAUDE.md` in the repo root for the original engineering spec.

## 2. Current status — what's built and verified

- **89 valve slots** in the PLC (`Array[1..89]` throughout `Valves_DB`, `Valve_Meta_DB`,
  `Valve_Channels_DB`, `FB_ValveLoop`'s SimStep array). **Resized 96 → 89 on 2026-08-13**: capacity now
  exactly equals the real valve count, no spare padding. The earlier 96 was headroom; it was dropped
  because the schedule is settled and the padding slots were showing up as phantom "unconfigured"
  valves. The 89 real valves were repacked into contiguous slots 1-89 in the process (CM89 and CM90
  previously sat at 90/91 and would have been truncated — check this first if slot numbers look off
  against an older note).
- **Zones**: AFT = slots 1-27, ER/Bilge = slots 28-54, FWD = slots 55-89. Exact fit — 27 / 27 / 35
  real valves, no spare slots in any zone. These boundaries are duplicated in `FB_ValveLoop` (zone
  counters + the three paged table windows), `GenerateHmiLayout.cs` (alarm zone area), and
  `MarineScreens.cs` (KPI boxes, bulk-configure buttons, vessel mimic) — change all of them together.
- **The real valve count is 89.** Note the earlier "88" figure was the client's count of valves *with
  I/O assigned* — it excluded CM89, which is a real valve that simply had no channel assignment yet.
  CM89 now has channels like every other valve, so 89 is the working number throughout. See section 4
  for how that got confusing; worth reading before touching counts again.
- **Full HMI rebuild done**: Home (vessel mimic), Screen_AftBallast, Screen_Bilge (labelled "BILGE AND
  FIRE" — this is correct, not a bug, see section 4), Screen_FwdBallast, Screen_Diagnostics
  (Configuration screen), Screen_Popup (valve control popup). All rebuilt at 89-slot capacity, all
  compiled clean, all saved. **Watch for orphaned HMI tags after any downsize** — the 96→89 resize
  left 119 stale `V090_*`–`V096_*` tags pointing at array indices that no longer existed, which failed
  the HMI compile with "The property PLC tag is invalid" until deleted (`CleanupOrphanTags.exe`).
- **Screen_Alarms was NOT rebuilt this session** — rebuilding it wipes the Alarm Control's `Filter`
  property, which cannot be set via Openness and must be re-entered by hand in TIA's UI afterward. Its
  text alignment is still using the old (buggy) settings — see section 5's alignment bug.
- **Client's valve schedule is loaded** into `Valve_Meta_DB` as DB start values (CmNo, VTag, SysName,
  Location, FuncName per valve) — verified field-by-field against their Excel
  (`Annex_B_IO_List_Full_Mapped.xlsx`). The CM89 SysName fix is now imported and live (was pending on
  the old laptop's expired licence; done 2026-08-13).
- **Dynamic I/O Mapper is now LIVE and end-to-end verified** (2026-08-13/14). `FC_PhysicalIoCopy` and
  `FC_IoMapper` are both called from `Main[OB1]` every scan, in that order (physical → buffer, then
  buffer → valve). `Valve_Channels_DB` holds real channel assignments for all 89 valves.
  `FB_ValveLoop` defers to real hardware for any valve with a channel assigned, and drives
  `OpenValve`/`CloseValve` as sustained run outputs that drop on limit-switch arrival or time out.
  **Proven live against PLCSIM**, not just compiled: forcing `%I2.0`/`%I2.2` drove CM25 to
  open+healthy on the HMI; forcing both limit switches at once correctly held the double-indication
  fault even against a healthy real signal, and released when the condition cleared. See section 6a
  for the exact test method — it is repeatable and worth re-running after any change here.

## 3. Full todo list

Statuses updated 2026-08-15. Numbering kept stable so older notes referencing "item 7" still line up.

1. **[done 2026-08-17]** Discrete alarms regenerated for all 89 slots — **632 alarms** (89 × 7 + 9
   system). Text and Origin now carry **CM numbers** (name stays `V0xx` as a stable internal id);
   `_Conflict` renamed `_DoubleInd` with corrected text; "in automatic mode" → "on remote command".
   See items 22 and 23 for the reasoning and the rename trap.
2. **[partially done]** Visual render check. AFT zone + Home verified live in TIA/runtime 2026-08-14.
   FWD (3 pages), Bilge, and Config (6 pages) still not eyeballed since the 89-slot resize.
3. **[pending]** Client still owes clarification on 7 rows: CM80, CM85, CM86, CM87, CM88, CM90 (marked
   "Tag to verify" / "Duplicate tag on mimic; verify" in their sheet) and CM89 (needs Location/Function
   — its I/O is now assigned, see item 5). **Plus, added 2026-08-14:** the client's Bilge drawing
   (`WB-121-P004`) doesn't reconcile with the Bilge valve list — tags in the schedule that aren't on
   the drawing (CM01, CM04/05, CM11/12, CM13/14, CM23/24), tags on the drawing that aren't in the
   schedule (10-001, 10-002, 10-006, 10-008, 10-009, 10-013..016, 10-019, 10-050..063), one class
   mismatch (CM08 listed A1, drawing shows A2/A3), and all four Fire tags (CM94-97, `20-xxx`) absent
   from that drawing entirely. Also unresolved: the drawing's title block reads **M/V GROTON
   (IMO 9246310)**, not MV Westerly. **Bilge physical wiring should not be finalised until answered.**

   **Added 2026-08-16 — TWO TIMING FIGURES NEEDED PER VALVE, for all 89.** Both are currently
   one-size-fits-all constants that were only ever right for the simulation, and both cause real
   misbehaviour on real hardware if wrong. Ask the client for each valve, or measure at commissioning:

   - **Full travel time** (seat to seat, each direction). Drives `TravelTimeout`, hardcoded at
     **8 s** today (`FB_ValveLoop`, the `TimerOpen`/`TimerClose` PT). Because the output is
     *maintained*, a value shorter than real travel does not merely raise a nuisance alarm — it
     **de-energises the solenoid and stops the valve mid-stroke**. See item 13.
   - **Seat-break time** — how long the valve holds its *starting* limit switch after the actuator
     begins to drive. Drives the direction/limit discrepancy grace, hardcoded at **5 s** today
     (`DirTmr` PT). If the grace is shorter than the real seat-break time, **every normal stroke
     raises a false direction fault**. A large DN400 butterfly can hold its limit switch for
     several seconds. See item 18.

   Rule of thumb until real numbers exist: travel timeout = measured travel x ~1.5; grace =
   measured seat-break x ~2, and always well under the travel timeout. Verified live 2026-08-16 that
   the grace does fire at exactly 5 s as designed — the figure is correct, it is the *sizing* that is
   unknown.

   **Also added 2026-08-16, from cross-checking the client's own design P&ID against their schedule
   (aft/ER portion only — the forward half of the PDF has no tag text to check against):**
   - **Seven valves are drawn on the P&ID but appear nowhere in the 102-row schedule:**
     `11-007/300`, `11-013/300`, `11-017/125`, `11-018/125`, `11-019/125`, `11-070/300`,
     `11-090/200`. All drawn as EH valves with A-class codes, identical in kind to valves that *are*
     listed. If they are in VRC scope the schedule is short by seven rows and the PLC needs more
     than 89 slots.
   - **CM51 looks like a digit transposition.** Schedule says `11-009-A1` at "Aft peak C"; the
     drawing labels that valve **`11-090/200`**, and `11-009` appears nowhere on it.
   - **The `System` column is not positional.** `CM77` = `11-014-A1` is filed as *Ballast Fwd* but
     `11-014/300` sits in the engine room around Fr 50. `CM86`-`CM90` are likewise *Ballast Fwd* with
     Location "Cross-over manifold" (midships). This matters because the PLC zone split
     (AFT = slots 1-27) was derived from `System`, so a valve can appear on the wrong zone screen.
   - Request a **tag-numbered forward half** of the P&ID so the FWD illustration can be verified the
     same way instead of guessed.
4. **[done 2026-08-13]** CM89 SysName fix re-imported and verified live.
5. **[done 2026-08-13]** `Valve_Channels_DB` populated — all 89 valves × 6 channels, assigned
   zone-to-station (AFT→`ET200SP_AFT`, ER→`MID`, FWD→`FWD`), 4 consecutive DI + 2 consecutive DQ per
   valve. Verified by read-back. Client-facing wiring schedule generated as
   `MV_Westerly_IO_Wiring_Schedule.xlsx` (DI + DO sheets, sorted by PLC address). Note: this DB was
   restructured from `Array[1..89] of UDT_Valve_Config` into six flat parallel `Array[1..89] of Int`
   members — Openness `Import` rejects StartValues on struct-typed array elements in V20 (four
   encodings tried, all rejected); flat arrays accept them cleanly. `FC_IoMapper` updated to match.
6. **[done 2026-08-14]** `FB_ValveLoop` real-channel guard added, plus the `OpenValve`/`CloseValve`
   run-output latch that was missing entirely (nothing ever set those, so `FC_IoMapper` had no signal
   to drive a real output with — real valves would have shown status correctly but never actually
   moved). Fail-to-open/close timeouts extended to real valves; E/F alarms guarded against
   false-tripping during legitimate real-valve travel.
7. **[done 2026-08-14]** `FC_PhysicalIoCopy` + `FC_IoMapper` wired into `Main[OB1]`, in that order.
   **The old note here was wrong** — this did *not* need a manual TIA UI edit. Exporting OB1, appending
   two `SW.Blocks.CompileUnit` networks with `<Call>`/`CallInfo BlockType="FC"` copied from the
   existing `FB_ValveLoop` call, and re-importing works fine. Watch the closing-tag balance when
   patching; a dropped `</SW.Blocks.CompileUnit>` is the first thing to check if import complains.
8. **[done 2026-08-17]** `Screen_Alarms` rebuilt (`--only=Alarms,AlarmColumns`). AlarmControl placed,
   8/8 columns configured, marine theme applied, PLC 0/0 and HMI 0 errors afterwards.
   **The old note here was wrong** — it claimed the Filter had to be re-entered by hand because
   Openness cannot set it. It is set in code (`GenerateHmiLayout.cs` ~line 696) and survives a
   rebuild; proven by the fact that the debug line immediately after the assignment reached the log.
   The filter matters: without it WinCC's own runtime alarms (`PlcInStopAlarm`,
   `PlcDisconnectedAlarm`, memory-space warnings) mix in with the valve alarms.
   Note `--only=Alarms` also re-runs the full 632-alarm generation — there is no flag separating the
   screen from the alarm set. It is idempotent (Find-then-Create), just ~45 min of extra runtime.
9. **[pending]** Fail-safe design review (what should each fault condition actually *do* to a valve —
   hold/close/stop), PUT/GET security lockdown decision, PROFINET single-port topology question
   (largely resolved — bus adapters have 2 ports each, so daisy-chain works, but a switch is still
   arguably better for reliability on a vessel), FAT bench test before anything goes near the ship.
10. **[done 2026-08-13]** `PLC Address`/`Terminal` translation delivered — see item 5's Excel.

11. **[pending — HIGH PRIORITY, safety-relevant]** **ET200SP station-failure detection does not exist.**
    Found 2026-08-15 while checking whether PLCSIM could simulate ET200SP diagnostics (it can't —
    PLCSIM simulates the CPU process image including distributed-I/O addresses, which is why forcing
    `%I2.0`/`%I12.0` correctly drove real valve logic, but it does not simulate PROFINET-level events
    like station offline, module pulled, or wire break).

    Three separate gaps, all confirmed by reading the code:
    - `Valves_DB.HwHealthy[1..9]` is set `TRUE` once at startup (`FB_ValveLoop`, ~line 156) and
      **nothing anywhere ever sets it `FALSE`**. It is read at ~line 544 only to pack into `HwWord`,
      which drives the System-area discrete alarms — so those alarms can never fire.
    - The HMI **SYSTEM STATUS panel is entirely hardcoded** (`BuildSysStatus` in `MarineScreens.cs`):
      static green dots and literal `"OK"` text for `PLC S7-1200`, `ER RIO`, `FWD RIO`, `AFT RIO`,
      `UPS`, `PROFINET`. No tag bindings at all. It is decoration.
    - **No diagnostic OBs exist.** The project has only `Main [OB1]` — no OB82 (diagnostic error
      interrupt), no OB86 (rack/station failure).

    Consequence on the vessel: if an ET200SP station drops (cable, switch, station power), the PLC
    does not notice, the HMI still shows that station "OK", and every valve in that zone freezes
    displaying its last-read position with no operator indication. For a ballast system that is a
    real safety gap — a stale "OK" is worse than no indicator.

    Fix approach (not started): add OB86 (rack/station failure) and/or use the S7-1200 `DeviceStates`
    instruction to poll PROFINET IO device status; drive `HwHealthy[]` from that; then bind the
    System Status panel's dots/text to those tags instead of literals. Genuinely testable next week
    with real hardware — unplug a station's PROFINET cable and watch the panel go red. Until it is
    built, consider greying the panel or labelling it "not monitored" so nobody trusts a false OK.

12. **[pending — design decision, not urgent]** **`Healthy` does double duty and should probably be split.**
    Raised 2026-08-15 from the principle that the four feedbacks (`OpenFB`, `ClosedFB`, `LocalMode`,
    `Healthy`) are **valve-owned signals** — the valve reports its true physical state on them,
    including while it is being worked by hand in Local Mode — so the PLC must only ever read them,
    never write them.

    Two places knowingly break that rule, both deliberately:
    - `FB_ValveLoop` ~line 309 and `FC_IoMapper` ~line 106: the double-indication trip
      (`IF OpenFB AND ClosedFB THEN Healthy := FALSE`) overwrites the actuator's genuine healthy
      contact with a *computed* conclusion. It fails safe and is verified working (Test 4), but the
      raw signal is lost — you cannot tell "the actuator says it is faulted" from "we decided it is
      faulted because both limits are made".
    - Cleaner design: leave `Healthy` untouched as the raw signal, carry the conclusion in a separate
      `PositionFault` flag, and have `StateCode` consider both.
    - **Why not done now:** `StateCode`, the alarm word packing (`W_Unhealthy`), and several HMI
      bindings all read `Healthy` today, so it is a real refactor, not a rename. Deferred rather than
      done days before an install. Decide with the client during the fail-safe review (item 9).

    *(Already fixed under the same principle: the HMI Reset Fault script no longer writes
    `OpenFB`/`ClosedFB`/`Healthy` — on a real valve that manufactured a phantom Unexpected Movement.
    And `FB_ValveLoop`'s first-scan init no longer seeds `Healthy` for real-channel valves.)*

13. **[pending — BLOCKER for install]** **The 8 s travel timeout is a simulation number and will stop
    every real valve mid-stroke.** Raised 2026-08-15 from the "how do the output commands work"
    question. Context first, because the two facts only bite when combined:

    - **The command style is MAINTAINED, not pulsed** — and that is correct, confirmed against the
      BOM. `FB_ValveLoop` ~line 213-260: `OpenCmd` is consumed in one scan and cleared; what persists
      is the run latch `OpenValve`/`CloseValve`, which `FC_IoMapper` (~line 111-115) writes to
      `IO_Buffer_DB.DQ[]` → `FC_PhysicalIoCopy` → real `%Q` → interposing relay, **held energised**
      until the real limit switch confirms arrival (~line 254-255), the timeout expires, or
      LocalMode/unhealthy forces it off (~line 256-259).
    - **Why maintained is the only option here:** the BOM has **176 interposing relays = 88 × 2** —
      one OPEN, one CLOSE per valve, and **no third relay for STOP**; the I/O schedule likewise has
      4 DI + 2 DO per valve with no spare. A pulse scheme needs something downstream that latches
      (seal-in contactor, bistable relay), and anything that latches needs a way to un-latch. Nothing
      in the BOM can do that, so the field side has no memory: relay energised = running, dropped =
      stopped. Also matches the actuator type — Pleiger EHS is electro-hydraulic, the solenoid
      directs oil to the ram, so de-energising re-centres the spool and travel stops.

    **The defect:** `PT := T#8S` (`FB_ValveLoop` lines 276 and 296) dates from when simulated travel
    was 3 s. Real DN200-DN400 hydraulic ballast valves typically stroke in **15-60 s**. Because the
    output is maintained, that timeout does not merely raise a nuisance alarm — **dropping the latch
    de-energises the solenoid and physically halts the valve.** On real hardware every valve slower
    than 8 s will stop part-way, latch Fail-to-Open/Close, and sit with both limit switches off
    (position unknown). Every valve, on day one. With a pulsed scheme a short timeout would be
    cosmetic; with a maintained scheme it is a hard stop. That is why this is a blocker.

    **Fix:** make the timeout per-valve and configurable. `UDT_Valve_Config` in `CLAUDE.md` already
    specifies a `TravelTimeout` (Time) member — it exists in the design and the code just never used
    it. Default generously (60 s) until stroke times are measured at commissioning, then tune each to
    measured × ~1.5. This also lines up with the already-confirmed fact that travel time genuinely
    varies by valve type (butterfly vs globe).

14. **[pending — confirm before the panel is wired]** Three hardware questions that follow directly
    from the maintained-output decision in item 13:
    - **Continuous-duty coils.** Maintained means the coil is held for the whole stroke, and the
      8 s → 60 s change in item 13 lengthens that hold ~7×. Confirm the Phoenix relay coils and the
      Pleiger solenoids are continuous-duty rated. They almost certainly are, but this is the one
      assumption a maintained output makes that a pulsed one does not.
    - **Hardware interlock between OPEN and CLOSE.** Software already guarantees mutual exclusion
      (`FB_ValveLoop` ~line 210-212 plus the paired assignments), but standard practice on a reversing
      circuit is to cross-wire the two relays' NC contacts so both can never energise even from an I/O
      card fault. Cheap, and a class surveyor will look for it.
    - **Failure mode on power / comms loss.** If an ET200SP drops, its DQ module de-energises and the
      valve **stops in place**. Confirm with the client whether stop-in-place or fail-closed is wanted.
      Decide alongside item 9 (fail-safe review); interacts directly with item 11 (station-failure
      detection), since today nothing even notices the station is gone.

15. **[done 2026-08-15]** Popup PLC-mirror refactor — the popup was slow to populate because ~14 of its
    scripts polled on `T500ms`, forced there because each built its tag name from `SelectedValve` at
    runtime and WinCC's `AutomaticTags` trigger only tracks statically-known tag names. `FB_ValveLoop`
    now copies the selected valve into seven fixed mirror members each scan (`SelIdx`, `SelState`,
    `SelHealthy`, `SelLocalMode`, `SelConfigured` in `Valves_DB`; `SelCmNo`, `SelVTag` in
    `Valve_Meta_DB`), keyed off `SelIdx` which the HMI writes on valve tap. All 7 popup display
    scripts read those fixed names and run on `AutomaticTags` — net ~14 polled scripts → 0. Also on
    this pass: SYSTEM/LOCATION/FUNCTION dropped from the meta card (VALVE TAG only) and the
    SIMULATE STUCK button removed, both on request. **PLC 0/0, HMI 0 errors.** Two traps worth
    remembering: newly created HMI tags need the `--fix-tags` repair pass to bind their PLC address
    (the normal run path passes `forceRefresh=false`), and `--only=<anything>` always rebuilds
    Screen_Popup, so `--only=Login` is the cheap way to regenerate just the popup.

16. **[done 2026-08-17]** **FUNCTION column added to the zone-screen VALVE LIST.** Needed a PLC change too — `<Zone>TblFunc` arrays in `Valve_Meta_DB` plus packing in `FB_ValveLoop` — and a re-budget of every column width (LOCATION gave up the most, since its real values are short). Original request: to the zone-screen VALVE LIST.** Requested 2026-08-15
    from a live screenshot of Screen_AftBallast. The table currently runs CM NO / VALVE TAG /
    LOCATION / STATUS / COMMAND in two side-by-side halves, and there is visible slack — widening
    into it with FUNCTION costs nothing and puts the one identity field the operator can't otherwise
    see on this screen in front of them. Applies to all three zone screens (Aft, Fwd, Bilge) so they
    stay consistent.

    Implementation note so this isn't re-derived: the data exists (`Valve_Meta_DB.FuncName[i]`, and
    the Config screen already renders it via the `Cfg_TblFunc_<slot>` tags), but the **zone** tables
    read PLC-side mirror arrays and only `<zp>TblCmNo` / `<zp>TblVTag` exist today
    (`GenerateHmiLayout.cs` ~line 1613-1614). So this needs a matching `<zp>TblFunc` array added to
    `Valve_Meta_DB` plus the packing loop in `FB_ValveLoop`, following the exact pattern the existing
    two already use — not just a column added on the HMI side.

17. **[done 2026-08-17]** **Fixed properly: `Authorization` is now set inside `BuildValveTable` at creation time**, not by the `--finish-login-auth` repair pass — which set it on live objects, so every zone-screen rebuild silently wiped it. Original report:
    users.** Reported live 2026-08-15: on Screen_AftBallast the per-row OPEN/CLOSE buttons are fully
    active as DEFAULTUSER. Wanted: greyed out when nobody is logged in, and clicking one should
    prompt for login.

    **Most likely cause — check this first, it may be the whole bug.** The `Authorization="Operate"`
    on the 84 table buttons (`Tr_Open_*` / `Tr_Close_*`) is **not set in the normal screen-build
    path**. It is applied only by the separate `--finish-login-auth` fixup pass
    (`RunFinishLoginAuth`, `GenerateHmiLayout.cs` ~line 1700-1763), which walks the three zone
    screens and sets it on the live objects. So **every zone-screen rebuild silently wipes it** —
    and `Screen_AftBallast` was recreated on 2026-08-15 during the popup refactor
    (`--only=Aft`) with no `--finish-login-auth` run afterward. That timing matches the report
    exactly. Same exposure almost certainly applies to Bilge and Fwd whenever they were last rebuilt.

    Two things to do, in order:
    - **Short term:** re-run `HmiBuilder.exe --finish-login-auth` and confirm it reports 84 buttons.
    - **Proper fix:** set `Authorization` inside `BuildZoneScreen` where the buttons are created, so
      it survives a rebuild, and retire that part of the fixup pass. The popup's own six buttons
      already do it this way (`SetStr(btn, "Authorization", "Operate")` at ~line 438/447/513/523),
      which is why the popup has never had this problem. Note `Btn_AckAll` on Screen_Alarms is a
      third case — set in source but on a screen excluded from every rebuild, so it too depends on
      the fixup pass.

    **On the "require login popup" half:** `Authorization` natively greys/blocks the control, but
    WinCC Unified does not pop a login dialog from a denied press on its own (see the earlier
    login/logout finding — Unified handles login through Authorization, not a coded dialog). If a
    prompt is genuinely wanted, it likely has to be built: leave the button enabled, and have its
    script check the logged-in user and route to `Screen_Login` (or a popup wrapping it) when there
    is none. Decide which behaviour the client actually wants — grey-out alone is the standard,
    lower-risk answer and is what most vessel HMIs do.

18. **[done 2026-08-16]** **Command-aware state + direction/limit discrepancy detection (Phase 1).**
    Triggered by a live reversal test: press OPEN, press CLOSE, then make OpenFB. The close output
    stayed correctly energised, but `StateCode` — computed purely from the two limit switches, never
    from the run latches — reported a settled green **FULLY OPEN** for the full 8s travel timeout
    before raising a misleading Fail-to-Close. Operator commanded CLOSE, panel said OPEN, water
    flowing. The same blind spot was present on *every normal stroke*: a valve commanded CLOSE while
    still on its open limit gave no acknowledgement the command had even registered.

    Built:
    - **Fix A** — `StateCode` now checks the run latches *before* the position feedbacks:
      FAULT > LOCAL > **6=OPENING** > **7=CLOSING** > OPEN > CLOSED > 5=position-unknown. Both new
      codes count toward `TotalTransit`. All four `CASE` blocks (Aft/Er/Fwd/Cfg table text) emit
      `'OPENING'`/`'CLOSING'`.
    - **Fix B** — `DirTmr[i]` (5s) + `DirFault[i]`: driving one way while the *opposite* limit is
      still made past the grace means either the valve went the wrong way (OPEN/CLOSE swapped at the
      interposing relays) or it never left its seat. Latches, drops both run latches, feeds StateCode
      and `W_DirFault`. The grace is essential — a valve leaving a fully-open seat legitimately holds
      OpenFB for the first moment of a close. **The PLC deliberately does NOT try to correct
      direction**: deducing "the wiring looks swapped so drive the other output" would bake wiring
      compensation into safety logic and invert every valve the day someone fixed the wiring.
    - `CmdPos[i]` (0/1/2) — last commanded position, never cleared on arrival, for the HMI's
      commanded-vs-actual display. This is what catches the *likelier* in-service fault: a stuck
      limit switch making a valve report a position it is not in.
    - Reset Fault clears `DirFault`; re-latches within 5s if the fault is real, same as the
      double-indication and Loss-of-Position trips already did.
    - New members: `DirFault`, `DirTmr`, `CmdPos`, `W_DirFault`, `SelDirFault`, `SelCmdPos`.

    **Also fixed here — the predicted T14 bug, confirmed by reading the code.** De-configuring a valve
    mid-travel left `OpenValve`/`CloseValve` latched, and because `FC_IoMapper` skipped unconfigured
    valves the DQ bit kept its last value while `FC_PhysicalIoCopy` went on copying it to the real
    `%Q` **forever** — relay energised with nothing managing it, screen showing a harmless
    UNCONFIGURED. Needed *two* changes, not one: `FB_ValveLoop`'s unconfigured branch now drops both
    run latches, **and** `FC_IoMapper` now drives the DQ writes unconditionally, outside the
    `Configured` guard (that guard exists to stop a real DI overwriting a simulated feedback and had
    no business gating outputs). Clearing the latches alone would have done nothing.

    PLC 0/0, HMI 0 errors. **Not yet re-tested live** — the T1-T14 procedure should be re-run against
    the new states before install.

19. **[pending — decisions needed from the client/user before Phase 2 screens]** Illustration
    reconciliation, from the artwork supplied 2026-08-16 (`C:\hmi_graphics\`). Box positions are
    extracted precisely by `DetectBoxes.exe` (scans for the grey valve squares, prints exact centres)
    so overlay placement is measured, not estimated — re-run it rather than eyeballing coordinates.

    | File | Size | Boxes | Box px | Labels |
    |---|---|---|---|---|
    | AFT zone1.png | 1888x500 | 32 | 29-30 | 23 real CM, 9 blank |
    | FWD Zone1.png | 1888x500 | 32 | 29-30 | all `CM00` |
    | Full.png | 1888x584 | 64 | 19-20 | all `CM00` |

    All 23 labelled AFT boxes cross-check as System = "Ballast Aft" in
    `Annex_B_IO_List_Full_Mapped.xlsx` (which is unchanged: 102 rows, 13 Fuel Oil excluded -> 89,
    split Aft 27 / Bilge 27 / Fwd 35). Ballast-Aft valves *not* labelled: **CM57, CM58, CM80, CM85**
    (CM80/CM85 are two of the seven the client already owes clarification on, so blank is consistent;
    CM57/CM58 are confirmed valves that simply have not been placed).

    Open, all blocking accurate placement:
    - **AFT drawing has 32 boxes but the AFT zone has 27 valves.** 23 + 4 unplaced = 27, leaving
      **5 boxes that are not Ballast-Aft valves**. There are exactly 5 unconfirmed *Ballast Fwd*
      valves (CM86-CM90) — suggestive, not assumed. If those do sit physically aft, the schedule's
      `System` column disagrees with physical position, which matters because the PLC zone split
      (AFT = slots 1-27) was derived from `System`, not from position on the drawing.
    - **No Bilge illustration exists** — that is 27 valves, as many as AFT.
    - **64 boxes drawn vs 62 ballast valves** (27+35): two unaccounted for.
    - **CM numbers are baked into the PNGs as pixels**, and 62 of 64 currently read `CM00`.
      Recommendation: strip labels from the artwork and have the HMI draw them as text from
      `Valve_Meta_DB.CmNo`, so confirming numbers later is a config change rather than a
      redraw-and-reimport — and so a printed label can never disagree with the live overlay.

20. **[done 2026-08-16]** **HMI tag creation: set Connection BEFORE the PLC address.** This was the
    root cause of the long-standing "new tags never bind, run `--fix-tags` afterwards" dance, and it
    is now fixed rather than worked around. `CreateSummaryTag` set `PlcTag` first and assigned
    `Connection`/`PlcName` after — but a brand-new tag has no PLC connection yet, and TIA rejects an
    address on an unconnected tag. The setter's `catch {}` swallowed the exception, so the warning
    read "Could not set address" and never said why. Every newly created tag therefore failed its
    address on the run that created it. Order reversed; the following `--fix-tags` reported **zero**
    warnings where the previous run reported 43. The WARN now also prints the real exception and the
    address it attempted, so a recurrence diagnoses itself.

    Corollary: `_State` tags (`V001..V089_State`, read by every mimic badge, diagram overlay and the
    popup) were never created by this generator at all — inherited from an earlier script and merely
    happened to resolve. It stopped happening the moment a screen referenced slot 89: `V089_State`
    had never existed (pool was 96, cut to 89, orphan cleanup removed V090-V096). Now created
    explicitly for all 89, so the set is owned by the generator and complete.

21. **[done 2026-08-16]** **Phase 2 screens, partial.** Home rebuilt on the client's `Full.png`
    artwork; AFT and FWD rebuilt on `AFT zone1` / `FWD Zone1`. Overlay coordinates are **measured**
    by `DetectBoxes.exe` (scans the PNG for the grey valve squares, prints exact centres) rather than
    estimated — re-run it if artwork is re-exported, do not eyeball. Box size is a parameter now:
    30px on zone sheets, 20px on Home, matching what the artwork actually measures.

    Also landed: `Authorization` moved into `BuildValveTable` at creation time (the real fix for
    item 17 — the repair pass set it on live objects, so every rebuild wiped it); FUNCTION column
    coded end-to-end (`<Zone>TblFunc` arrays in `Valve_Meta_DB`, packing in `FB_ValveLoop`, 42 HMI
    tags, column widths re-budgeted with LOCATION giving up the most room).

    **Not yet visible on the panel:** the FUNCTION column and the new Authorization only appear when
    a zone screen is *redrawn*, and the Aft/Fwd/Bilge rebuild was cancelled part-way to save time.
    Run `--only=Aft,Fwd,Bilge` when the panel is free. OPENING/CLOSING already works everywhere
    without a redraw, because the PLC supplies the status word and existing cells just display it.

    Still outstanding in Phase 2: commanded-vs-actual on the popup.

22. **[done 2026-08-16]** **Full T1-T22 fault procedure run live against CM79 (slot 21) — all passed.**
    Procedure published as an artifact (step-by-step, with the two standard "enter travel" moves).
    Confirmed fixed by live test: the queued-command bug (local mode), the direction fault in both
    forms, and the de-configure-mid-travel output latch — the last of which had only ever been
    predicted from reading the code.

    **Four defects found BY the testing, all fixed the same day:**
    - **Empty-slot code collision.** The zone tables used state code 6 to mean "no valve in this
      row", and 6 had just become OPENING. An opening valve rendered its status text transparent.
      Sentinel moved to 9 (outside the StateCode range - keep it that way if more states appear).
    - **The "Command Conflict" alarm was mislabelled.** `W_Conflict` is packed from
      `OpenFB AND ClosedFB` - double indication - not from a command conflict. Proven live: test D1
      (both limits made) raised it, test C5 (a genuine OpenCmd+CloseCmd in one scan) raised nothing.
      Renamed `_Conflict` -> `_DoubleInd` with matching text. The real command conflict deliberately
      gets no alarm: an operator cannot press two buttons in one scan, the interlock clears both,
      and nothing moves.
    - **Popup buttons stayed lit on an unhealthy valve.** The lock styling checked LocalMode and
      Configured but not Healthy, while the PLC guard is `NOT LocalMode AND Healthy` - so presses
      were discarded silently with no explanation. `Healthy` added to the lock condition.
    - **`V089_State` never existed.** The `_State` tags were inherited from an earlier script and
      merely happened to resolve; slot 89 had never been created. Now generated explicitly for all
      89 (see item 20).

    **Three design changes agreed from what the testing revealed:**
    - **Code 5 renamed `MOVING` -> `NO POSITION`.** It means "neither limit switch made", which is
      not motion - and the moment an operator actually sees it is the 2s Loss-of-Position debounce
      after Reset Fault, with the valve sitting perfectly still. 6 and 7 carry real motion now.
    - **Travel timeouts self-clear on arrival.** A latched Fail-to-Open on a valve that has since
      opened correctly showed FAULT on a working valve. Now raised -> cleared, still in history,
      still needs acknowledging. `UnexpMove` and `DirFault` stay latched deliberately - the first
      has no "resolved" state, the second points at wiring that does not fix itself.
    - **Position and fault are now separate.** `StateCode` = 1 used to *replace* the position, so a
      faulted ballast valve could not be read as open (flooding path) or closed (blocked line). New
      `PosCode[]` (position only, never faulted) and `FaultCode[]` (which fault) drive a popup line
      reading e.g. `FULLY CLOSED · FAIL TO OPEN · REMOTE`. Also kills the contradictory
      `FAULT · HEALTHY` that appeared when a latched alarm coexisted with a fine healthy contact.

    Also confirmed by reasoning during the run, no change needed: **reversal cannot accumulate
    travel time.** The timers are per direction and reset when their run latch drops, so a reversal
    gets a fresh full-stroke timer, which always covers a distance shorter than a full stroke.

23. **[done 2026-08-16]** **Single full rebuild landed everything.** Command used:
    `--only=Home,Bilge,Fwd,Aft,Diag,Login,DiscreteAlarms,AlarmColumns` - deliberately **excluding
    the `Alarms` key**, which rebuilds Screen_Alarms and would wipe its hand-entered Filter.
    `AlarmColumns` re-applies the column config to the existing view instead. ~90 min.

    Landed: FUNCTION column and button `Authorization` on all three zone lists; mimic/diagram boxes
    now fill by position with a red (fault) / amber (local) border via native value maps - **not** a
    flashing script, because 89 boxes re-evaluating on a 1Hz tick is real load and flashing is worth
    reserving to mean "unacknowledged"; popup position/fault split and red-to-position flash;
    **632 discrete alarms regenerated** carrying **CM numbers** in text and Origin (name stays
    `V0xx` as a stable internal id, mapped from `HOME_DIAGRAM` so there is one source of truth),
    and "in automatic mode" corrected to "on remote command".

    **Trap worth remembering: renaming an alarm is a two-step operation.** Regenerating creates the
    new name but leaves the old alarm bound to the SAME trigger tag and bit, and TIA rejects that -
    "the tag/bit combination is also used for other HMI alarms". 176 errors = 88 collisions seen
    from both sides. New `--purge-alarms=<suffix>` flag on HmiBuilder deletes by name suffix;
    `--purge-alarms=_Conflict` removed 88 (not 89 - slot 89's never existed, the set was last
    generated at 88 slots). Final state: **PLC 0/0, HMI 0 errors.**

    Note the Openness attach hung for a full 10-minute timeout on the first purge attempt and
    succeeded immediately on retry - TIA was still busy from the compile. A failed attach right
    after a long operation is worth simply retrying before investigating.

24. **[pending — designed, deliberately not built 2026-08-17]** **Mimic fault flash, computed in the
    PLC.** The mimic boxes currently show position as fill with a red fault border; the popup circle
    flashes red-to-position. Two visual languages for the same thing. Agreed target is to flash on
    the mimic too, but the flash must NOT be a script — 89 boxes re-evaluating on a 1Hz clock tick
    is real load on the panel, and this project already learned that lesson once when polled popup
    scripts made it slow to populate.

    **Design: let the PLC do the alternating, and bind a native value map to the result.** Add
    `DispCode[1..89]` Int to `Valves_DB`, computed beside `PosCode`/`FaultCode`:

    ```
    IF NOT Configured                  THEN DispCode := 0;        // unconfigured
    ELSIF FaultCode > 0 AND Clock_1Hz  THEN DispCode := 1;        // red half of the flash
    ELSIF LocalMode                    THEN DispCode := 2;        // amber, steady
    ELSE                                    DispCode := PosCode;  // 3/4/5/6/7
    END_IF;
    ```

    The HMI binds `BackColor` to one native value map on `_DispCode` — **zero scripts**. The key
    property: when nothing is faulted `DispCode` equals `PosCode` and simply does not change, so
    there is no update traffic at all. Cost scales with the number of faults, not the number of
    valves — the opposite of the scripted version. It also collapses two bindings (fill + border)
    into one, so the border can go back to plain dark 1px for definition only, and the
    inset-vs-outset border question disappears with it.

    Applies identically to Home, AFT and FWD. Optionally drive the popup circle from the same code
    for a single source of truth.

    **Watch when built:** 1Hz flashing needs the HMI tag acquisition cycle fast enough not to alias
    the square wave (0.5s high / 0.5s low). The popup's existing `Clock1Hz` flash proves the
    mechanism works on this panel, but with 89 tags it is worth checking once several faults are
    active together. Fallback if it stutters is a slower flash — one line in the PLC.

25. **[done 2026-08-17]** **Popup redesigned, and a self-inflicted encoding bug fixed.**

    **The encoding bug first, because it is the reusable lesson: NEVER round-trip a source file
    through PowerShell `Get-Content` / `Set-Content`.** Doing the alarm CM-number replacement that
    way corrupted 51 sequences in `GenerateHmiLayout.cs`: `Get-Content` read the UTF-8 file as ANSI
    (Windows-1252), mangling every multi-byte character, and writing back as UTF-8 double-encoded
    the damage. On the panel that showed as `CM79 â€" CONTROL PANEL`, `â–² OPEN VALVE`,
    `âš¡ RESET FAULT`. `MarineScreens.cs` was untouched only because it was edited exclusively with
    the Edit tool — which is why the nav bar still rendered correctly and the popup did not.
    Repaired by reversing the mojibake (re-encode CP1252, re-decode UTF-8); 46 glyphs restored, no
    data loss, verified before writing. **Use the Edit tool for source, or explicit
    `[System.IO.File]::ReadAllText/WriteAllText` with an explicit UTF8 encoding object.**

    **Redesign, from panel review.** Four problems with the old layout, all fixed:
    - Position, health and mode were crammed into ONE line, which truncated — "REMOTE" fell off the
      end entirely on a faulted valve. Each now has its own row.
    - Position had no visual weight. It is the most important fact about a ballast valve (open =
      flooding path, closed = blocked line) and read as a fragment mid-sentence. Now a 27pt
      colour-coded word beside the circle, and the hero of the popup.
    - The VALVE TAG card spent a whole row on one value. The tag moved into the header next to the
      CM number (`CM79 · 11-011-A3`), which bought that row back. "CONTROL PANEL" dropped — the
      operator opened the popup deliberately.
    - **`CmdPos` was computed since Phase 1 and never displayed.** Now shows `COMMANDED: OPEN` in
      amber, but *only when commanded and actual genuinely disagree*, and stays quiet while the
      valve is legitimately travelling. This is what catches a stuck limit switch, where nothing is
      technically faulted but the valve is not where it was told to go.

    Fault row is blank when healthy rather than printing "HEALTHY" — absence of a red row is a
    cleaner signal than a reassuring word. Popup 360 -> 380 tall. New HMI tag `Valves_DB_SelCmdPos`.

    **API trap worth remembering:** `MakeLiveText` creates an **HmiButton** — it has no alignment
    property (the align argument is silently dropped, text is always centred) and its text property
    is `Text`, not `ProcessValue`. For left-aligned live text use `MakeTb` and bind `Text`.

## 4. The valve count saga — read this before touching counts again

This went through several wrong turns; the final answer is solid but got there messily:
- Original assumption: 88 valves (matches the original 88 pre-allocated slots from the spec).
- A photographed/typed valve list draft suggested 89, with several ambiguous/corrected rows.
- The client then sent an actual Excel (`Annex_B_IO_List_Full_Mapped.xlsx`) with **102 rows total**,
  including a **13-valve Fuel Oil system** that was invisible in earlier screenshots.
- **Client's explicit instruction (WhatsApp screenshot): "ignore all hidden ones [Fuel Oil valves],
  in total there will be 88 valves."**
- 102 − 13 (Fuel Oil) = 89. Of those 89, **CM89 has no DO/DI channel assignment at all** in the sheet —
  it's a real valve (has a tag, a system, a RIO location) but isn't "finished" — its Location/Function
  and I/O are undecided. Excluding it: **88 valves with working I/O = the client's confirmed number.**
- This also means: **the interposing relay count (176, ordered for 88×2) was correct all along.** An
  earlier "2 relays short" message sent to the client was based on a miscount and should be understood
  as superseded — not urgent to correct with them, but don't repeat that number.
- Zone split of the real 88: **AFT 27, ER 27, FWD 34** (+ CM89 pending, which would make FWD 35 once
  it's finished).
- "BILGE AND FIRE" as the ER screen's title is *correct*, not a leftover mistake — the Excel confirms
  ER RIO genuinely carries Bilge valves (27) plus Fire main/pump valves (CM94-97, 4 of them, tagged
  System="Bilge" but functionally fire-related) — a real, deliberate crossover, not a naming error.

## 5. Non-obvious technical findings from this session (avoid re-discovering these)

- **`HmiButton` has no alignment property at all** — confirmed via reflection. `MakeLiveText`'s `align`
  parameter was always silently discarded (the helper `SetProp` swallows failures in an empty catch).
  Any left/right-aligned live text must be built as `HmiTextBox` + `DynTag`, not `MakeLiveText`.
- **`MakeTb`'s `VerticalTextAlignment` was set to `"Middle"`, which isn't a valid value** — the enum is
  `{Top, Center, Bottom, Stretch}`. This silently failed (same empty-catch pattern) and left every
  textbox in the project top-aligned instead of vertically centred, project-wide, until fixed this
  session. Fixed in `MakeTb` itself, so it only affects screens rebuilt after the fix — `Screen_Alarms`
  and possibly others not rebuilt since still have the old behaviour.
- **A latent tag-binding bug**: 7 of the per-valve `Valves_DB.Valve[i].*` tag registrations in
  `CreateSummaryHmiTags` were missing the `forceRefreshNewTags` argument that every other registration
  passes. This meant `--fix-tags` silently skipped them for any *newly created* valve slot. Harmless for
  slots 1-88 (created before that refresh mechanism existed, got their address on first creation), but
  broke all 7 tags for every one of slots 89-96 until fixed. If slots are ever extended again, double
  check this doesn't regress.
- **Openness/C# assembly-loading gotcha (bit everyone on the new laptop)**: if `Main()` directly
  references a type from a dynamically-resolved assembly (e.g. `TiaPortal.GetProcesses()`), the CLR
  needs to resolve that assembly to JIT `Main()` itself — **before** `Main()`'s first statement
  (subscribing `AppDomain.CurrentDomain.AssemblyResolve`) actually takes effect. Every real tool in
  `src/` and `scratch_probe/` avoids this by keeping `Main()` to just the resolver hookup + a call to a
  separate `Run()` method that contains the actual Siemens.Engineering-referencing code. Any new
  one-off probe script MUST follow this shape or it will fail with a confusing
  `FileNotFoundException` even when the DLL genuinely exists at the resolved path.
- **`csc.exe` invoked from Git Bash mangles switches** — MSYS path conversion turns `/nologo` into
  `C:/Program Files/Git/nologo` and the compile fails with CS2001. Always compile via PowerShell (see
  `compile_builder.ps1` in the repo root) or the PowerShell tool directly, never Bash.
- **A brand-new Windows account needs to be added to the local "Siemens TIA Openness" group** before
  any Openness tool can attach — otherwise you get
  `EngineeringSecurityException: ... is not member of the windows group 'Siemens TIA Openness'`.
  `net localgroup "Siemens TIA Openness" "<username>" /add` from an elevated PowerShell. **Critically,
  a full logoff/login (or reboot) is required afterward** — restarting just TIA Portal is not enough,
  Windows bakes group membership into the session at login time.
- **Windows 11 Home has no `lusrmgr.msc` / "Local Users and Groups" in Computer Management** — use the
  `net localgroup` command instead, not the GUI, on Home-edition machines.
- **TIA Portal's own PLC compile occasionally throws a transient
  `EngineeringSecurityException: ... The operation has timed out`** on `Attach()` even when TIA is
  fully healthy — confirmed via CPU-usage checks (TIA burning real CPU = genuinely working, not hung).
  Always retry once before assuming something is actually broken.
- **TIA Portal is delete-and-recreate per screen, not patch-in-place.** Any manual edit made directly
  in TIA's screen editor gets wiped the next time that screen is rebuilt via the generator. All layout
  changes must go into `src/GenerateHmiLayout.cs` / `src/MarineScreens.cs`, never the TIA UI directly
  (the one standing exception: the Alarm Control's `Filter` property, which Openness cannot set at
  all and must be re-entered by hand after every `Screen_Alarms` rebuild).
- **DB start values via Openness use `<Subelement Path="N"><StartValue>'text'</StartValue></Subelement>`
  as a child element**, not `StartValue="..."` as an XML attribute (the attribute form fails import
  with "attribute not declared"). Verified working end-to-end this session for loading the valve
  schedule into `Valve_Meta_DB`.
- **Block `Export` via Openness fails outright if the target file already exists** ("The export cannot
  be made because the file ... already exists") — delete the old export first, or the failure will
  silently look like nothing changed if you don't check the actual error text.

## 6. Machine/environment notes

- This session moved from an old laptop (Windows account "Admin", project at
  `C:\Users\Admin\Documents\Automation\valveDemo2`) to a new laptop (Windows account "abbas") because
  the old laptop's TIA trial license expired mid-session (`STEP 7 Basic` license missing error).
- New laptop setup is now confirmed working: TIA Portal V20 installed with STEP 7 Professional, WinCC
  Unified, WinCC Unified Devices, and **TIA Portal Openness** (under the "Options" node in the
  installer — not visible at the top-level component list, has to be expanded), Automation License
  Manager, trial licensed, Openness group membership added and login-refreshed, verified working with
  a minimal test project.
- Repo remote: `https://github.com/AbbasHirani/PLC-HMI-Valve.git`. All work as of this handoff is
  pushed to `main` (commits up to and including this file).
- Recommended project path on the new laptop: **anywhere outside OneDrive**. OneDrive sync was ruled
  out as the cause of the assembly-loading bug (the real cause was the `Main()`-JIT issue above), but
  it's still worth avoiding for a TIA project given known sync-lock issues with actively-open project
  files.
- `scratch_probe/Siemens.Engineering.dll` (a convenience local copy used by some probe compile
  commands) is **not tracked in git** and won't be present after a clone — recreate it with one copy
  command from `C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.dll`
  if any probe script's compile command references the local copy instead of the full Program Files
  path.
- Hundreds of untracked one-off `.cs`/`.exe`/`.log` files exist in the old laptop's working directory —
  these are deliberately never committed (established convention in this project: scratch diagnostic
  tools are ephemeral, not checked in). None of them are needed on the new laptop.

## 6a. Testing real I/O against PLCSIM (worked out 2026-08-14, non-obvious)

Proving the I/O chain without real hardware. Several intuitive approaches **do not work** — the
working one is last, don't waste time re-deriving the dead ends:

- **TIA watch-table "Modify" on `IO_Buffer_DB.DI[n]` — does not hold.** `FC_PhysicalIoCopy` overwrites
  the whole buffer from the physical addresses every scan, so the modified value survives well under
  a scan.
- **TIA watch-table "Modify" on `%I2.0` — does not hold either.** The CPU refreshes the input process
  image from hardware at the start of every scan, before user code runs. Modify is a one-shot write
  between scans; the refresh immediately overwrites it.
- **TIA Force table on `IO_Buffer_DB.DI[n]` — rejected outright.** S7-1200 only permits Force on
  physical `%I`/`%Q`, not DB memory ("Address cannot be forced").
- **TIA Force table on `%I2.0` — blocked by a monitoring restriction** ("Peripheral inputs can only
  be monitored in expanded mode"). Not worth chasing.
- ✅ **PLCSIM's own SIM table is the answer.** In the PLCSIM window (not TIA): left sidebar → New SIM
  View → library panel → **+** on "SIM Table". Type the address (`I2.0`, no `%`) into the **Address**
  column, set Display Format `Bool`, click the table's **▶** to start monitoring, then tick the
  checkbox in **Monitor/Modify State** to drive the bit. This sets the *simulated hardware* state, so
  the scan-start refresh reads the value you set instead of clobbering it. Values persist until
  changed — deleting a row does **not** reset the bit to FALSE, it just stops showing it (this caused
  real confusion once: a "stuck" fault was simply a still-TRUE input on a deleted row).

Two gotchas that will otherwise look like bugs:
- **`Valves_DB.Valve[i].Configured` resets to FALSE on every PLCSIM download** (it's NonRetain), and
  `FC_IoMapper` skips any unconfigured valve entirely. Set it TRUE (watch table → Modify now) before
  concluding anything is broken. This wasted a solid chunk of one session.
- **Sequence editor**: rejected both `"Valves_DB".Valve[21].OpenCmd` and the bare address with
  "Invalid Tag" — exact expected syntax never established. Watch table + SIM table cover everything
  needed; not worth fighting.

The full fault-case test procedure (12 scenarios: healthy open/closed, unhealthy, double-indication,
local-mode lockout, command→arrival, fail-to-open/close timeouts, loss-of-position, unexpected
movement, command conflict, reset-fault) was written out in chat on 2026-08-15 against CM79/slot 21.
It is not reproduced here — regenerate it from `MV_Westerly_IO_Wiring_Schedule.xlsx` (any valve's six
addresses) plus the seven alarm conditions listed in `GenerateHmiLayout.cs` `CreateAlarms`.

## 7. How to resume

On the new laptop, once the repo is cloned and TIA has opened the `.ap20` once (to rebuild its cache
folders): open Claude Code in that folder and just say what you want to do next. Point it at this file
first if it hasn't already read it. The immediate next actions in priority order are items 4 and 1 in
section 3 (re-import the CM89 fix, then the discrete alarms pass) — both are mechanical, no open
decisions needed. Item 5 (channel mapping) is the next real engineering task after that.
