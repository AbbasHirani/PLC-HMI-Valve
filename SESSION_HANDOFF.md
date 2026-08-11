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

- **96 valve slots** in the PLC (`Array[1..96]` throughout `Valves_DB`, `Valve_Meta_DB`,
  `FB_ValveLoop`'s SimStep array). Deliberately sized above the real valve count so future additions
  don't force another full resize (this has already happened twice this session as the count moved).
- **Zones**: AFT = slots 1-28, ER/Bilge = slots 29-56, FWD = slots 57-96. AFT and ER hold their real
  valves exactly (27 each); FWD has 40 slots for its 34-35 real valves.
- **The confirmed real valve count is 88** (client explicitly confirmed via WhatsApp). This came from
  a messy multi-round investigation — see section 4 below, it's worth reading before touching valve
  counts again.
- **Full HMI rebuild done**: Home (vessel mimic), Screen_AftBallast, Screen_Bilge (labelled "BILGE AND
  FIRE" — this is correct, not a bug, see section 4), Screen_FwdBallast, Screen_Diagnostics
  (Configuration screen), Screen_Popup (valve control popup). All rebuilt at 96-slot capacity, all
  compiled clean, all saved.
- **Screen_Alarms was NOT rebuilt this session** — rebuilding it wipes the Alarm Control's `Filter`
  property, which cannot be set via Openness and must be re-entered by hand in TIA's UI afterward. Its
  text alignment is still using the old (buggy) settings — see section 5's alignment bug.
- **Client's valve schedule is loaded** into `Valve_Meta_DB` as DB start values (CmNo, VTag, SysName,
  Location, FuncName per valve) — verified field-by-field against their Excel
  (`Annex_B_IO_List_Full_Mapped.xlsx`), 89 rows matched with only one deliberate gap (CM89, see below).
  **This load happened on the OLD laptop and was verified in that TIA session, but the final CM89 fix
  (adding its SysName="Ballast Fwd") never got re-imported into the live `.ap20`** — the old laptop's
  STEP 7 Basic license expired mid-session before that last re-import could run. The *source* file
  (`temp_valve_meta_db.xml`, committed) already has the fix; it just needs one more
  import→compile→fix-tags→compile→save cycle once you have a working TIA session again.
- **Dynamic I/O Mapper scaffolding exists but is NOT wired in**: `UDT_Valve_Config`,
  `Valve_Channels_DB` (channel index per valve, currently all zero/unassigned), `IO_Buffer_DB` (raw
  channel scoreboard), `FC_IoMapper`, `FC_PhysicalIoCopy` (uses real hardware addresses, confirmed via
  the ET200SP-to-PLC network assignment fix earlier this session). None of these are called from
  `Main[OB1]` yet. Two things block wiring them in: (1) `FB_ValveLoop`'s own simulation still
  unconditionally overwrites `OpenFB`/`ClosedFB` every scan — needs a guard added so it defers to real
  data once a valve has a real channel assigned; (2) the actual channel numbers from the client's Excel
  (`DO_xxx_OPEN` etc.) still need translating into `Valve_Channels_DB` entries — this is the next real
  engineering task once alarms/testing catch up.

## 3. Full todo list (carried over verbatim)

1. **[pending]** Discrete alarms for slots 89-96 (56 alarms) — separate `--only=DiscreteAlarms` pass,
   slow (~30-60 min), not yet run.
2. **[pending]** User to visually test Home's 96-slot mimic, FWD zone (40 slots/3 pages), Config screen
   (6 pages) render correctly in TIA/runtime.
3. **[pending]** Client still owes clarification on 7 rows: CM80, CM85, CM86, CM87, CM88, CM90 (marked
   "Tag to verify" / "Duplicate tag on mimic; verify" in their sheet) and CM89 (needs Location/Function
   and an actual I/O assignment before it becomes a 89th real valve).
4. **[pending]** Re-import the CM89 SysName fix (source already correct, see section 2).
5. **[pending]** Populate `Valve_Channels_DB` from the client's `DO_xxx`/`DI_xxx` channel numbers —
   the actual Dynamic I/O Mapper wiring task, blocked on nothing except time.
6. **[pending]** Add the `FB_ValveLoop` simulation guard so real channel data isn't overwritten.
7. **[pending]** Wire `FC_PhysicalIoCopy` + `FC_IoMapper` into `Main[OB1]` — requires a manual TIA UI
   edit (OB1 is LAD, Openness has no granular network-edit API for LAD blocks — confirmed via
   reflection, only whole-block export/import exists).
8. **[pending]** Rebuild `Screen_Alarms` at some point for the 96-slot resize + alignment fix, and
   re-enter its Filter property by hand afterward.
9. **[pending]** Fail-safe design review (what should each fault condition actually *do* to a valve —
   hold/close/stop), PUT/GET security lockdown decision, PROFINET single-port topology question
   (largely resolved — bus adapters have 2 ports each, so daisy-chain works, but a switch is still
   arguably better for reliability on a vessel), FAT bench test before anything goes near the ship.
10. **[pending]** Get whoever's doing the physical wiring their `PLC Address`/`Terminal` columns filled
    in — the client's Excel left those blank, meaning translating logical channels to real
    `%I`/`%Q` addresses is explicitly our job, not theirs.

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

## 7. How to resume

On the new laptop, once the repo is cloned and TIA has opened the `.ap20` once (to rebuild its cache
folders): open Claude Code in that folder and just say what you want to do next. Point it at this file
first if it hasn't already read it. The immediate next actions in priority order are items 4 and 1 in
section 3 (re-import the CM89 fix, then the discrete alarms pass) — both are mechanical, no open
decisions needed. Item 5 (channel mapping) is the next real engineering task after that.
