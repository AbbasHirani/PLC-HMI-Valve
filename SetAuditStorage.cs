// Points the Audit Trail at the panel's SD card and turns segment backup on.
//
// Why this matters: the trail keeps a rolling 365 days, and with BackupMode = NoBackup a segment
// that ages out is purged with no copy kept. Backup archives each segment as it closes, so the
// live log can still roll while the history survives.
//
// Reversible: --revert restores NoBackup + USBX61 (the state before 2026-08-23).
using System;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon;

class Program {
    // Unified Comfort Panels mount the X51 data card at /media/simatic/X51/. The documented
    // panel example is "/media/simatic/data-storage/My_Archives/TagLogs", i.e.
    // /media/simatic/<device>/<subfolder>. VERIFY ON REAL HARDWARE before relying on it:
    // a bad path means the backup silently writes nowhere.
    const string SD_BACKUP_PATH    = "/media/simatic/X51/auditLogBackup";
    const string AUDIT_LIVE_FOLDER = "/auditLogLive";

    // Live log subfolder on the card. Without this WinCC picks its own base folder and the layout
    // is whatever it decides - fine until someone has to service the panel and go looking for it.
    // Naming it puts the live log and the backups in two obvious, separate folders on the card.
    // StorageFolder needs a LEADING FORWARD SLASH. Without one the engineering layer refuses
    // it with a bare "Unable to set PropertyValue"; a backslash is rejected as an invalid
    // storage folder. An earlier version of this tool concluded the property could not be
    // set at all - it can, the format was simply wrong:
    //     "alarmLogLive"     refused
    //     "/alarmLogLive"    accepted
    //     backslash form     refused, invalid storage folder

    // The alarm log gets the same treatment, and it matters more than it looks: alarm
    // history keeps only 7 days against the audit trail's 365, so a fault from three weeks
    // ago leaves the operator actions on record with no sign of the alarm itself. Its
    // segments are 1 day, so with backup on every day is archived as it closes and the
    // history outlives the 7-day window.
    const string SD_ALARM_BACKUP_PATH = "/media/simatic/X51/alarmLogBackup";
    const string ALARM_LIVE_FOLDER    = "/alarmLogLive";

    // --backup-local only. Backup has never actually been tested: it has only ever been pointed
    // at /media/simatic/X51/..., a path on the panel's Linux filesystem that cannot resolve on a
    // Windows simulation. So "backup stops logging" may be nothing more than "that path does not
    // exist here". PrimaryPath is free text, so a local folder settles which of the two it is.
    const string LOCAL_AUDIT_BACKUP = @"C:\UnifiedArchive\TestBackup\audit";
    const string LOCAL_ALARM_BACKUP = @"C:\UnifiedArchive\TestBackup\alarm";

    // Backup fires when a SEGMENT closes, not when a row is written. Shipped periods are 30 days
    // for the trail and 1 day for alarms, so a test would show nothing for a month. Shortened to
    // a minute for the duration of the test, restored by --revert.
    const int SEG_TEST_MINUTES = 1;
    const int SEG_AUDIT_DAYS   = 30;   // shipped value, restored on revert
    const int SEG_ALARM_DAYS   = 1;    // shipped value, restored on revert

    static void Main(string[] args) {
        bool revert = args.Any(a => a == "--revert");
        bool deviceUsbOnly = args.Any(a => a == "--device-usb");
        // Isolation test: turn segment backup on and touch NOTHING else, so that if logging
        // stops we know it was the backup and not the move to the SD card. Backup has only ever
        // been switched on together with StorageDevice = SDX51, and SDX51 alone is known to stop
        // the trail in simulation, so backup has been carrying blame it may not deserve.
        bool backupOnly  = args.Any(a => a == "--backup-only");
        bool backupLocal = args.Any(a => a == "--backup-local");
        bool report = args.Any(a => a == "--report");

        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
        var proj = procs[0].Attach().Projects[0];
        Device hmiDevice = null;
        foreach (var d in proj.Devices)
            if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) hmiDevice = d;
        var hmi = FindHmiSoftware(hmiDevice);

        foreach (var at in hmi.AuditTrails) {
            Console.WriteLine("=== " + at.Name + " ===");
            Show("BEFORE", at);
            if (report) break;

            if (revert) {
                // Order matters: PrimaryPath turns read-only the moment BackupMode is NoBackup,
                // so it is never cleared here - a stale path is inert while backup is off anyway.
                at.Settings.StorageDevice = DeviceNode.USBX61;
                TrySetFolder(at.Settings, "");
                at.Backup.BackupMode      = HmiBackupMode.NoBackup;
                SetSegment(at, SEG_AUDIT_DAYS, 0);
            } else if (backupLocal) {
                at.Backup.BackupMode  = HmiBackupMode.PrimaryPath;
                TrySetPath(at.Backup, LOCAL_AUDIT_BACKUP);
                SetSegment(at, 0, SEG_TEST_MINUTES);
            } else if (backupOnly) {
                at.Backup.BackupMode  = HmiBackupMode.PrimaryPath;
                at.Backup.PrimaryPath = SD_BACKUP_PATH;
            } else if (deviceUsbOnly) {
                // Isolation step: put the live log back on USB but KEEP segment backup on, to find
                // out which of the two changes stopped the trail recording in simulation.
                at.Settings.StorageDevice = DeviceNode.USBX61;
            } else {
                // Live log onto the SD card...
                at.Settings.StorageDevice = DeviceNode.SDX51;
                TrySetFolder(at.Settings, AUDIT_LIVE_FOLDER);
                // ...and archive each closed segment so ageing out of the 365-day window
                // no longer means losing the records.
                at.Backup.BackupMode  = HmiBackupMode.PrimaryPath;
                at.Backup.PrimaryPath = SD_BACKUP_PATH;
            }
            Show("AFTER ", at);
            break; // this project has exactly one trail
        }

        Console.WriteLine();
        foreach (var al in hmi.AlarmLogs) {
            Console.WriteLine("=== " + al.Name + " (alarm log) ===");
            Show("BEFORE", al);
            if (report) continue;

            if (revert) {
                // The alarm log's medium is locked to whatever the TIA UI has set as the main
                // database location for alarm logging, so this only succeeds once that dropdown
                // is back on USB-X61. Reported rather than thrown - the rest of the revert should
                // still run.
                try { al.Settings.StorageDevice = DeviceNode.USBX61; }
                catch {
                    Console.WriteLine("  [device] cannot leave SD: the main database location for");
                    Console.WriteLine("           alarm logging is still SD-X51. Change it first in");
                    Console.WriteLine("           HMI_1 > Runtime settings > Storage system.");
                }
                TrySetFolder(al.Settings, "");
                al.Backup.BackupMode      = HmiBackupMode.NoBackup;
                SetSegment(al, SEG_ALARM_DAYS, 0);
            } else if (backupLocal) {
                al.Backup.BackupMode  = HmiBackupMode.PrimaryPath;
                TrySetPath(al.Backup, LOCAL_ALARM_BACKUP);
                SetSegment(al, 0, SEG_TEST_MINUTES);
            } else if (backupOnly) {
                al.Backup.BackupMode  = HmiBackupMode.PrimaryPath;
                al.Backup.PrimaryPath = SD_ALARM_BACKUP_PATH;
            } else if (deviceUsbOnly) {
                // nothing to do - the alarm log never leaves USB
            } else {
                // The alarm log CAN go on the SD card. Openness refuses the move on its own -
                // "Database of the log must be on the same medium as the main database for alarm
                // logging" - because the main-database medium is a separate setting that Openness
                // does not expose. It lives in the TIA UI at:
                //     HMI_1 > Runtime settings > Storage system
                //           > Main database location for alarm logging > Storage medium
                // With that set to SD-X51, the write below is accepted. An earlier version of this
                // tool recorded the move as impossible; it was only impossible from Openness alone.
                al.Settings.StorageDevice = DeviceNode.SDX51;
                TrySetFolder(al.Settings, ALARM_LIVE_FOLDER);
                al.Backup.BackupMode  = HmiBackupMode.PrimaryPath;
                al.Backup.PrimaryPath = SD_ALARM_BACKUP_PATH;
            }
            Show("AFTER ", al);
        }

        if (!report) {
            Console.WriteLine("\nSaving project...");
            proj.Save();
            Console.WriteLine("Saved.");
        }
    }

    // PrimaryPath normally holds a panel path. A Windows path may be rejected by the engineering
    // layer's validation - if it is, that is the answer and the test stops there.
    static void TrySetPath(dynamic backup, string path) {
        try { backup.PrimaryPath = path; Console.WriteLine("  [path] accepted: " + path); }
        catch (Exception ex) {
            while (ex.InnerException != null) ex = ex.InnerException;
            string m = ex.Message.Replace('\r', ' ').Replace('\n', ' ');
            if (m.Length > 130) m = m.Substring(0, 130);
            Console.WriteLine("  [path] REFUSED '" + path + "': " + m);
        }
    }

    static void SetSegment(dynamic log, int days, int minutes) {
        try {
            // The period fields are uint, and dynamic binding will not widen an int for them.
            log.Segment.SegmentTimePeriod.Days    = (uint)days;
            log.Segment.SegmentTimePeriod.Hours   = (uint)0;
            log.Segment.SegmentTimePeriod.Minutes = (uint)minutes;
        } catch (Exception ex) {
            while (ex.InnerException != null) ex = ex.InnerException;
            Console.WriteLine("  [segment] could not set period: " + ex.Message);
        }
    }

    static void TrySetFolder(dynamic settings, string folder) {
        try { settings.StorageFolder = folder; }
        catch (Exception ex) {
            while (ex.InnerException != null) ex = ex.InnerException;
            string m = ex.Message;
            int nl = m.IndexOfAny(new char[] { (char)13, (char)10 });
            if (nl > 0) m = m.Substring(0, nl);
            if (m.Length > 110) m = m.Substring(0, 110);
            Console.WriteLine("  [folder] '" + folder + "' refused: " + m);
        }
    }

    static void Show(string label, dynamic at) {
        Console.WriteLine("  " + label + ": StorageDevice=" + at.Settings.StorageDevice
                        + "  BackupMode=" + at.Backup.BackupMode
                        + "  PrimaryPath=" + (string.IsNullOrEmpty((string)at.Backup.PrimaryPath) ? "(empty)" : at.Backup.PrimaryPath)
                        + "  Folder=" + (string.IsNullOrEmpty((string)at.Settings.StorageFolder) ? "(default)" : at.Settings.StorageFolder)
                        + "  Retention=" + at.Settings.LogTimePeriod.Days + "d");
        // Backup only fires when a SEGMENT closes, not when a row is written, so the segment
        // period decides how long a backup test has to run before there is anything to see.
        try {
            var seg = at.Segment;
            Console.WriteLine("          segment closes every "
                            + seg.SegmentTimePeriod.Days + "d "
                            + seg.SegmentTimePeriod.Hours + "h "
                            + seg.SegmentTimePeriod.Minutes + "m"
                            + "  or at " + seg.SegmentMaxSize + " MB");
        } catch { Console.WriteLine("          (no segment settings on this log)"); }
    }

    static HmiSoftware FindHmiSoftware(Device device)
    { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
    static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
    { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
}
