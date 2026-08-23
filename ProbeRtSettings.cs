// Does the HMI device expose anything that turns the Audit Viewer / system navigation on?
// Read-only. Avoids hmi.HmiAlarmAuditClass, which took TIA down on 2026-08-23.
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

        Console.WriteLine("=== HmiSoftware top-level members ===");
        foreach (var p in hmi.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Console.WriteLine(string.Format("   {0,-34} : {1}", p.Name, p.PropertyType.Name));

        Console.WriteLine("\n=== RuntimeSettings (all scalar values) ===");
        Dump(hmi.RuntimeSettings, "   ", 0);

        Console.WriteLine("\n=== Screens present in project ===");
        foreach (var sc in hmi.Screens) Console.WriteLine("   " + sc.Name);
    }

    static void Dump(object o, string pad, int depth) {
        if (o == null || depth > 2) return;
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (p.Name == "Parent") continue;
            object v; try { v = p.GetValue(o, null); } catch { continue; }
            if (v == null) continue;
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || v is string || v is DateTime || v is TimeSpan)
                Console.WriteLine(pad + string.Format("{0,-34} = {1}", p.Name, v));
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
