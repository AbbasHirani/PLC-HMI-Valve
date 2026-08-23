// Reads the Audit Trail's retention configuration (Segment / Backup / Settings) from the live
// project. Deliberately does NOT touch hmi.HmiAlarmAuditClass - enumerating that composition
// took TIA Portal down mid-probe on 2026-08-23.
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

        foreach (var at in hmi.AuditTrails) {
            Console.WriteLine("=== Audit trail: " + at.Name + " ===");
            Dump(at, "", 0);
            break; // one trail is all this project has
        }
    }

    static void Dump(object obj, string prefix, int depth) {
        if (obj == null || depth > 2) return;
        foreach (var p in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (p.Name == "Parent") continue;
            object v;
            try { v = p.GetValue(obj, null); } catch { continue; }
            if (v == null) continue;
            var t = v.GetType();
            bool leaf = t.IsPrimitive || t.IsEnum || v is string || v is TimeSpan || v is DateTime || v is decimal;
            if (leaf) {
                Console.WriteLine(string.Format("  {0,-42} = {1}", prefix + p.Name, v));
            } else if (t.FullName != null && t.FullName.StartsWith("Siemens.")) {
                Console.WriteLine("  " + prefix + p.Name + ":");
                Dump(v, prefix + p.Name + ".", depth + 1);
            }
        }
    }

    static HmiSoftware FindHmiSoftware(Device device)
    { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
    static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
    { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
}
