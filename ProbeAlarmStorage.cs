// Can the alarm log go on the SD card, and can either log have a named folder?
//
// Both were refused earlier and recorded as Openness limits rather than Siemens limits. This
// digs properly: dumps the alarm log object in full looking for a main-database setting the
// earlier shallow scan missed, then tries StorageFolder and StorageDevice several ways and
// reports the exact refusal each time.
//
// Read-only unless --try is passed. With --try it changes the in-memory project and does NOT
// save; run SetAuditStorage.exe --report afterwards and revert if anything stuck.
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon;

class Program {
    static void Main(string[] args) {
        bool doTry = args.Any(a => a == "--try");
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
        var proj = procs[0].Attach().Projects[0];
        Device dev = null;
        foreach (var d in proj.Devices)
            if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) dev = d;
        var hmi = Find(dev);

        object alarm = null;
        foreach (var al in hmi.AlarmLogs) { alarm = al; break; }
        if (alarm == null) { Console.WriteLine("[ERROR] no alarm log"); return; }

        Console.WriteLine("=== AlarmLog_Main, full object tree ===");
        Dump(alarm, "  ", 0);

        if (!doTry) { Console.WriteLine("\n(read-only; pass --try to attempt the writes)"); return; }

        var settings = alarm.GetType().GetProperty("Settings").GetValue(alarm, null);

        Console.WriteLine("\n=== StorageFolder attempts ===");
        foreach (var v in new[] { "alarmLogLive", "/alarmLogLive", "\\alarmLogLive", "AlarmLive" })
            TrySet(settings, "StorageFolder", v);

        Console.WriteLine("\n=== StorageDevice attempts ===");
        TrySet(settings, "StorageDevice", DeviceNode.SDX51);

        Console.WriteLine("\n=== StorageFolder again, after the device attempt ===");
        TrySet(settings, "StorageFolder", "alarmLogLive");

        Console.WriteLine("\nNOTHING SAVED. Check with SetAuditStorage.exe --report.");
    }

    static void TrySet(object target, string prop, object value) {
        try {
            var p = target.GetType().GetProperty(prop);
            p.SetValue(target, value, null);
            object back = p.GetValue(target, null);
            Console.WriteLine(string.Format("  {0,-14} = {1,-16}  ACCEPTED, reads back '{2}'", prop, value, back));
        } catch (Exception ex) {
            while (ex.InnerException != null) ex = ex.InnerException;
            string m = ex.Message.Replace("\r", " ").Replace("\n", " ");
            if (m.Length > 150) m = m.Substring(0, 150);
            Console.WriteLine(string.Format("  {0,-14} = {1,-16}  REFUSED: {2}", prop, value, m));
        }
    }

    static void Dump(object o, string pad, int depth) {
        if (o == null || depth > 3) return;
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (p.Name == "Parent") continue;
            object v; try { v = p.GetValue(o, null); } catch { continue; }
            if (v == null) continue;
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || v is string || v is DateTime || v is TimeSpan)
                Console.WriteLine(pad + string.Format("{0,-24} = {1}   {2}", p.Name, v, p.CanWrite ? "(rw)" : "(ro)"));
            else if (t.FullName != null && t.FullName.StartsWith("Siemens.")) {
                Console.WriteLine(pad + p.Name + ":");
                Dump(v, pad + "   ", depth + 1);
            }
        }
    }

    static HmiSoftware Find(Device d)
    { foreach (DeviceItem i in d.DeviceItems) { var r = F2(i); if (r != null) return r; } return null; }
    static HmiSoftware F2(DeviceItem it)
    { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem s in it.DeviceItems) { var r = F2(s); if (r != null) return r; } return null; }
}
