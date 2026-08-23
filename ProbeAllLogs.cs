// Dumps every log collection's storage/backup config. The alarm log already writes successfully,
// so whatever it uses is a known-good reference for path format and device selection.
// Avoids hmi.HmiAlarmAuditClass - enumerating that crashed TIA on 2026-08-23.
using System;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.HmiUnified;

class Program {
    static void Main() {
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
        var proj = procs[0].Attach().Projects[0];
        Device hmiDevice = null;
        foreach (var d in proj.Devices)
            if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) hmiDevice = d;
        var hmi = FindHmiSoftware(hmiDevice);

        // Which log collections does HmiSoftware expose?
        foreach (var p in hmi.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            var n = p.Name.ToLowerInvariant();
            if (!(n.Contains("log") || n.Contains("audit"))) continue;
            if (p.Name == "HmiAlarmAuditClass") { Console.WriteLine("(skipping HmiAlarmAuditClass - crashes TIA)"); continue; }

            object coll;
            try { coll = p.GetValue(hmi, null); } catch (Exception e) { Console.WriteLine(p.Name + ": ERR " + e.Message); continue; }
            if (coll == null) continue;
            Console.WriteLine("\n########## " + p.Name + " ##########");
            var en = coll as System.Collections.IEnumerable;
            if (en == null) { Console.WriteLine("  (not enumerable)"); continue; }
            foreach (var item in en) {
                var nameProp = item.GetType().GetProperty("Name");
                Console.WriteLine("  --- " + (nameProp != null ? nameProp.GetValue(item, null) : item.GetType().Name));
                Dump(item, "      ", 0);
            }
        }
    }

    static void Dump(object obj, string pad, int depth) {
        if (obj == null || depth > 2) return;
        foreach (var p in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (p.Name == "Parent" || p.Name == "Name") continue;
            object v;
            try { v = p.GetValue(obj, null); } catch { continue; }
            if (v == null) continue;
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || v is string || v is DateTime || v is TimeSpan)
                Console.WriteLine(pad + string.Format("{0,-20} = {1}", p.Name, v));
            else if (t.FullName != null && t.FullName.StartsWith("Siemens.")) {
                Console.WriteLine(pad + p.Name + ":");
                Dump(v, pad + "   ", depth + 1);
            }
        }
    }

    static HmiSoftware FindHmiSoftware(Device device)
    { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
    static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
    { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
}
