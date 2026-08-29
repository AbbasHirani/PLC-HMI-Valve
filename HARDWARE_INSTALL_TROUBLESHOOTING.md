# Hardware Install Troubleshooting Sheet — MV Westerly Valve Control System

**Read this before you leave, and keep it open/printed on-site.** Written for a no-network
situation — no links out, no "ask Claude," just symptom → likely cause → fix. If none of these
fixes it, STOP and don't force it (see the last section).

---

## 0. Before you leave — pre-flight checklist

- [ ] Laptop has the full project folder: `C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\`
- [ ] TIA Portal V20 opens the `.ap20` project file successfully, offline, right now
- [ ] Full offline compile (PLC + HMI) shows 0 errors — if it doesn't, fix it now, not on-site
- [ ] `MV_Westerly_IO_Wiring_Schedule.xlsx` is on the laptop AND printed on paper (screens die,
      paper doesn't)
- [ ] This file is either printed or saved somewhere you can open without TIA/internet (e.g. a
      phone photo of each page, or a second device)
- [ ] **SD card for slot X51 in your bag**, with all four log folders already created on it:
      `auditLogLive` `auditLogBackup` `alarmLogLive` `alarmLogBackup`. No card means no audit
      trail and no alarm history, and nothing warns you except an alarm on the ALARMS screen
- [ ] Read `README.md` section **Panel installation** before touching the panel — the storage
      settings are staged, not applied, and there is one setting in the TIA UI that has to be
      changed *before* the tool will run
- [ ] After first boot, go to the **ALARMS** screen before operating anything. `RemovableStorage`
      or `SystemService` alarms there mean logging is dead — the system otherwise looks perfectly
      normal, valves operate and alarms annunciate, but nothing is being recorded
- [ ] Know your PLC's static IP / device name and each ET200SP station's device name — write them
      down separately from the laptop in case the laptop itself has problems

---

## 1. Commissioning sequence — do these in this order

The rest of this file is symptom-driven. This section is the running order. Everything
below assumes the panel is built, the stations are powered, and the network is patched.

### 1.1 Name the three ET200SP stations — before anything else

PROFINET finds devices by **name**, not by IP. New stations arrive unnamed, and until each
one carries the name the project expects, the CPU cannot find it.

1. `Online access` → your laptop's network adapter → **Update accessible devices**.
2. The stations appear, identified only by MAC address. **Three identical boxes.**
3. Select one → **Flash LED**. The physical unit blinks. Walk to the cabinet and see which
   one it is. This is the only reliable way to tell AFT from MID from FWD.
4. Right-click → **Assign device name**, matching what the project expects.

Where the project defines it: select the IM → `Properties` → `PROFINET interface` →
`Ethernet addresses` → **PROFINET device name**.

> **Check the AFT station's name in the project first.** Its IM is still called
> `ET200SP_AFT_TEST`, so the device name is likely `et200sp-aft-test`. Assigning that writes
> "test" onto hardware on a working ship. Rename it in the project before this step.

Getting this wrong is not a subtle failure: **every valve on the ship answers to the wrong
zone**, because `FC_IoMapper` keys the whole station-to-valve relationship off which station
reported which channel.

### 1.2 Download the hardware configuration

Right-click `PLC_1` → `Download to device` → **Hardware configuration**.

This sends the module layout, the addresses, and the **protection settings**. The CPU stops
to accept it.

> **On first commissioning, consider `Download to device → All` instead**, which sends
> hardware and software in one operation. See 1.4 for why the order matters to the password.

### 1.3 Download the program blocks

Right-click `PLC_1` → `Download to device` → **Software (all blocks)**.

`Main [OB1]`, `OB86`, `FB_ValveLoop`, `FC_IoMapper`, `FC_PhysicalIoCopy` and every DB. Then
put the CPU in RUN.

### 1.4 Expect the password prompt — and know when it starts

The CPU is set to **Read access with a password** (item 41). Timing:

| Download | Prompt? |
|---|---|
| The one that installs protection | **No** — the CPU is not protected yet when it begins |
| Every download after that | **Yes** |
| Going online only to read / watch | No — that is what Read access permits |

The dialog appears when you click Download, before anything transfers. TIA remembers the
password for the rest of that session, so it is one prompt per TIA session, not per download.

**This is why `All` is easier on day one:** the whole install happens while the CPU is still
unprotected, and the prompt starts from your next session.

> The password must be in the handover pack that ships with the panel. If it is lost, the
> only route back is a factory reset of the CPU, which **erases the program**.

### 1.5 Confirm all three stations came online

A free and very valuable test, and it happens before a single valve is touched.

- Green LEDs on all three IMs.
- No station alarms on the ALARMS screen.
- If one is missing, `OB86` has fired and the matching `System_*_RIO_Fault` alarm is active —
  which also proves that whole detection chain works on real hardware.

Check the **device name first** if a station is missing. It is the most common cause and the
least obvious.

### 1.6 Fit the SD card, then download the HMI

**Card first.** It goes in the panel's X51 slot and must be present *before* the runtime first
starts, or alarm and audit logging have nowhere to write. See `README.md` → Panel installation.

Then right-click `HMI_1` → `Download to device`. Separate device, separate download; it has no
relationship to the PLC download beyond needing the PLC's tags to exist.

The panel needs its **V20 runtime image** already installed. A panel from stock will not accept
the project until it is updated — do that at the shop, not on board.

### 1.7 First three numbers to read — this closes item 27

As soon as you are online with the real CPU, `Online & diagnostics`:

| Read | Where | Why |
|---|---|---|
| **Cycle time** — shortest / longest / current | Online & diagnostics | Scan time has never been measured on real hardware. Watch the *longest*: the 200 ms table refresh is a periodic spike. |
| **Work memory** used vs free | same screen | 100 KB on a 1214C, shared by program and DBs. Never measured. |
| **Cycle-time watchdog** setting | `PLC_1 → Properties → Cycle` | Confirms the actual limit. Exceeding it faults the CPU to STOP. |

Estimated scan is 3–8 ms against a 150 ms watchdog, so this should be comfortable — but it is
an estimate, and five minutes here replaces it with a fact.

### 1.8 Configure the valves

After any download, **every valve comes up UNCONFIGURED** and nothing responds. Press
**CONFIGURE ALL** on the CONFIG screen.

If item 36 has landed by then this stops being a step, and a Home banner says so out loud when
it happens anyway. Until then it is an unmarked trap — the panel looks completely normal.

### 1.9 The I/O check — the real work

89 valves × 6 signals = **534 checks**. There is no shortcut and this is what a commissioning
record is made of.

Per valve:

1. Operate the **open** limit switch by hand. Confirm that valve — **and only that valve** —
   changes on the panel.
2. Repeat for **closed**, **healthy**, **local**.
3. Command OPEN, confirm the correct relay clicks. Then CLOSE.

Step 1's second half is the one that matters. A wrong channel number shows up as *the wrong
valve reacting*, and it is a **data fix**: change the number in `Valve_Channels_DB` and
re-import. No code change, nothing to re-test beyond that valve.

### 1.10 Stroke times

Stopwatch each valve, both directions, seat to seat. Also note the **seat-break time** — how
long it holds its starting limit switch after the actuator drives.

Enter the measured values per valve. **This only works if item 35 has been built** — until the
timers are per-valve data, there is nowhere to put the numbers and every valve slower than
8 seconds will halt part-open on every command.

### 1.11 Behaviour tests

- **Pull a station's network cable** → one alarm, zone banner appears, positions freeze at last
  known. Reconnect → recovers.
- **Kill 24 V to one station** → today this gives ~81 alarms and nothing naming the cause. After
  item 39 it should give one.
- **Power-cycle the CPU** → confirm valves return configured (item 36) and that **nothing moves**
  on startup.
- **Switch the panel off** → today the PLC never notices. After the heartbeat work it should alarm.
- **Clear every force before you leave.** A forced value survives a CPU restart and is easy to
  forget. Force an output on a real valve and it moves.

---

## 2. Downloading to the real PLC

**Symptom: "No accessible devices found" when trying to go online / download**
- Check the physical Ethernet cable is actually in the PLC's PROFINET port, not a different port
- Check your laptop's network adapter is set to the SAME subnet as the PLC (if PLC is
  192.168.0.1, your laptop's Ethernet adapter needs a 192.168.0.x address, not DHCP/auto — S7-1200s
  don't hand out DHCP by default)
- Try "Accessible devices" (not "Download") first — if the PLC doesn't show up there, it's a
  network/cabling problem, not a TIA problem
- A cheap unmanaged switch between laptop and PLC can help if you also need the ET200SP stations
  reachable at the same time — don't daisy-chain through a station unless the bus adapter
  (BA 2xRJ45) is actually wired for pass-through

**Symptom: Download fails with "Online and offline configurations are different" /
"Incompatible configuration"**
- Someone (or a previous session) changed the module config in a way that doesn't match the
  physical rack. Don't just force-override — first check: does the real physical module order in
  each ET200SP rack actually match what's in the TIA project (DI/DQ module count and order)? If a
  module was swapped or added on-site, the project needs updating, not the other way around.

**Symptom: Download fails with a PROFINET device-name mismatch (e.g. "device name
'et200sp_aft' expected, found 'et200sp-aft-1'")**
- The physical station's PROFINET name (set via its display, or via "Assign device name" in TIA)
  doesn't match what the project expects. Use TIA's **Online > Assign PROFINET device name**
  dialog, select the actual physical device, and either rename it to match, or update the project
  if the physical name is what should be kept.

**Symptom: "Not enough work memory" or similar during download**
- Genuinely rare on a 1214C for a project this size (already verified to fit — see project history).
  If this happens, it likely means something got duplicated or bloated since the last known-good
  compile. Don't try to fix this live on-site; fall back to the simulator-tested version.

---

## 3. Going online / first power-up

- Bring the PLC up **without the HMI panel connected first**, if practical — easier to diagnose
  PLC-only problems (via TIA's own online diagnostics) without a second device in the loop.
- **Online > Diagnostics** in TIA shows module-level LED-equivalent status. A red module in this
  view means a real hardware fault (module not seated, no power to the rack, etc.) — not
  something software can fix.
- If a specific ET200SP station doesn't come online: check its own power supply first (each
  station needs its own 24V feed, separate from signal wiring), then check the PROFINET cable
  into its bus adapter.

---

## 4. I/O-specific checks (this is the part that matters most for this trip)

The whole point of this trip is validating that real field signals reach the right valve on the
HMI. Do this in order, valve by valve, starting with just 1-2 valves before assuming everything's
fine:

1. **Confirm the wire is physically at the right terminal** — cross-check against
   `MV_Westerly_IO_Wiring_Schedule.xlsx` (DI Schedule / DO Schedule tabs). Every row has the exact
   `%I`/`%Q` address, station, module, and channel.
2. **Open a Watch Table in TIA** (or use "Monitor all" on the block) and watch
   `IO_Buffer_DB.DI[n]` / `IO_Buffer_DB.DQ[n]` for that channel's index (also in the Excel sheet,
   "Sl. No" column corresponds to the array index within that signal type — DI schedule and DQ
   schedule are numbered separately). Toggling the real field device (e.g. manually operating a
   limit switch) should flip that bit live.
   - **If the bit doesn't move at all**: the physical wiring or the field device itself is the
     problem, not the PLC program — check polarity (24V DI: is it wired for sourcing or sinking
     correctly?), check the field device is actually powered.
   - **If the bit moves but a DIFFERENT index than expected changes**: the physical wire is in the
     wrong terminal — cross-check the module/channel against the schedule again, terminal numbers
     count 1-16 per module in the physical rack.
3. **Then check `Valve_Channels_DB`** for that valve's slot — `OpenFbChannel[n]`,
   `ClosedFbChannel[n]` etc. should hold the same channel number you just confirmed above. This
   was already populated and verified in software (see project history) — it should not need
   changing unless the client's wiring plan itself changed.
4. **Then check the HMI** — the valve's popup / zone screen should now reflect the real signal.
   If step 2 and 3 both check out but the HMI still shows the wrong thing, that's a genuine
   software bug worth writing down precisely (which valve, which signal, what you saw vs.
   expected) to fix once you're back online.

**Important, don't skip this:** before trusting any of the above, confirm
`FB_ValveLoop`'s simulation guard is actually in place (this was being added the same week as this
sheet — check the block's code has a check like "skip simulated OpenFB/ClosedFB write if a real
channel is assigned" near the top). If that guard isn't there, the simulator will keep overwriting
real signals and every test above will look broken even if the wiring is perfect. **This is the
single most likely false alarm on-site — check it first if things look wrong.**

---

## 5. TIA Portal quirks already known from this project (don't waste time rediscovering these)

- **`Attach()` / online operations occasionally throw a timeout error even when TIA is completely
  healthy.** Check Task Manager — if TIA is using real CPU (not stuck at 0%), it's just slow, not
  hung. Wait and retry once before assuming something's broken.
- **A brand-new Windows account needs to be in the local "Siemens TIA Openness" group**, and needs
  a full logoff/login (not just restarting TIA) after being added, before Openness tools will work.
  Not relevant to manual TIA UI use, only if you're trying to run any of the `.exe` automation
  tools in `src/` on a different machine.
- **Manual edits made directly in a TIA screen get wiped** if that screen is ever rebuilt by the
  generator tool later. If you tweak something in the HMI screen editor on-site to get through an
  install, write down what you changed so it can be re-applied properly later — don't just leave it
  as an undocumented one-off.
- **Block export fails if the target file already exists** — if you're exporting anything for your
  own reference on-site, delete any old file with the same name first.

---

## 6. When to stop and not force it

Do **not**:
- Force-download over a device-name or module mismatch without understanding why it doesn't match
- Wire around a wrong physical terminal "just to test" without immediately fixing it back or
  writing it down — this is exactly how real installs end up with silent wiring errors that surface
  months later
- Change `Valve_Channels_DB` values on-site without a clear reason tied to something you've
  physically confirmed — these were generated from a reviewed mapping, don't second-guess them from
  a single confusing reading without ruling out the simulation-guard issue first (Section 3)

If you hit something not covered here and have zero connectivity: **document it precisely**
(valve/CM tag, what you expected, what you saw, exact error text if any) rather than guessing a
fix under pressure. A precise problem description gets solved in five minutes once you're back
online; a vague "it didn't work" plus a guessed workaround can cost hours untangling later.
