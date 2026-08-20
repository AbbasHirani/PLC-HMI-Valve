using System;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

class Program {
    const string LOG_NAME = "AlarmLog_Main";
    static void Main(string[] args) {
        bool apply = args.Any(a => a == "--apply");
        bool revert = args.Any(a => a == "--revert");
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("No TIA Portal running."); return; }
        var tia = procs[0].Attach();
        var proj = tia.Projects[0];
        HmiSoftware hmi = null;
        foreach (var d in proj.Devices) {
            foreach (var it in d.DeviceItems) { hmi = Find(it); if (hmi != null) break; }
            if (hmi != null) break;
        }
        if (hmi == null) { Console.WriteLine("No HMI found."); return; }

        var logs = hmi.AlarmLogs;
        Console.WriteLine("existing alarm logs: " + logs.Count);
        foreach (var l in logs) Console.WriteLine("   - " + l.Name);
        if (revert) {
            string[] cls2 = { "ValveFault", "System", "ValveWarning", "ValveEvent" };
            foreach (var cn in cls2) {
                var c2 = hmi.AlarmClasses.Find(cn);
                if (c2 == null) continue;
                try { c2.Log = ""; Console.WriteLine("  cleared " + cn + ".Log -> '" + c2.Log + "'"); }
                catch (Exception ex) { Console.WriteLine("  [WARN] " + cn + ": " + Root(ex)); }
            }
            var dead = logs.Find(LOG_NAME);
            if (dead != null) { dead.Delete(); Console.WriteLine("  DELETED log " + LOG_NAME); }
            else Console.WriteLine("  (log not present)");
            proj.Save();
            Console.WriteLine("PROJECT SAVED (reverted)");
            return;
        }
        if (!apply) { Console.WriteLine("(dry run - pass --apply to create)"); return; }

        var log = logs.Find(LOG_NAME);
        if (log == null) {
            log = logs.Create(LOG_NAME);
            Console.WriteLine("CREATED alarm log: " + log.Name);
        } else Console.WriteLine("alarm log already exists: " + log.Name);

        // Storage: this device accepts removable media only (probed 2026-08-21 - None/Off/
        // Default/Local are all rejected by the MTP1500). SDX51 matches the audit trail and
        // Siemens' own recommendation of a SIMATIC SD card for panel data memory.
        var st = log.Settings;
        try {
            var sdP = st.GetType().GetProperty("StorageDevice");
            object before = sdP.GetValue(st, null);
            sdP.SetValue(st, Enum.Parse(sdP.PropertyType, "USBX61"), null);
            Console.WriteLine("  StorageDevice: " + before + " -> " + sdP.GetValue(st, null));
        } catch (Exception ex) { Console.WriteLine("  [WARN] StorageDevice: " + Root(ex)); }
        try { st.LogMaxSize = 20000; Console.WriteLine("  LogMaxSize   = " + st.LogMaxSize); }
        catch (Exception ex) { Console.WriteLine("  [WARN] LogMaxSize: " + Root(ex)); }

        // Move the audit trail onto the same medium so one USB stick serves both, and the
        // runtime raises one "medium not available" alarm instead of two.
        try {
            var atp2 = hmi.GetType().GetProperty("AuditTrails");
            var ac2 = atp2.GetValue(hmi, null) as System.Collections.IEnumerable;
            foreach (var o2 in ac2) {
                object s2 = o2.GetType().GetProperty("Settings").GetValue(o2, null);
                var sp2 = s2.GetType().GetProperty("StorageDevice");
                object b2 = sp2.GetValue(s2, null);
                sp2.SetValue(s2, Enum.Parse(sp2.PropertyType, "USBX61"), null);
                Console.WriteLine("  AuditTrail StorageDevice: " + b2 + " -> " + sp2.GetValue(s2, null));
            }
        } catch (Exception ex) { Console.WriteLine("  [WARN] audit trail storage: " + Root(ex)); }


        // Point every alarm class at the log. HmiAlarmClass.Log is a plain RW string.
        string[] classes = { "ValveFault", "System", "ValveWarning", "ValveEvent" };
        int ok = 0;
        foreach (var cn in classes) {
            var cls = hmi.AlarmClasses.Find(cn);
            if (cls == null) { Console.WriteLine("  [WARN] class not found: " + cn); continue; }
            try {
                cls.Log = LOG_NAME;
                string back = cls.Log;
                Console.WriteLine("  " + cn.PadRight(13) + " Log = " + back + (back == LOG_NAME ? "  OK" : "  DID NOT STICK"));
                if (back == LOG_NAME) ok++;
            } catch (Exception ex) { Console.WriteLine("  [WARN] " + cn + ".Log: " + Root(ex)); }
        }
        Console.WriteLine(ok + "/" + classes.Length + " classes wired to " + LOG_NAME);
        proj.Save();
        Console.WriteLine("PROJECT SAVED");
    }
    static string Root(Exception e) { while (e.InnerException != null) e = e.InnerException; return e.Message; }
    static HmiSoftware Find(DeviceItem it) {
        var c = it.GetService<SoftwareContainer>();
        if (c != null) { var h = c.Software as HmiSoftware; if (h != null) return h; }
        foreach (var sub in it.DeviceItems) { var r = Find(sub); if (r != null) return r; }
        return null;
    }
}
