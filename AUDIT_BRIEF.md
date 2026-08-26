# Independent audit brief — MV WESTERLY Valve Remote Control System

Paste everything below the line into the orchestrating agent.

---

You are the lead auditor for an independent, adversarial review of a marine
valve remote-control system that is approaching installation on a working ship.
**Spin up multiple specialist sub-agents**, one per workstream below, run them
in parallel, and consolidate their findings yourself.

Your job is **not** to be reassuring. The people who built this have already
convinced themselves it works. Assume something important is wrong and go and
find it. A review that returns "looks good" without naming what it verified,
how, and what it could not verify is a failed review.

## The system

| | |
|---|---|
| Vessel | MV WESTERLY, IMO 9834217 |
| Function | Remote open/close and monitoring of 89 ballast, bilge and fire valves |
| PLC | Siemens S7-1200, CPU 1214C DC/DC/DC (6ES7 214-1AG40-0XB0) |
| Remote I/O | 3 × ET200SP, IM 155-6AU02-0BN0 — AFT (7 DI + 4 DQ), MID (7 DI + 4 DQ), FWD (9 DI + 5 DQ) |
| HMI | SIMATIC MTP1500 Unified Comfort Panel (6AV2 128-3QB06-0AXx) |
| Software | TIA Portal V20, WinCC Unified |
| Built by | A C# Openness application (`HmiBuilder.exe`), not by hand in the TIA UI |

Roughly 2,150 HMI tags and 632 configured alarms. Valve feedbacks are open
limit switch, closed limit switch, healthy, and local/remote. Commands are
maintained (not pulsed) open and close.

## Repository

```
c:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\
```

Read these first, but **treat them as claims to be tested, not as truth**:

| File | What it claims to cover |
|---|---|
| `README.md` | Panel installation, storage, system diagnostics, station failure |
| `SESSION_HANDOFF.md` | Numbered todo list with statuses — known to be partly stale |
| `AUDIT_DEPLOYMENT.md` | Audit trail design and delivery steps |
| `HARDWARE_INSTALL_TROUBLESHOOTING.md` | On-site symptom→cause→fix sheet |
| `VALVE_SCADA_SYSTEM_TECHNICAL_DOCUMENTATION.md` | PLC and HMI architecture |
| `CLAUDE.md` | Original engineering specification |
| `src/*.cs` | The Openness builder — this is where the HMI actually comes from |
| `temp_*.xml` | Exported PLC blocks (SCL). `temp_fb_valveloop.xml` is the main control block |
| `AUDIT_TAG_MAP.csv` | Valve slot → CM number mapping |
| `MV_Westerly_IO_Wiring_Schedule.xlsx` | Client-facing wiring schedule |

## Ground rules

1. **Read-only.** Change nothing in the repository or in TIA Portal. If TIA is
   running, do not attach to it with Openness — a session on 2026-08-27 crashed
   TIA mid-operation and left a screen half-built.
2. **Verify, do not trust.** Documentation in this project has been wrong before
   and was corrected: claims that a setting "cannot be set", that the alarm log
   "cannot move to SD", and that PROFINET inputs "go to zero on station failure"
   were all asserted without sources. Where a document states a fact, find the
   primary source or mark it unverified.
3. **Cite everything.** Every finding needs a file and line, or a URL to
   Siemens/IACS/ISA documentation. "Best practice says" without a source is
   worthless.
4. **Separate proven from suspected.** Label each finding CONFIRMED (you can
   point at the evidence) or PLAUSIBLE (it looks wrong but you could not prove
   it).
5. **Say what you could not check** and why. That list is as valuable as the
   findings.

---

# Workstreams

Give each to its own sub-agent. They may need to read the same files; that is
fine. Do not let one agent's conclusion become another's premise.

## 1 — PLC logic and safety

Read `temp_fb_valveloop.xml`, `temp_fc_iomapper.xml`,
`temp_fc_physical_io_copy.xml`, `temp_ob86.xml`, `temp_main_ob1_patched.xml`
and the DB exports.

- Walk `FB_ValveLoop` line by line. It runs a 89-iteration loop every scan and
  is the whole control system. What happens on the edge cases — power-up,
  a command issued mid-travel, both limit switches made, neither made,
  a valve de-configured mid-stroke?
- **Commands are maintained**: the run latch is held for the whole stroke and
  cleared by a timeout. Trace exactly what de-energises a solenoid and when.
- **The travel timeout is hardcoded at `T#8S`** and the direction-fault grace at
  `T#5S`. Both are simulation numbers. Establish what a real DN300–DN400
  motorised or hydraulic valve actually takes, and quantify the consequence of
  the timeout being shorter than real travel.
- Is there any path where a valve can be commanded open and closed
  simultaneously, or where an output is left energised with nothing managing it?
- Four legacy blocks are still in the project and may still be called:
  `FB_ValveControl`, `FB_PlantSimulation`, `FB_ValveLogic`, `FB_HMI_MIirror`.
  Determine what they do, whether OB1 calls them, and whether they fight the
  live logic. Nobody currently working on this has read them.
- Assess fail-safe behaviour: on fault, on comms loss, on CPU stop, on power
  loss — what *should* each valve do, and what does it actually do?

## 2 — Scan time and real-hardware capacity

The whole project has only ever run under PLCSIM on a laptop.

- Estimate worst-case scan time for the 1214C: an 89-iteration loop with nested
  16-bit packing loops, timers per valve, plus the HMI mirror.
- The 1214C has 100 KB work memory and 4 MB load memory. Estimate actual usage
  from the DB sizes and block count. Is there headroom?
- ~2,150 HMI tags at 1 s acquisition over one S7 connection to a 1200. Estimate
  the communication load and whether the CPU's connection resources are
  adequate. The 1214C supports a limited number of concurrent connections —
  find the real figure and count what this project needs.
- The HMI has polled JavaScript on several screens, including a new
  one-per-second `GetActiveAlarms` call on the home screen. Assess screen load
  time and runtime CPU on an MTP1500.
- Will anything here lag on the real hardware? Be specific about what and why.

## 3 — Hardware configuration and I/O

- Verify the ET200SP module count against the valve count per zone. 89 valves ×
  4 inputs and × 2 outputs — does the configured I/O actually fit, with spares?
- Check `Valve_Channels_DB` channel assignments against the physical module
  addresses. 534 assignments; look for duplicates, out-of-range values, and
  channels assigned to the wrong station.
- **Module diagnostics are disabled**: `DiagnosticsNoSupplyVoltage = False`,
  `DiagnosticsWireBreak = False` on the DI modules. Should they be on? What are
  the consequences either way for unused channels?
- `EnableValueStatus = False` — per-channel validity is available and switched
  off. Assess whether it should be used and what enabling it costs in address
  layout.
- Review PROFINET topology: three stations on one line via BA 2xRJ45 bus
  adapters. Is a switch needed? What is the update time, and is the watchdog
  appropriate for a ship?
- Assess power: 24 V supply, potential groups, whether the DQ modules can drive
  the actual solenoid/relay loads continuously. **Maintained outputs mean coils
  are held energised for the whole stroke** — confirm continuous-duty ratings
  are actually required and check them against the BOM.
- Is there a hardware interlock between the OPEN and CLOSE outputs of a valve,
  or is mutual exclusion software-only?

## 4 — Client data reconciliation

This is the workstream most likely to find something expensive.

- Reconcile the client's 102-row valve schedule against their P&ID drawings and
  against `AUDIT_TAG_MAP.csv` and `Valve_Meta_DB`.
- Known open items to verify and expand: seven valves drawn on the P&ID that
  appear in no schedule row; `CM51` looking like a digit transposition
  (`11-009` vs `11-090`); the `System` column not being positional, so valves
  may sit on the wrong zone screen; the Bilge drawing carrying the title block
  of a **different vessel (M/V GROTON, IMO 9246310)**.
- If those seven extra valves are in scope, the PLC's 89 slots are short. Assess
  what expanding beyond 89 would cost across the PLC, the HMI and the I/O.
- Check that every valve's zone assignment matches its physical location and its
  station's I/O range.

## 5 — Standards, class and regulatory

Research properly; do not answer from memory.

- **Class requirements** for remote ballast valve control: DNV, Lloyd's
  Register, ABS, BV. What do they require for indication, alarms, fail-safe
  behaviour, local override, and independence from other systems?
- **IACS UR E22** on programmable electronic systems, and any requirements for
  software change control and testing evidence.
- **Ballast Water Management Convention** implications, if any, for valve
  logging and record retention.
- **ISA-18.2** alarm management — assess the 632-alarm set for rationalisation,
  priority distribution, and flood behaviour. Is a 632-alarm set defensible for
  89 valves, or will it flood?
- **ISA-101** HMI design — assess the screens against it, including the use of
  colour, the stale-data indication, and whether an operator can tell live data
  from last-known.
- **IEC 61131-3** conformance of the SCL.
- Emergency shutdown and remote closing requirements for ballast valves in a
  fire or flooding scenario — does this system have any part to play, and if so
  is it addressed?

## 6 — HMI, WinCC Unified and licensing

- Verify the tag count against the panel's licensed limit and the WinCC Unified
  licensing model. Confirm from Siemens sources what an MTP1500 includes.
- The audit trail requires `WinCC Unified Audit`. The runtime reports it as a
  missing licence. Establish exactly what is needed, what it costs, and what
  happens at runtime without it.
- No audit signing certificate is installed (`Integrity = 0`). Assess whether
  the audit trail is defensible to a surveyor in that state.
- Review the alarm-class priorities, the alarm filter (currently empty by
  design), and whether system alarms will reach the operator.
- Assess the storage design: SD card, four folders, 365-day audit retention,
  7-day alarm retention, segment backup. Is 7 days defensible? What happens when
  the card fills?
- **The panel's Audit Viewer shows internal tag names (`V021_Configured`), not
  the client's CM numbers.** Assess whether a readable record can be produced at
  the panel at all, and what it would take.

## 7 — Security

- PLC access level is currently **Full access (no protection)**, no password.
  Assess against Siemens hardening guidance and class expectations.
- PUT/GET communication, the web server, OPC UA server state, SNMP community
  strings (`public`/`private` are still default on the ET200SPs).
- WinCC Unified user management: currently no user holds the "User management"
  function right, and the compile warns about it.
- Assess the realistic threat model for a vessel network and what is
  proportionate.

## 8 — Commissioning readiness

- Is the documentation sufficient for somebody who is not the author to install
  this? Read `HARDWARE_INSTALL_TROUBLESHOOTING.md` and `README.md` as if you
  were that person.
- **A stated constraint: code cannot be changed during commissioning.** Assess
  every remaining assumption against that. What must be settled before the panel
  ships, and what can be discovered on site?
- Several behaviours are documented as unverified on real hardware: the SD card
  mount path `/media/simatic/X51/`, whether the diagnostic buffer resolves its
  text, whether OB86 fires, whether the system diagnostics matrix populates on a
  1200. Assess the risk of each and whether any can be closed before the panel.
- Is there a written test plan? If not, produce one.

## 9 — Adversarial sweep: what has been missed

Deliberately look outside the workstreams above.

- What questions has nobody asked? What is conspicuously absent from the
  documentation?
- Are there failure modes with no detection at all — sensor stuck, actuator
  jammed, limit switch shorted, cable damaged in a way that mimics a valid
  signal?
- What happens on power loss mid-stroke, on brownout, on CPU restart with valves
  part-open?
- Is there any single point of failure whose loss is silent?
- Is anything in this system safety-related in a way nobody has classified?

---

# Output

Consolidate into one report:

1. **Executive summary** — the five things that would stop this installation
   working, in order.
2. **Findings**, each with: severity (blocker / high / medium / low), CONFIRMED
   or PLAUSIBLE, file and line or URL, what actually goes wrong, and the fix.
3. **Verified good** — what you checked and found sound. Be specific; this is
   what gives the rest of the report credibility.
4. **Could not verify** — with the reason, and what would be needed.
5. **Questions for the client** — anything blocked on information only they have.
6. **Recommended test plan** before the panel ships and at commissioning.

Rank by what will actually hurt on a ship, not by how easy it is to write up.
