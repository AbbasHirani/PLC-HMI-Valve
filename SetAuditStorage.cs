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
    const string SD_BACKUP_PATH = "/media/simatic/X51/AuditBackup";

    static void Main(string[] args) {
        bool revert = args.Any(a => a == "--revert");
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
            if (report) return;

            if (revert) {
                at.Backup.BackupMode      = HmiBackupMode.NoBackup;
                at.Backup.PrimaryPath     = "";
                at.Settings.StorageDevice = DeviceNode.USBX61;
            } else {
                // Live log onto the SD card...
                at.Settings.StorageDevice = DeviceNode.SDX51;
                // ...and archive each closed segment so ageing out of the 365-day window
                // no longer means losing the records.
                at.Backup.BackupMode  = HmiBackupMode.PrimaryPath;
                at.Backup.PrimaryPath = SD_BACKUP_PATH;
            }
            Show("AFTER ", at);
            Console.WriteLine("\nSaving project...");
            proj.Save();
            Console.WriteLine("Saved.");
            return; // this project has exactly one trail
        }
        Console.WriteLine("[ERROR] No audit trail found.");
    }

    static void Show(string label, dynamic at) {
        Console.WriteLine("  " + label + ": StorageDevice=" + at.Settings.StorageDevice
                        + "  BackupMode=" + at.Backup.BackupMode
                        + "  PrimaryPath=" + (string.IsNullOrEmpty((string)at.Backup.PrimaryPath) ? "(empty)" : at.Backup.PrimaryPath)
                        + "  Retention=" + at.Settings.LogTimePeriod.Days + "d");
    }

    static HmiSoftware FindHmiSoftware(Device device)
    { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
    static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
    { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
}
