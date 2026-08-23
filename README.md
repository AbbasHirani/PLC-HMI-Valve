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
| Backup | **off** | on |
| Where logs actually land | `C:\UnifiedArchive\` on the hard disk | the SD card |

With backup off, WinCC tolerates the missing media and quietly falls back to
a local path, which is why development works at all. **Turning backup on
during development stops both logs recording** — same failure as a wrong path
on the panel, same silent symptom. It is off deliberately.

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
