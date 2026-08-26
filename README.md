# MV WESTERLY — Valve Remote Control System

Remote control and monitoring for the ballast and bilge valves on MV WESTERLY
(IMO 9834217). Siemens S7-1200 PLC, ET200SP remote I/O, MTP1500 Unified
Comfort Panel running WinCC Unified.

89 valves across three sections, each with open/close commands, limit switch
feedback, healthy and local/remote status, travel-timeout and direction-fault
alarms. Every operator action is recorded to an audit trail.

---

## READ THIS BEFORE INSTALLING ON THE REAL PANEL

Everything storage-related is **staged, not applied**. The project as
committed is in development configuration and logs to the laptop's hard disk.
[Panel installation](#panel-installation) below is what turns it into the
delivered configuration. Skip it and the panel records nothing.

---

## Documentation

| File | What it covers |
|---|---|
| `AUDIT_DEPLOYMENT.md` | Audit trail in full — what is recorded, how, retention, delivery steps |
| `HARDWARE_INSTALL_TROUBLESHOOTING.md` | On-site sheet: symptom to cause to fix, no network needed |
| `VALVE_SCADA_SYSTEM_TECHNICAL_DOCUMENTATION.md` | PLC and HMI architecture |
| `SESSION_HANDOFF.md` | Running engineering log |
| `CLAUDE.md` | Original engineering specification |

---

## Panel installation

### 1. The SD card

**One SD card, fitted in slot X51.** Everything logged lives on it — both
logs, live and backup, four folders:

```
SD card (X51)
|-- auditLogLive/       audit trail, rolling 365 days
|-- auditLogBackup/     each audit segment archived as it closes
|-- alarmLogLive/       alarm history, rolling 7 days
`-- alarmLogBackup/     each alarm segment archived as it closes
```

**Create all four folders on the card yourself before fitting it.** It is not
established that WinCC creates them. If it does, no harm done; if it does
not, you have avoided a silent failure — see
[If nothing is recording](#if-nothing-is-recording) for why that matters.

Two notations appear for the same card, which is confusing but correct:

| | Written as | Why |
|---|---|---|
| Live log | `/auditLogLive` | the device is chosen separately, so the folder is relative to the card |
| Backup | `/media/simatic/X51/auditLogBackup` | takes a full filesystem path, no device field |

`/media/simatic/X51/` **is** the card. The two forms point at siblings on one
piece of media.

### 2. Set the alarm log's main database to the card

Do this **first**, in the TIA UI. Openness cannot reach this setting, and
without it the next step is refused.

```
HMI_1 > Runtime settings > Storage system
      > Main database location for alarm logging > Storage medium = SD-X51
```

Without it, `SetAuditStorage.exe` fails on the alarm log with:

```
Database of the log must be on the same medium as the main
database for alarm logging.
```

The constraint is symmetric. Once this is on the card, the alarm log cannot
be moved back to USB until this dropdown goes back first.

### 3. Apply the storage settings

With TIA Portal open on the project:

```
SetAuditStorage.exe            apply delivery configuration
SetAuditStorage.exe --report   confirm what it set
```

Which writes:

| | Audit trail | Alarm log |
|---|---|---|
| `StorageDevice` | `SDX51` | `SDX51` |
| `StorageFolder` | `/auditLogLive` | `/alarmLogLive` |
| `BackupMode` | `PrimaryPath` | `PrimaryPath` |
| `PrimaryPath` | `/media/simatic/X51/auditLogBackup` | `/media/simatic/X51/alarmLogBackup` |
| Retention | 365 days | 7 days |

Then compile and download to the panel.

`SetAuditStorage.exe --revert` puts it back to development configuration —
but change the dropdown in step 2 back to `USB-X61` first, or the alarm log
stays on the card.

### 4. First boot — check this before anything else

**Go straight to the ALARMS screen.** Before operating anything.

If you see any of these, storage is not working:

```
RemovableStorage   Storage medium not available. Tag: SD-X51
StorageSystem      AuditTrail: Backup of a segment ... failed
SystemService      Service (AuditTrail) is not being executed
```

If the screen is clear, confirm properly:

- [ ] Operate a valve — open and close one
- [ ] Check it appears on the **AUDIT LOG** screen
- [ ] Trip an alarm — enable a valve and let the travel timeout fire
- [ ] Check it appears in **ALARM HISTORY**
- [ ] Power down, pull the card, look at it on a PC: are there files in
      `auditLogLive/` and `alarmLogLive/`?

That last check is the one that actually proves it. The others can pass while
the data goes somewhere you did not intend.

---

## If nothing is recording

### The backup path is the most likely cause

`/media/simatic/X51/` is **inferred from Siemens documentation and has never
been seen on real hardware.** It is the single least-verified thing in the
delivered configuration.

**A wrong backup path does not just cost you the backup — it costs you all
logging.** Measured on 2026-08-24: when backup cannot reach its target, WinCC
does not degrade, it shuts the logging services down completely.

It is specifically the *unreachable path* that does this, not backup itself.
Backup pointed at a path that exists runs perfectly happily — see
[Testing backup](#testing-backup). That is why the path is the thing to check
first when nothing is recording.

```
RemovableStorage   Storage medium not available. Tag: SD-X51
RemovableStorage   Storage medium not available. Tag: USB-X61
StorageSystem      AuditTrail:   Backup of a segment ... failed
StorageSystem      AlarmLogging: Backup of a segment ... failed
SystemService      Service (AlarmLogging) is not being executed
SystemService      Service (AuditTrail)   is not being executed
```

The system carries on looking completely normal. Valves operate, alarms raise
and acknowledge on screen — **alarm annunciation is unaffected, only logging
dies.** Nothing tells you except those alarms.

**Fix:** the panel's own Control Panel shows the true mount point for the X51
slot. Read it there, then correct `SD_BACKUP_PATH` and `SD_ALARM_BACKUP_PATH`
in `SetAuditStorage.cs`, rebuild, re-run, download again. One-line change.

### No card fitted

No card in X51 means no logs at all, and nothing warns you except the same
alarms. Check the slot before assuming a software fault.

### Changing StorageDevice destroys the existing log

**142 audit records were lost this way on 2026-08-23.** Never change the
storage device on a panel already carrying history that matters. Export
first.

---

## Development configuration

The laptop has no SD card and no X51 slot, so the delivery settings cannot
work here and must not be left applied.

| | Development | Delivered |
|---|---|---|
| Storage device | `USBX61` | `SDX51` |
| Backup | off | on |
| Backup path | n/a | `/media/simatic/X51/...` |
| Where logs actually land | `C:\UnifiedArchive\` on the hard disk | the SD card |

With no card fitted, WinCC tolerates the missing media and quietly falls back
to a local path, which is why development works at all.

Backup is off here only because its delivered path is a panel path and cannot
resolve on Windows. Backup itself works fine in simulation if you give it a
path that exists.

### Testing backup

```
SetAuditStorage.exe --backup-local    backup on, into C:\UnifiedArchive\TestBackup\
SetAuditStorage.exe --revert          back to development config
```

`--backup-local` changes two things. It points `PrimaryPath` at a local
Windows folder, which the engineering layer accepts without complaint — the
field is free text and is not validated against the panel's filesystem. And
it shortens `SegmentTimePeriod` to one minute, because **backup fires when a
segment closes, not when a row is written**, and the shipped periods are 30
days for the trail and 1 day for alarms. Without that second change a test
shows nothing for a month. `--revert` restores both.

Result of that test on 2026-08-24, over a four-minute run:

- **Both logs kept recording normally** — 122 new audit rows, 12 new alarm
  rows, with backup enabled throughout
- Segments rolled every minute as configured, 9 new audit and 5 new alarm
- One backup file was written, named
  `<log>_<segment start>_<segment end>.bak`

That single backup file is worth understanding before you read too much into
a short test on the panel. It was written seconds after startup, and it was
for a segment that had closed *before* the test began — a catch-up sweep of
the one closed, unarchived segment that existed. **None of the segments that
closed during the four minutes were archived in that window.**

So archiving is evidently not immediate on segment close. Whether it is on a
timer, or waits until a segment is no longer the most recent closed one, was
not established — four minutes was too short to tell. Plan for it on the
panel: an empty backup folder shortly after commissioning is not proof of
failure. The alarm on the ALARMS screen is the reliable signal, not the
folder.

### Is logging alive?

```
python check_logs.py --save     baseline before a runtime session
python check_logs.py            compare after it
```

Counts rows on disk before and after. Reads each database *with its WAL* —
the runtime holds the live files open and recent writes sit in the `-wal`
until it checkpoints, so copying the `.db3` alone under-reports and makes a
working log look dead.

### Reading the audit trail

```
python audit_report.py                 latest segment, readable
python audit_report.py --csv out.csv   export
python audit_report.py --days 7        last week only
```

Translates the raw records into operator language — `CM79  DISABLED  by USER`
rather than `HMI_RT_1::V021_Configured, true -> false`. The CM number comes
from `AUDIT_TAG_MAP.csv`; it is project data and cannot be derived from the
tag name.

---

## Building

Compile through PowerShell, never Git Bash — Bash mangles `/nologo` into a
filesystem path and csc fails with CS2001.

```powershell
.\compile_builder.ps1                    # build HmiBuilder.exe
.\HmiBuilder.exe --only=Home,Alarms      # regenerate specific screens
.\CompileHmiOnly.exe                     # compile the HMI, report errors
```

`--only=` accepts: `Home, Alarms, AlarmColumns, Bilge, Fwd, Aft, Diag, Login,
DiscreteAlarms`. Omit it to rebuild everything, which is slow.

TIA Portal must be open on the project — every tool attaches to the running
instance rather than opening the file itself.

---

## System diagnostics

Reached from **CONFIG → ⚙ SYSTEM DIAGNOSTICS**, bottom row. Not in the nav bar:
the nav is full at seven buttons and an eighth would mean narrowing all of them
on every screen — a full rebuild for a screen an operator has no reason to
open. CONFIG is already the engineering screen.

Two tabs, the same shape as the ALARMS screen:

| Tab | Shows |
|---|---|
| **MODULE MATRIX** | Grid of the PLC, the panel and the three ET200SP stations with all 36 I/O modules, coloured by status |
| **DIAGNOSTIC BUFFER** | The PLC's own diagnostic buffer as an event list with timestamps |

Both are `HmiSystemDiagnosisControl`, a Siemens control that builds itself from
the hardware configuration. Add a module in TIA and it appears here with no HMI
work — as against roughly 45 status bits, a diagnostics DB for the PLC to write
them into, and a tile per device to keep in step with the rack by hand.

Rebuild with `HmiBuilder.exe --only=SysDiag`. It has its own key because the
diagnosis controls are slow to place and CONFIG does not need rebuilding to
change this screen.

### Controller alarms are enabled — and the alarm filter had to go with them

`Runtime settings → Alarms → Controller alarms and diagnostics` now has
**System diagnostics** and **Security events** ticked on `HMI_Connection_1`.
That turns PLC diagnostic events into alarms on the ALARMS screen, where
operators already look. Without it a station can drop off PROFINET and nothing
says a word — the valves on it simply stop responding.

Enabling it exposed a trap in the alarm control. Its filter allow-listed four
classes: `ValveFault`, `ValveWarning`, `ValveEvent` and `System`. But controller
diagnostics arrive in Siemens' **built-in** classes — `SystemAlarm`,
`SystemWarning`, `SystemInformation`, `SystemNotification` and two
`WithoutClearEvent` variants. Our custom class is called plain `System`, which
matches none of them, so every station-failure alarm would have been dropped
silently.

The filter is now empty on purpose. The alarms it excluded are ones a ship's
operator needs: `PlcDisconnectedAlarm` means the valves have stopped answering,
and the storage alarms are what revealed on 2026-08-24 that both logs had
stopped recording while everything else looked normal. It is left empty rather
than allow-listing every class, because an empty filter cannot hide anything by
accident and a misspelt class name silently can. If it ever needs narrowing,
exclude named classes rather than allow-list them.

### Still outstanding

**2. Add OB 86** on the PLC. Siemens' V20 documentation is explicit that the
S7-1200 supports it:

> "The S7-1200 CPU operating system calls the OB 86 in the following cases: The
> failure of a DP master system or of a PROFINET IO system is detected... The
> failure of a DP slave or of an IO device is detected"

An earlier note in this project said the S7-1200 had no station-failure OB.
That was wrong. There is also a polling alternative if it is wanted in ladder
instead — `DeviceStates` with `MODE := 4` returns a bit array where bit *n* is
device *n* having a communication error.

### The diagnostic buffer shows "##Text missing##"

Every row of the buffer view arrives with its event number, timestamp and event
class, and `##Text missing##` where the text should be. So the control is
reading the buffer; only the text resolution fails.

Siemens' documented cause is access: the control reads the text out of the PLC,
and an unentitled connection gets headers without text. **That is not the cause
here.** `PLC_1 → Protection & Security` reads *Full access (no protection)*
with HMI, Read and Write all granted and no password, checked 2026-08-25.

Nor is it language, which the compile warning invites you to assume. Project
active languages are `en-US` alone and the HMI runtime language is
`English (United States)`.

The remaining likely explanation is PLCSIM — a simulated CPU may not carry the
text resources a real one does; all thirteen events present were simulator
startup entries. Treat it as a first-boot check on the panel: if real events
show real text there, it was simulation all along. If they do not, the compile
warning about the PLC language configuration is the next thread to pull.

### Station failure detection (OB 86)

Until this existed, an ET200SP could lose power and nothing in the system
noticed. The valves on it stopped answering, and **the HMI carried on showing
their last known positions as though they were live.** Stale data presented as
current is the real hazard here, more than the loss of control - an operator
plans a ballast transfer on positions that stopped being true ten minutes ago.

The chain, end to end:

```
ET200SP_AFT loses power
   -> CPU calls OB86        LADDR=273, Event_Class=16#39
   -> Diag_DB.AftLost := TRUE, AftLossCount +1
   -> FB_ValveLoop          HwHealthy[2] := NOT AftLost
   -> Valves_DB.HwWord      bit 1
   -> HMI tag Valves_DB_HwWord, 1 s poll
   -> "Aft Ballast RIO station failure." on the ALARMS screen
```

and the reverse on `Event_Class = 16#38`, when the station returns.

| Station | Hardware id | Diag_DB flag | HwHealthy | HwWord bit | Alarm |
|---|---|---|---|---|---|
| ET200SP_AFT | 273 | `AftLost` | `[2]` | 1 | System_Aft_RIO_Fault |
| ET200SP_MID | 288 | `MidLost` | `[3]` | 2 | System_Bilge_RIO_Fault |
| ET200SP_FWD | 306 | `FwdLost` | `[4]` | 3 | System_Fwd_RIO_Fault |

**The three alarms already existed.** They were generated with the rest of the
alarm set, correctly worded, and nothing had ever set their bits - an alarm
built for exactly this, waiting on a value nobody wrote. This connects them
rather than adding parallel ones.

#### Why the hardware identifiers are data, not code

`Diag_DB.Laddr_AFT/MID/FWD` hold 273/288/306. Re-adding a station in TIA
reassigns these, and as literals in SCL that would be a code edit and a
redownload; as DB values it is a data change. The current values are readable
off the **SYSTEM DIAGNOSTICS** screen - every device tile shows its identifier
beneath the name, which is where these came from in the first place.

If a station fails whose identifier matches none of the three, `UnknownLost`
and `LastUnknownLaddr` record it rather than dropping it. That means either the
identifiers moved and Diag_DB was not updated, or a device is on the network
this block does not know about. Silently ignoring it would hide a real station
failure, which is the exact thing the block exists to prevent.

#### Why FB_ValveLoop derives HwHealthy instead of OB86 writing it

OB86 could write `HwHealthy` directly and save a hop. It must not, because
FB_ValveLoop carries this, every scan:

```scl
IF NOT "Valves_DB".HwHealthy[1] AND NOT "Valves_DB".HwHealthy[8] THEN
    FOR #b := 1 TO 9 DO "Valves_DB".HwHealthy[#b] := TRUE; END_FOR;
END_IF;
```

That condition is the state at CPU startup. A station **already down when the
CPU starts** would have OB86 set its bit and then have this init wipe it in the
same scan - the one case that matters most reporting healthy. Deriving
`HwHealthy[2..4]` from Diag_DB after the guard makes the ordering unable to
lose a genuine loss. Diag_DB is written by OB86 alone and read everywhere else.

Note the inverted sense: `HwHealthy` TRUE means well, and `HwWord` sets its bit
on `NOT HwHealthy`. Backwards here would mean a system that reports healthy
precisely when it is not.

#### Not testable in simulation

There is no station to unplug. It compiles and downloads; whether it fires is a
pull-the-ethernet-cable check on the panel. What can be said is that every
value it matches against was read off the hardware rather than assumed, and the
event-class meanings (16#39 incoming, 16#38 outgoing) are Siemens' convention
rather than recollection.

The loss counters are worth watching at commissioning: a station cleanly down
reads 1, one flapping on a loose connector in a vibrating engine room climbs,
and the status bit alone cannot tell those apart.

### Not testable in simulation

There are no stations to unplug. The screen compiles, downloads and draws, but
the real questions are panel-only:

- Does an S7-1200 populate the matrix as richly as an S7-1500? The 1200 has
  thinner built-in diagnostics and may show less.
- Does the control need a licence beyond base runtime?

Expect the matrix to look empty or sparse in simulation. What can be checked
here is only that the button navigates, the tabs switch, and the layout holds.

---

## Still open

- Backup path `/media/simatic/X51/` unverified on hardware — see above
- Audit signing certificate not installed (`Integrity = 0`), so records are
  logged but not cryptographically signed
- Runtime names `WinCC Unified Audit Basis - V20.0` as a missing licence
- Panel time source not confirmed — every audit record is stamped with it,
  and the integrity checksum protects a record's contents, not whether its
  timestamp is true
- Alarm retention is 7 days; raising it is possible (`LogTimePeriod.Days` is
  writable) if the client expects to browse older alarms on the panel itself
- **A stray `Connection_1`** sits alongside `HMI_Connection_1` with no Station,
  Partner or Node, and a different HMI address (192.168.0.2 against
  192.168.0.20). Delete it, and confirm which address the real panel uses
- **The diagnostic buffer shows `##Text missing##`** — not access, not
  language; see [System diagnostics](#system-diagnostics)
- Whether the system diagnostics matrix populates usefully on an S7-1200 —
  panel-only question
