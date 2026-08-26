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

13. **[pending — BLOCKER for install. The BUILD half is now item 35; this item is the analysis.]**
    **The 8 s travel timeout is a simulation number and will stop
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

24. **[APPROVED 2026-08-18 — build at 0.25 Hz, pair with item 26]** **Mimic fault flash, computed in the
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

    **DECIDED 2026-08-18 after the performance audit — build it, at 0.25 Hz (2 s on / 2 s off).**
    The rate change matters twice over: a 2 s half-period needs only a ~1 s tag acquisition cycle, so
    the aliasing risk below largely disappears, and it quarters the update traffic. Use a 2 s clock
    (a `Clock_0Hz25` bit, or divide `Clock_1Hz` in the PLC) rather than `Clock_1Hz`.

    **It is CHEAPER than what is built today, not more expensive** — this was the open question and
    the audit answered it:
    - The box currently carries **two** live bindings (fill on `_PosCode`, border on `_State`).
      `DispCode` collapses that to **one**. On Home that is 89 fewer bindings, not more.
    - Tag traffic scales with the number of FAULTS, not the number of valves. Healthy plant:
      `DispCode` equals `PosCode`, never changes, **zero traffic**. All 89 faulted at 0.25 Hz:
      ~44 updates/sec — still a quarter of what a scripted flash would cost *permanently while
      everything is fine*.
    - **Zero JavaScript executions.** A scripted flash would be ~178/sec regardless of plant state,
      six times the load that already made the popup measurably slow before item 15.

    Do it in the same `FB_ValveLoop` pass as item 26 — both are edits to that block, so one import
    and one screen rebuild covers both. The screen rebuild IS required, because the box's BackColor
    binding moves from `_PosCode` to `_DispCode`.

    **Still unsettled, and it is a client question not a technical one:** convention (EEMUA 191,
    ISA 18.2) reserves flashing for *unacknowledged* alarms, with steady meaning acknowledged-but-
    active. Flashing on any fault spends that signal early. The mimic does not currently know
    acknowledgement state, so wiring it properly is a bigger job than this item assumes. Raise it in
    the fail-safe review (item 9) before anyone treats the flash as an alarm-management feature.

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

26. **[pending — PERFORMANCE, do before install]** **Gate the paged table-window rebuild.**
    Found in the 2026-08-18 performance audit; **the single biggest contributor to PLC scan time,
    and it is doing work nobody can read.** `FB_ValveLoop`'s four table-window loops (Aft 14, Er 14,
    Fwd 14, Cfg 16 = 58 slots) copy roughly **350 STRINGS every scan** — `TblTag`, `TblName`,
    `TblLoc`, `TblFunc`, `TblCmNo`, `TblVTag`, `TblStateTxt` per slot. String copies are the slowest
    operation in the whole program; estimated **1-2 ms of a ~2-4 ms scan**.

    The window contents only change when the page changes or a valve's state changes, and an
    operator cannot read faster than that. Gate the four loops behind a page-change flag plus a
    ~200 ms tick and roughly **half the scan time disappears** with no loss of responsiveness.
    Independent of everything else, low risk, high value.

27. **[pending — measure on real hardware]** **Confirm the audit's estimates against the actual CPU.**
    Everything in item 26 is calculated, not measured. Once on hardware:
    - TIA → *Online & diagnostics → Cycle time* for real scan time (estimate was 2-4 ms)
    - TIA → *Program info* for real work-memory usage against the CPU 1214C's **100 KB** budget
      (`Valve_Meta_DB` is string-heavy and is the block most likely to surprise)
    - Watch the **Home screen** first if anything feels sluggish — 89 mimic overlays plus three
      summary tables make it the heaviest page in the project.

28. **[pending — the one genuine unknown]** **Screen object count.** Home and the zone screens carry
    **230-250 screen objects each**. Siemens' published guidance for *classic* Comfort panels is a
    500 hard limit with 80-100 "practical"; Unified Comfort is stated as at minimum double, in places
    triple, but no per-screen object figure for Unified could be found. So this is above the
    conservative floor and cannot be resolved from a datasheet — **only real hardware settles it.**
    Mitigating factor: the bindings are native value maps, not scripts, which is roughly two orders
    of magnitude cheaper per binding.

29. **[settled 2026-08-18 — no action]** **Tag licensing is NOT a risk.** ~2,150 HMI tags, of which
    about five are internal, so ~**2,145 PowerTags** against a panel ceiling of **8,192** (74% free;
    still ~48% free even against the conservative 4,096 figure quoted for classic Comfort panels).
    **Proof is already in hand: exceeding the tag limit is a COMPILE-TIME error in TIA, and the HMI
    compiles with 0 errors.** The runtime licence is built into the panel hardware for Comfort and
    Unified Comfort — nothing to buy, nothing to ask the supplier. Recorded so nobody re-opens it.

30. **[verified good 2026-08-18 — do not regress]** **The HMI is already correctly optimised, and it
    must stay that way.** The audit found:
    - **All 27 trigger declarations are `AutomaticTags`. Zero polling anywhere in the project.**
    - Only ~18 script dynamizations exist in the entire generator, and every one is on a
      **single-instance** element (the popup, KPI counters) — never per-valve.
    - The 28 command buttons per zone use *event* scripts, which run on tap and cost nothing at rest.
    - Per-valve colour comes from **native value maps**, evaluated by the runtime's binding engine
      rather than the JavaScript engine.

    This is the thing that would normally cripple a panel of this size, and it was already fixed (see
    items 15 and 20). **Any future change that introduces a per-valve script, or a `T500ms` trigger,
    is a regression** — the popup was measurably slow with only ~14 polled scripts before item 15.

31. **[pending — UNKNOWN CODE RUNNING EVERY SCAN, read before install]** **Three legacy blocks
    execute in `Main [OB1]` and nobody has read them.** Found 2026-08-18 by exporting the live OB1 to
    confirm scan order. Actual order is:

    ```
    1. FB_ValveControl      <- purpose unknown
    2. FB_PlantSimulation   <- purpose unknown
    3. FB_ValveLoop            (ours — all valve logic)
    4. FB_HMI_MIirror       <- purpose unknown  (note the typo in the block name)
    5. (empty network)
    6. FC_PhysicalIoCopy       (ours)
    7. FC_IoMapper             (ours)
    ```

    All three predate the current architecture and run **before** the main loop every scan. Specific
    risks, none confirmed:
    - `FB_PlantSimulation` — if it writes simulated values into `Valves_DB` it could fight real I/O.
      The current design already had to add a real-channel guard inside `FB_ValveLoop` (item 6) for
      exactly this class of problem.
    - `FB_HMI_MIirror` — likely superseded by the selected-valve mirror now inside `FB_ValveLoop`
      (item 15). If both write the same mirror members, last-writer-wins and OB1 order decides.
    - Their scan-time cost is **not** included in the item 26/27 estimates, because they were never
      read. If measured cycle time comes back well above the ~2-4 ms estimate, look here first.

    They have caused no observed symptom — all 22 fault tests passed — but "no symptom yet" is not
    "harmless" on a system going onto a ship. **Read all three, then either delete the calls or
    document why they stay.** Note the OB1 export/patch/re-import route works fine (item 7), so
    removing a call is straightforward if they turn out to be dead.

32. **[pending — HMI REBUILD, agreed 2026-08-18. Do this ONE SCREEN AT A TIME.]**
    **Screen structure changed: Bilge is its own section, not part of the vessel overview.**

    **The decision and why.** Bilge is a genuinely separate system from ballast, not a region of it.
    Drawing both on one vessel illustration crams it to the point of being unreadable. So the four
    graphical screens are now:

    | Screen | Illustration | Valves |
    |---|---|---|
    | Home | **full ballast only** | AFT 1-27 + FWD 55-89 (62) |
    | Ballast Aft | AFT zone | 1-27 |
    | Ballast Fwd | FWD zone | 55-89 |
    | Bilge | **its own drawing (new)** | 28-54 |

    **The parked Bilge grid on Home STAYS** (revised 2026-08-18, overriding the first draft of this
    item). Home keeps the 27 Bilge valves in a grid at the bottom, exactly as item 21 built them —
    they simply have no place on a ballast-only drawing. Rationale: Home is the whole-plant landing
    page, and **every valve should be reachable from it** even when the artwork does not depict it.
    So Home carries 62 ballast overlays positioned on the drawing PLUS 27 parked Bilge boxes below.
    The Bilge SCREEN additionally gets its own proper drawing with the same 27 valves placed on it.
    This does retire the code-drawn `BuildZoneMimic` fallback, since Bilge was its last user.

    **New artwork, all four imported into TIA on their respective screen tabs (2026-08-18 22:25-22:27):**
    ```
    C:\hmi_graphics\full.png       C:\hmi_graphics\Bilge.png
    C:\hmi_graphics\AFT zone.png   C:\hmi_graphics\FWD Zone.png
    ```
    **These are DIFFERENT FILENAMES from the previous round.** The code currently references
    `"Full"`, `"AFT zone1"` and `"FWD Zone1"` — all three must be repointed to `"full"`,
    `"AFT zone"`, `"FWD Zone"`, plus a new `"Bilge"`. Confirm the exact graphic names as imported in
    TIA before building; a wrong name gives a blank background with the overlays still floating on it.

    **Box sizes:** Home **20 x 20 px**, all other screens **30 x 30 px**. Measure the real positions
    with `DetectBoxes.exe` — do not eyeball them (see item 21).

    **Placement rule — this is new and important.** Only place a live overlay where the artwork has a
    **real CM number printed above the box**. Boxes with no number, or reading `CM00`, are
    **unconfirmed** — leave them bare. A wrong overlay is worse than a missing one: it would bind a
    real valve's live state to the wrong symbol on a ballast system. Consequence to expect: the
    overlay count per screen will be LESS than that zone's valve count, and those valves will be
    operable from the valve list but absent from the mimic until the client confirms numbering
    (item 3 / item 19).

    **Also in this pass — valve list buttons must disable on UNCONFIGURED**, the same way they already
    grey out when logged out (item 17). An unconfigured valve is skipped entirely by `FC_IoMapper`, so
    pressing OPEN on it does nothing at all — currently with no explanation. The popup already handles
    this (`!cfg` is in its lock condition, item 25); the zone list does not.

    **Build order — ONE SCREEN AT A TIME, user verifies each before the next.** Agreed explicitly
    because the last full rebuild was ~90 minutes and a mistake in the shared code would have been
    repeated across every screen before anyone saw it. Suggested order: **Bilge** (all new, nothing to
    regress) → **AFT** (its labels are already confirmed, so it is the best correctness check) →
    **FWD** → **Home**. Use `--only=<Screen>` each time; note `Screen_Popup` is always rebuilt too.

    **STATUS 2026-08-18/19 — all coordinate work DONE and verified; only the build remains.**

    | Screen | Artwork | Boxes | Overlays | No overlay |
    |---|---|---|---|---|
    | Home | `full.png` 1888x584, 20px | 64 | 62 ballast + 27 parked Bilge | 2 x `CM00` |
    | AFT | `AFT zone.png` 1888x500, 30px | 28 | 27 | 1 unlabelled `?` |
    | FWD | `FWD Zone.png` 1888x500, 30px | 36 | 35 | 1 x `CM00` (forepeak) |
    | Bilge | `Bilge.png` 1888x500, 30px | 31 | 27 | 4 x `CM00` |

    Every CM number was read off 2.3-6.0x crops and matched to a `DetectBoxes.exe` centre — not
    eyeballed. Verified programmatically: each table has the exact slot range, no duplicates, no
    gaps, and every Home coordinate still lands on a box in the current file.

    Done: all four tables written; graphic names repointed to `full` / `AFT zone` / `FWD Zone` /
    `Bilge`; valve-list buttons grey on UNCONFIGURED and LOCAL; the bowtie glyph removed from every
    overlay (the artwork draws its own symbol); `BuildZoneMimic` now unreachable.
    **Screen_Bilge is already built** — but with the binary from before the button lock and the
    symbol removal, so it needs the rebuild too.

    **Remaining: one run of `--only=Home,Aft,Fwd,Bilge`.**

    Two artwork findings resolved along the way, both worth not re-investigating:
    - A duplicate **CM48** existed on BOTH `full.png` (top-right, 1851,56) and `FWD Zone.png`
      (outside the hull, 1732,419). Neither was ever mapped — the arithmetic caught them, because
      62 and 35 valves respectively were already accounted for. Both have since been removed from
      the drawings by the user. Comments in the code record this so nobody re-opens it.
    - The FWD forepeak `CM00` at (1735,242) is a **real valve the client's schedule does not
      contain**. The forepeak run goes 11-056 (CM45), 11-057 (CM46), 11-058 (CM47) and stops, and
      11-059 is absent from the schedule — so 11-059 is the likely tag, inferred from the numbering
      gap, NOT read off the P&ID. If it is in scope the pool needs a 90th slot. Same family as the
      seven other drawn-but-unscheduled valves in item 3.

33. **[pending — DO THIS WITH OR BEFORE ITEM 24]** **The direction/limit fault has no alarm.**
    Verified 2026-08-19: `CreateAlarms` generates **7** alarms per valve (Unhealthy, DoubleInd,
    FailOpen, FailClose, LossPos, UnexpMove, Local) — 632 = 89 x 7 + 9. `W_DirFault` is packed by
    `FB_ValveLoop` every scan but is referenced **zero** times in the alarm generator, so a direction
    fault latches, drives StateCode to FAULT, colours the mimic and the popup — and produces nothing
    in the alarm list. No timestamp, no acknowledgement, no history.

    **Why the ordering matters:** once item 24 makes the box FLASH, a direction fault becomes the
    loudest thing on the screen while still having no alarm behind it. Flashing implies "go read the
    alarm list", and there would be nothing there. That is worse than the current quiet failure, so
    the alarm must land with or before the flash — not after.

    Fix is one line beside the other seven in `CreateAlarms`, bound to
    `Valves_DB_W_DirFault_<w>` bit `(i-1)%16`, class `ValveWarning` (same tier as the travel
    timeouts), text along the lines of "<CM> Direction / limit fault - check actuator and limit
    switch wiring". It names both candidate causes, which is the point: the PLC cannot tell a
    wrong-direction actuator from a stuck limit switch, and the technician can.

    Cost: it rides the `--only=DiscreteAlarms` pass, which is slow (~45 min) but has to run anyway if
    anything else about the alarm set changes. Do it in the same session as 24 and 26.

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

34. **[pending - COMMISSIONING ON REAL HARDWARE. Storage medium for logs.]**
    **Logging works, but where it writes differs between simulation and the real panel - and only
    the panel setting is part of the delivered system.**

    How it stands today:
    - Project: `AlarmLog_Main.Settings.StorageDevice = USBX61` and
      `Audit trail_1.Settings.StorageDevice = USBX61`.
    - "Main database location for alarm logging" set to USB-X61 by hand in TIA (UI-only, NOT
      reachable through Openness. Creating a log before this is set breaks the HMI compile).
    - **Simulation ignores all of the above.** Per Siemens V20 docs, simulation uses the path from
      the "WinCC Unified Configuration" tool -> Archive settings, which on this laptop is
      C:\UnifiedArchive. Confirmed working 2026-08-21: alarm history populated and
      HMI_RT_1-SIM_AlarmLoggingDatabase.db3 grew on disk as alarms were raised.
    - The laptop path is a simulation artefact only. It does not transfer and needs no cleanup.

    On the real MTP1500, the project settings ARE what govern. To do at commissioning:

    a. **Decide the medium, and prefer SD-X51 over USB.** Siemens documents the SD card as the
       recommended medium for frequent read/write, and alarm logging writes constantly. A USB stick
       protruding from a panel on a ship is also physically exposed - easy to knock out, easy to
       walk off with. If switching, change BOTH the project objects (MakeAlarmLog.exe sets them,
       currently hardcoded to "USBX61" - change that string) AND the main database location in the
       TIA UI. They must match or the HMI compile fails with
       "Database of the log must be on the same medium as the main database for alarm logging."
    b. **Fit the card before first runtime start.** Siemens recommends a SIMATIC SD card of at
       least 32 GB for panel data memory, particularly for data consistency on power-off.
    c. **Verify on the panel**: browse to media/simatic/X51 (or the USB equivalent) in the panel's
       file explorer to confirm the medium is detected, then raise and clear a valve alarm and
       confirm ALARM HISTORY populates and the database file appears.
    d. **Expect the "Storage medium not available" system alarm to disappear** on the real panel.
       In simulation it persists and is CORRECT - the laptop genuinely has no USB-X61 port. Do not
       chase it in simulation; do not judge logging by it.
    e. **Sizing**: LogMaxSize is 20000 and the segment period is 30 days. Worth a sanity check
       against how many alarms 89 valves actually generate in service before handover.

35. **[pending — BUILD THIS BEFORE THE PANEL SHIPS. Does NOT depend on the client.]**
    **Per-valve travel and seat-break times, held as data instead of code.**

    Item 13 describes the defect and names the fix. This is the build task, split out because the
    two are separate jobs and only one of them is blocked on anybody else:

    - **Job A — get the numbers.** Section 2 already asks the client for full travel time and
      seat-break time per valve. Still outstanding. *Blocked on the client.*
    - **Job B — build somewhere to put them.** Not started. *Blocked on nobody.*

    **The trap:** these look like one job and are not. If the client emails all 89 travel times
    tomorrow, they still cannot be used — the times are compiled into the SCL and there is no
    mechanism that accepts them. Job B has to happen regardless of whether Job A ever completes,
    and because it is a code change it cannot wait for the ship.

    Confirmed still hardcoded 2026-08-27, exactly as item 13 describes:

    | Value | Where | Drives |
    |---|---|---|
    | `PT := T#8S` | `FB_ValveLoop`, TimerOpen | Fail-to-Open, and halts the valve |
    | `PT := T#8S` | `FB_ValveLoop`, TimerClose | Fail-to-Close, and halts the valve |
    | `PT := T#5S` | `FB_ValveLoop`, `DirTmr[#i]` | Direction/limit fault, and halts the valve |

    **Design:**

    a. **Two new `Array[1..89] of Time` members: `TravelTimeout[]` and `SeatBreakGrace[]`.**
       Put them in `Valve_Channels_DB`. That DB is already the per-valve configuration table, it
       already carries 535 compiled start values, and it is already the thing `FB_ValveLoop` reads
       per valve. **Do not put them in `Valves_DB`** — that DB has zero start values, so anything
       living there comes up as `T#0S` after a power cycle, and a zero PT means the timer expires
       instantly. That would be worse than today.
    b. **Compiled start values for all 89: `T#60S` travel, `T#10S` seat-break.** Generous on
       purpose. A timeout that is too long raises a late alarm; one that is too short stops a
       working valve. Only one of those strands a ballast valve half-open.
    c. **Substitute the three PT arguments** with the array reads. SCL takes a variable PT on a
       TON directly, so this is three lines.
    d. **Guard against zero.** If either value reads `T#0S`, fall back to the default in code
       rather than arming a timer that fires on the first scan. Cheap insurance against a bad
       import or a mis-typed panel entry.
    e. **A way to edit them at the panel**, on the Config screen, per valve. This needs editable
       numeric input fields — the buffer-tag pattern from the removed Name/Location fields is the
       method, and it is written up in the project memory. Authorization `Operate` at minimum;
       consider restricting to a commissioning role.
    f. **Mark both arrays `Retain`** — settled 2026-08-27 with item 36, which took the same decision
       for `Valve[i].Configured` and explains the reasoning. A time typed at the panel is exactly the
       kind of data that is meaningless if it silently reverts: `Valve_Channels_DB` is `NonRetain`
       today, so without this an afternoon of stopwatch work disappears at the next blackout with no
       message. Keep the generous start values from (b) as the floor for a memory reset or CPU swap —
       a valve falling back to a 60 s timeout is safe, one falling back to `T#0S` is not.

    **Verification:** set one valve to `T#3S` travel, command it, confirm Fail-to-Open raises at 3 s
    and not 8. Set another to `T#90S` and confirm it does not trip early. Then confirm both survive
    (or do not survive, per (f)) a CPU stop/start.

    **Once this is built, item 13 stops being a blocker** and becomes a commissioning task: an
    engineer times each valve with a stopwatch and types the numbers in. That is true even if the
    client never answers Job A, which is the whole point of doing it this way.

36. **[pending — BLOCKER for install. Found and decided 2026-08-27.]**
    **Every valve comes up UNCONFIGURED after a power cycle, so nothing responds until somebody
    presses CONFIGURE ALL.**

    Found while cross-checking an external audit. The audit reported this as "all runtime
    configuration, names and CM tags are lost on every power cycle" — **that part is wrong**, and the
    distinction matters because it changes the fix from days of work to minutes:

    - `Valve_Meta_DB` holds **443 compiled start values** — every name, location and CM number.
    - `Valve_Channels_DB` holds **535** — all 534 channel assignments.
    - `NonRetain` means "revert to the start value", and here the start value *is* the correct
      data. **All of that survives a blackout intact.** Nothing is lost.

    What actually resets is one flag. `Valves_DB` has **zero** start values, so
    `Valve[i].Configured` comes back FALSE for all 89. `FC_IoMapper` skips unconfigured valves and
    `FB_ValveLoop` will not accept a command for one, so after any 24 V blip the panel shows 89
    valves reading UNCONF and **no valve on the ship answers the panel** until someone notices and
    presses CONFIGURE ALL.

    There is no auto-configure and no first-scan init — verified, `Configured` is only ever read in
    `FB_ValveLoop`, never written. The HMI is the only thing that writes it.

    **Fix, decided 2026-08-27 — three parts, all of them needed:**

    a. **Mark `Valve[i].Configured` as `Retain`.** This is the actual fix. Retentive memory survives
       a power cut, so the plant comes back exactly as it was left — including valves deliberately
       taken out of service. The 1214C has ~10 KB of retentive memory and 89 Bools is 12 bytes, so
       capacity is not a consideration.

    b. **Leave the start value FALSE.** Retain is wiped by a memory reset, a `Valves_DB` structure
       download, or a CPU swap, and the start value is what the CPU lands on afterwards. TRUE was
       considered and rejected: **all three of those situations have an engineer standing at the
       cabinet**, so "the plant re-enables itself" buys one saved button press, while FALSE holds the
       line that 89 valves do not arm themselves because somebody swapped a CPU. Retain already
       covers the blackout, which is the case that happens with nobody there.

    c. **Make "nothing is configured" visible on the panel.** (b)'s failure mode is silent — after an
       MRES the screen looks entirely normal: no alarm, no message, just 89 grey boxes. Anyone who
       does not already know the system reads that as a broken panel. Add a banner on Home, shown
       when `Valves_DB.TotalConfigured = 0`:

       > **NO VALVES CONFIGURED — system not commissioned. Go to CONFIG.**

       `TotalConfigured` is already computed every scan by `FB_ValveLoop` and already has an HMI tag,
       so this is a rectangle and a value-mapped colour on Home — the same pattern as the
       station-offline banner on the zone screens. No PLC change.

    **Do not skip (c).** Parts (a) and (b) are the engineering decision; (c) is what stops that
    decision becoming a trap for whoever is on board at the time.

    **Item 35(f) must match this.** The per-valve travel times are the same class of data — set at
    the panel, meaningless if lost — so they get `Retain` for the same reason. Note the interaction:
    while the program is still changing during commissioning, every structure download wipes retain
    and both the flags and the tuned times reset. That is expected, it settles once downloads stop,
    and anyone tuning valves needs to be told so they do not lose an afternoon's stopwatch work
    without understanding why.

    **Verification:** configure some valves, disable one deliberately, power-cycle the CPU, and
    confirm the exact pattern comes back — including the disabled one still disabled. Then memory-
    reset the CPU and confirm all 89 read UNCONF *and the Home banner appears*.

37. **[pending — six of the nine system alarms can never fire. Found 2026-08-27.]**
    **`HwWord` has 9 alarms defined and 3 driven. The other six are wired to bits nothing writes.**

    `FB_ValveLoop` packs `HwHealthy[1..9]` into `HwWord` bits 0..8 (~line 743-750) and the HMI binds
    one system alarm to each bit (`GenerateHmiLayout.cs` ~1769-1777). Only `HwHealthy[2..4]` are ever
    written — by the OB86-derived station logic at ~line 197-199. Nothing in the project writes the
    other six, so those alarms are permanently unilluminable:

    | Bit | `HwHealthy[]` | Alarm | Driven by |
    |---|---|---|---|
    | 0 | [1] | PLC CPU fault | **nothing** |
    | 1 | [2] | Aft RIO station failure | OB86 ✅ |
    | 2 | [3] | Bilge/ER RIO station failure | OB86 ✅ |
    | 3 | [4] | Fwd RIO station failure | OB86 ✅ |
    | 4 | [5] | I/O module fault | **nothing** |
    | 5 | [6] | Power / UPS fault | **nothing** |
    | 6 | [7] | Network loss | **nothing** |
    | 7 | [8] | HMI heartbeat loss | **nothing** |
    | 8 | [9] | System general fault | **nothing** |

    **Why this is worse than having no alarm.** A missing alarm is an absence; a dead alarm is a
    false statement. An operator who sees Power/UPS Fault sitting inactive concludes the supply is
    healthy. The panel claims to be watching six things it is not watching.

    ---

    **⚠ READ THIS BEFORE DRIVING ANY OF THEM — the startup init will silently wipe the lot.**

    `FB_ValveLoop` ~line 178-183:

    ```
    IF NOT "Valves_DB".HwHealthy[1] AND NOT "Valves_DB".HwHealthy[8] THEN
        FOR #b := 1 TO 9 DO "Valves_DB".HwHealthy[#b] := TRUE; END_FOR;
    END_IF;
    ```

    It uses `HwHealthy[1]` and `[8]` as a sentinel for "this is a fresh start, nothing has been set
    yet". Those are **bit 0 (CPU fault) and bit 7 (HMI heartbeat)** — precisely two of the six this
    item is about. Drive them and the sentinel becomes reachable in normal operation: the moment a
    CPU fault and a heartbeat loss are both active, all nine bits reset to healthy, taking the three
    *working* station alarms with them. And it is not an unlikely pair — a panel powered down
    overnight holds heartbeat-lost on its own, so it only takes one CPU diagnostic alongside it.

    The station bits already dodge this by being re-derived from `Diag_DB` every scan (~line 190-194
    explains why). That workaround does not extend to [1] and [8], because they *are* the sentinel.

    **Prerequisite fix — do this first, on its own:** replace the sentinel with an explicit one-shot.
    Add `HwInitDone` (Bool) to `Valves_DB`, leave it `NonRetain` with no start value so it is FALSE
    on every CPU start, and gate the init on it:

    ```
    IF NOT "Valves_DB".HwInitDone THEN
        FOR #b := 1 TO 9 DO "Valves_DB".HwHealthy[#b] := TRUE; END_FOR;
        "Valves_DB".HwInitDone := TRUE;
    END_IF;
    ```

    Runs exactly once per start, means what it says, and cannot be re-entered by live fault data.
    Keep it `NonRetain` even after item 36 makes `Configured` retentive — this one *should* reset on
    every restart, that is its whole job.

    ---

    **Then, per alarm — drive it or delete it. Defined-but-dead is the one option that is not honest.**

    a. **Bit 7, HMI heartbeat — BUILD. Highest value of the six.** Today the PLC has no idea whether
       the panel is alive: if the MTP1500 dies, the PLC holds every valve output where it was and
       nothing annunciates. The crew's only window onto 89 ballast valves goes dark in silence.
       Design: HMI increments a counter tag; `FB_ValveLoop` keeps `PrevHeartbeat` and a TON, and
       clears `HwHealthy[8]` if the value has not changed for ~5 s.
       **Open question that must be settled first:** what runs the HMI-side write. A screen script
       only executes while that screen is displayed — the localhost beep at `GenerateHmiLayout.cs:774`
       is the cautionary example, an annunciator that only sounds when you are already looking at the
       alarm list. A heartbeat with that flaw is worse than none, because it would report
       the panel dead whenever an operator opened a popup. Verify whether WinCC Unified Scheduled
       Tasks are reachable through Openness and run independently of the displayed screen **before**
       designing the PLC side around it.

    b. **Bit 4, I/O module fault — BUILD, via OB82.** OB86 catches a whole station dropping off
       PROFINET. OB82 (diagnostic error) catches a single module failing *inside* a station that is
       otherwise healthy — a DI card dies, its 16 channels go dead, the station keeps talking. Today
       that produces four valves reporting Unhealthy and Loss-of-Position with nothing saying why:
       the operator sees four broken valves instead of one broken card.
       Same route as OB86: Openness cannot create OBs, so create it in the TIA UI and import the
       body. Check the S7-1200 OB82 interface before writing SCL rather than assuming — that was the
       lesson from OB86.
       **Prerequisite:** module diagnostics are currently OFF (`DiagnosticsWireBreak = False`,
       `DiagnosticsNoSupplyVoltage = False` — item 14). OB82 fires on diagnostics the module reports,
       so with all of them disabled it may never fire at all. Enabling them is part of this job, not
       separate from it.

    c. **Bit 5, Power/UPS — BLOCKED ON THE CLIENT.** Needs a volt-free contact from a real UPS wired
       to a spare DI. Two things to establish: **is there a UPS at all** (nothing in the project says
       so), and if yes, does it have an alarm contact. Practical note — there are free DI channels for
       it: 109-112 on AFT, 221-224 on MID, 365-368 on FWD (item 35's channel audit). If the answer is
       "no UPS", delete the alarm.

    d. **Bits 0, 6, 8 — DELETE.** PLC CPU fault, Network loss, System general fault. Nothing credible
       will ever drive them and each is already covered: a CPU that faults is not running this code
       at all; network loss to a station is bits 1-3 and network loss to the panel is bit 7; and
       "general fault" adds nothing over the Home screen's active-alarm count, which is already live.
       Deleting three drops the alarm total from 632 to 629.
       Note bit 0 is `HwHealthy[1]` — deleting the alarm does not by itself remove the sentinel
       problem above. Do the prerequisite fix regardless.

    **Verification:** force each surviving bit in `Valves_DB.HwHealthy[]` from the watch table and
    confirm the matching alarm raises and clears on the ALARMS screen. Then force `HwHealthy[1]` and
    `[8]` FALSE together and confirm the other seven **stay** where they were — that is the regression
    test for the sentinel fix, and it fails today.

38. **[pending — THERE IS NO AUDIBLE ALARM. Decided 2026-08-27: use the panel buzzer.]**
    **The "beep" is an HTTP call to a service that does not exist, it fails silently, and it only
    runs while the ALARMS screen is displayed.**

    `GenerateHmiLayout.cs:774`, inside the `TotalFaults` label's script dynamization on
    `Screen_Alarms`:

    ```js
    if (!globalThis._beepTimer) globalThis._beepTimer =
      setInterval(function() { try { fetch('http://127.0.0.1:8081/beep/'); } catch(e){} }, 1500);
    ```

    Three separate faults, any one of which is enough to make it useless:

    - **Nothing serves 127.0.0.1:8081.** That address means "this machine" — on the panel, the panel
      itself. Nothing in this project installs or starts anything on that port. It was written
      against something on a development laptop and cannot work on the delivered system.
    - **`catch(e){}` swallows the failure.** It fails every 1.5 s forever with no error, no log and
      no screen indication, which is precisely why it has survived this long looking like working code.
    - **It only runs while `Screen_Alarms` is displayed.** Screen scripts do not execute when their
      screen is not shown. So the annunciator only attempts to sound when the operator is already
      looking at the alarm list — backwards, since the entire job of an audible alarm is to fetch
      somebody who is *not* looking.

    Net effect: **a ballast, bilge and fire valve system with no audible annunciation whatsoever.**
    Beyond the operational problem, expect a surveyor to ask how alarms are annunciated.

    **Decision (user, 2026-08-27): use the MTP1500's built-in buzzer.** Confirmed by the user that
    the panel has one. A hardwired sounder on a spare DQ was the alternative and is not being taken;
    if a surveyor later requires annunciation independent of the HMI, revisit — there are 10 free DQ
    channels per station (item 35's channel audit) so the option stays open at no cost today.

    **Step 1 — recover the API answer that was already paid for.** `InspectAcousticSignal.exe` exists
    in the repo, built 2026-08-01, written to search the Siemens DLLs for acoustic-signal types and
    read a live `SetAcousticSignal` handler. **It was run and the output was never recorded** — there
    is no log and no note of it anywhere. Re-run it and commit the output this time.

    What that determines: whether the buzzer is reachable through Openness (so `HmiBuilder` sets it,
    like the alarm-class colours) or is **UI-only** and needs a manual TIA step documented in the
    panel setup. Do not assume the former — the "main database location for alarm logging" turned out
    to be UI-only and unreachable through Openness (item 34), and creating a log before it was set by
    hand broke the HMI compile outright. Same class of trap.

    Likeliest place to look: `HmiAlarmClass`. Commit `55f7cd6` established it exposes `RaisedState` /
    `AcknowledgedState` / `ClearedState` / `AcknowledgedClearedState`, each with writable `BackColor`,
    `TextColor` and `Flashing`, and that **class-level changes DO flow through to existing alarm
    instances** — so if the acoustic setting lives there too, all 632 alarms can be given sound
    without an alarm regeneration.

    **Step 2 — decide which classes sound.** Mirror the existing colour/flash tiering, which was
    already reasoned out against ISA-18.2 / EEMUA 191 and should not be re-litigated:

    | Class | Priority | Sound? |
    |---|---|---|
    | `ValveFault` | 14 | **yes** |
    | `System` | 12 | **yes** |
    | `ValveWarning` | 8 | probably — decide with the client |
    | `ValveEvent` | 3 | **no** — Local mode is information, and it already never flashes |

    **Step 3 — the buzzer must stop on ACKNOWLEDGE, not on clear.** An operator silences by
    acknowledging; the alarm stays visible until the condition actually goes away. Silencing on clear
    means the noise continues until the valve is fixed, which trains people to ignore it. Verify the
    acoustic setting is tied to the unacknowledged state — the same `RaisedState` /
    `AcknowledgedState` split the flashing already uses.

    **Step 4 — remove the localhost beep.** Note it is *inside* the script that also renders
    "ACTIVE FAULTS: n", so the dynamization is edited, not deleted. `Internal_PrevFaultCount` exists
    only to give that script its 0→n edge and becomes unused once the beep goes — check for other
    readers before removing the tag.

    **Build cost:** this touches one script on `Screen_Alarms`, and `--only=Alarms` is the ~45-minute
    rebuild that also regenerates all 632 alarms and carries the COM deadlock risk documented in
    `BuildAlarmScreen`. Do it as a targeted patch instead — `PatchNav` and `PatchAlarmColumns` are the
    existing pattern for reaching into one screen and rewriting one thing in place.

    **Verification:** raise a valve fault with the panel showing **Home**, not the alarm screen, and
    confirm the buzzer sounds. That is the exact case today's code fails. Then acknowledge without
    fixing the valve and confirm the buzzer stops while the alarm stays active.

39. **[pending — ALARM FLOOD. One 24 V field-supply failure raises 81 alarms and none of them says
    why. Found 2026-08-27.]**

    **The failure.** The 24 V supply feeding the field wiring on one station fails — fuse, loose
    terminal, dead PSU — but **the ET200SP itself stays powered and stays on PROFINET**. Every input
    on that station reads 0, and the PLC has no reason to disbelieve it. Per valve, three separate
    alarms fire (all three verified 2026-08-27, none suppressed by any of the others):

    | Alarm | Why it fires | Where |
    |---|---|---|
    | Unhealthy | `Healthy` input reads 0 | packed from `Valve[i].Healthy` |
    | Loss of Position | both limits 0, no run latch, 2 s debounce | `PosLostTmr`, ~line 413 |
    | Unexpected Movement | a made limit switch dropped with no command | ~line 431-437, **and it latches** |

    27 valves x 3 = **81 alarms in about two seconds**. If the failure is upstream of all three
    stations, **267**. ISA-18.2 puts the flood threshold at 10 alarms per 10 minutes per operator.

    **And nothing states the cause.** `System_Power_UPS_Fault` is one of the six bits nothing drives
    (item 37). So the operator gets 81 rows saying valves are broken, zero rows saying the power
    failed, and the reasonable conclusion available to them is that 27 valves failed simultaneously.

    **Why the station-loss case does not have this problem.** `FC_IoMapper`'s `#stationDown` guard
    freezes the last known feedbacks instead of copying zeros, so a station dropping off PROFINET
    raises exactly one alarm and leaves last-known positions on screen. That is good design and it is
    already there. It does not help here for one reason: **OB86 tells the PLC the station is gone.
    Nothing tells it the power died.** Identical symptom at the valves, opposite handling, purely
    because of what the PLC was told. The two cases cannot collide — a frozen station keeps its last
    `Healthy` value, so it never floods.

    **Fix — detect the pattern and raise one alarm instead of many.**

    a. **Count unhealthy configured valves per station**, in `FB_ValveLoop`. The 1..89 loop already
       does per-zone counting (~line 441-455) and the increments are already free, so this is a
       counter alongside the existing ones.
    b. **Threshold, and why not "all".** ET200SP load groups mean a single fuse may kill only part of
       a station — how much depends on the BaseUnit mix, which is not finalised (item 14 / the
       BaseUnit gap). "All 27 unhealthy" would miss a partial-group failure entirely and leave 8-12
       alarms flooding anyway. Use **N or more configured valves on one station unhealthy at once**,
       with N held in a DB member so it is tunable at commissioning rather than compiled in — same
       lesson as item 35. Start at 5. Debounce ~2 s so a transient cannot trip it.
       Accepted trade-off: five genuinely-failed valves on one station would be reported as a group
       fault. Unlikely, and far better than the current behaviour.
    c. **Suppress the per-valve alarms for that station while the group alarm is active** — gate the
       `W_Unhealthy`, `W_LossPos` and `W_UnexpMove` packing for those slots. Suppress the *alarms*
       only: the valves must still show unhealthy on the zone screens and in the summary counts. The
       information stays, the 81 notifications do not.
    d. **Do not let `UnexpMove` latch during the event.** It is latched (~line 435) and nothing else
       here is. Suppressing only the alarm bit while still setting the latch means all 27 latched
       faults appear the moment power returns — the flood arrives late instead of not at all. The
       latch itself has to be inhibited while the group fault is active.
    e. **Three new alarms, one per station** — "AFT/BILGE/FWD station field supply failure".
       **Tidy option: reuse the bits item 37 deletes.** That item removes bits 0, 6 and 8 (CPU fault,
       Network loss, General fault) as undriveable, freeing exactly three. Reusing them means no new
       tag, no change to `HwWord`, and no alarm-count change. It does make 37 and 39 one piece of
       work rather than two — decide that before starting either.

    **Verification:** force `Healthy` FALSE on all 27 AFT valves from the watch table. Confirm **one**
    alarm, not 81; confirm the zone screen still shows those valves unhealthy; then restore and
    confirm **no latched Unexpected Movement flood arrives on recovery** — (d) is the part most likely
    to be got wrong, and it fails silently in testing unless you look for it specifically.

40. **[pending — ORDER SPARE I/O MODULES BEFORE THE PANEL IS BUILT. Cheap now, a mobilisation
    later. Verified 2026-08-27.]**

    **Three spare valve slots on the entire vessel — 3.4%.**

    Every channel in `Valve_Channels_DB` was parsed and checked against the configured module
    addresses in `inspect_hw.log` / `inspect_addresses.log`:

    | Station | DI ch. | DI used | DI free | DQ ch. | DQ used | DQ free | Spare valves |
    |---|---|---|---|---|---|---|---|
    | AFT | 112 (7 x DI16) | 108 | **4** | 64 (4 x DQ16) | 54 | 10 | **1** |
    | MID | 112 (7 x DI16) | 108 | **4** | 64 (4 x DQ16) | 54 | 10 | **1** |
    | FWD | 144 (9 x DI16) | 140 | **4** | 80 (5 x DQ16) | 70 | 10 | **1** |

    Free DI channels are exactly `109-112`, `221-224`, `365-368`. **DI is the binding constraint** —
    outputs have room for 5 more valves per station, inputs for 1, and a valve needs 4 in and 2 out.
    A marine retrofit would normally carry 10-20% spare.

    Also verified clean while counting, so the numbers above can be trusted: 534 assignments, **no
    duplicates, no zero/unassigned entries, none out of range, and none assigned across a station
    boundary** — every valve's channels sit on the station its slot index implies, which is what
    `FC_IoMapper`'s index-range `#stationDown` guard depends on.

    **Why this becomes urgent.** Item 3 records seven valves drawn on the client's P&ID that appear
    in no schedule row (`11-007/300`, `11-013/300`, `11-017/125`, `11-018/125`, `11-019/125`,
    `11-070/300`, `11-090/200`). If the client confirms any of them are in scope, they need 28 DI and
    14 DQ. **There are 12 DI free.** They do not fit.

    **The cost is not the module — it is re-addressing.** `FC_PhysicalIoCopy` holds 368 hardcoded DI
    and 208 DQ assignments (all verified arithmetically perfect: `DI[n]` -> `%I(2 + (n-1)/8).((n-1)
    mod 8)`, spanning exactly `%I2.0-%I47.7` and `%Q2.0-%Q27.7`, no gaps or duplicates). Insert a
    module **mid-station** and every address behind it shifts, invalidating those lines and
    potentially the whole 534-entry channel map — discovered after the panel is built and the cables
    are terminated.

    **Fix — add spare modules now, appended at the END of each station.** "End" is the load-bearing
    word: appending after the last module gives the new one fresh addresses above everything existing
    and **nothing already assigned moves**. Anywhere else and the addresses behind it shift. Empty
    channels cost nothing to own.

    Suggest one DI16 per station minimum (takes each to 5 spare valves, ~18%), and one DQ16 on AFT
    and MID if the same 18% is wanted on outputs — FWD's DQ already has the headroom.

    **Each added module also needs a BaseUnit.** Flagging because BaseUnits appear nowhere in any
    parts list in this repo, along with the 3 bus adapters (`6ES7 193-6AR00-0AA0`) and 3 server
    modules (`6ES7 193-6PA00-0AA0`) that **are** configured in the project and must therefore be
    bought. Light vs dark BaseUnits also decide where a new potential group starts, which matters for
    item 39's load-group reasoning.

    **Caveat on the BOM, stated plainly:** an external audit reported order-number and firmware
    mismatches against "what you are buying", including a SIPLUS `6AG1214-1AG40-4XB0` CPU and a
    `6EP1334-2BA20` PSU. **No parts list exists anywhere in this repository** — those part numbers
    appear in no file, including inside the `.xlsx` files. Either the BOM was supplied to that agent
    outside the repo, or the column was fabricated. **Get the real BOM into the repo** so this is
    checkable; until then treat any BOM-vs-project comparison as unverified. The firmware readings
    themselves are solid, being read from `inspect_hw.log`: CPU configured at **V4.0**, the three IMs
    at **V6.2**, HMI at **20.0.0.0**.

    **Verification after adding:** re-run `InspectAddresses` and confirm every existing `%I`/`%Q`
    address is **unchanged** and the new module's range sits entirely above them. If any existing
    address moved, the module went in at the wrong position — stop and reposition before touching
    `FC_PhysicalIoCopy`.

41. **[pending — SECURITY. Both parts are pre-ship: one is a hardware-config change, the other
    cannot be fixed from the panel afterwards. Verified 2026-08-27.]**

    **a. The PLC has no password, and direct writes bypass the audit trail.**

    `compile_plc_physio.log` (2026-08-08, the most recent PLC compile on record):

    > `[Warning] PLC_1 does not contain a configured protection level`

    Independently confirmed by the user's own screenshot of Protection & Security showing **Full
    access (no protection)**, no password. Anyone who can reach the CPU on the ship's network, with
    TIA Portal on a laptop, can stop it, upload the whole program, or **write directly into
    `Valves_DB` — which operates ballast valves with no HMI, no login and no button press.**

    **The part that matters most: this bypasses the audit trail entirely.** The audit trail records
    actions taken *at the panel*. A direct PLC write never touches the panel, so the one record of
    who moved what has a hole in it that leaves no trace. That is a surveyor question as much as a
    security one.

    Realistic threat model is not hackers — it is a service engineer "just checking" something, a
    crew laptop on the same network, or somebody tracing an unrelated fault who opens the wrong block.

    **Fix:** set a protection level and password in the CPU's Protection & Security settings.
    Read-only for general access, password-protected for write. Hardware configuration, so it is a
    download — **must be done before the panel ships.** Record the password somewhere the vessel's
    engineers will still have it in five years; a PLC nobody can get into is its own failure mode.

    **b. No panel account can manage accounts — so logins are frozen at handover.**

    Present in every HMI compile log from at least 2026-08-13 through 2026-08-15:

    > `[Warning] The configuration is missing a user with the function right "User management".
    > User data cannot be changed in runtime`

    Nobody on the panel holds the right to administer users. Once the panel is aboard: **no user can
    be added when an engineer joins, none removed when somebody leaves, and no password changed —
    ever.** The only route is TIA Portal and a download, which is precisely what the
    no-changes-at-the-ship constraint rules out.

    **This undermines the audit trail that was built for exactly this purpose** (see
    `AUDIT_DEPLOYMENT.md`). With no way to issue accounts, everyone shares one login, so the trail
    records "Operator opened CM51" with no way to say which person that was — and a shared password
    that can never be changed also can never be revoked when someone leaves the vessel. An audit
    trail that cannot attribute an action to a person is far weaker as evidence, and attribution is
    the first thing a surveyor tests.

    **Fix:** grant the "User management" function right to at least one role before shipping. One
    setting in HMI user administration. Decide *which* role deliberately — Master or a dedicated
    admin, not Operator.

    **c. Runtime language mismatch — ten minutes, possibly related to a live problem.**

    > `[Warning] The configured runtime language for the HMI device does not match the language
    > configuration of connected PLC "PLC_1"`

    Low severity alone. Worth doing now because `##Text missing##` on the panel is still unexplained
    (the PLC read-access theory was killed by the user's screenshot showing Full access), and an
    HMI/PLC language mismatch is a plausible cause of text failing to resolve at runtime. Cheap to
    align and it removes one candidate.

    **Verification:** after (a), confirm an online connection without the password is refused write
    access and that the HMI connection still works — the HMI uses its own connection and must not be
    locked out by the new protection level. After (b), log in at the panel and confirm a user can
    actually be added and a password changed **in runtime**, not just that the warning disappeared.

On the new laptop, once the repo is cloned and TIA has opened the `.ap20` once (to rebuild its cache
folders): open Claude Code in that folder and just say what you want to do next. Point it at this file
first if it hasn't already read it. The immediate next actions in priority order are items 4 and 1 in
section 3 (re-import the CM89 fix, then the discrete alarms pass) — both are mechanical, no open
decisions needed. Item 5 (channel mapping) is the next real engineering task after that.
