using System;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;

class Program {
    static void Main() {
        var tia = TiaPortal.GetProcesses()[0].Attach();
        var proj = tia.Projects[0];
        Device hmiDevice = null;
        foreach (var d in proj.Devices) if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) hmiDevice = d;
        var hmi = FindHmiSoftware(hmiDevice);
        Console.WriteLine("HMI device: " + hmiDevice.Name);
        Console.WriteLine("GMPEnabled: " + hmi.RuntimeSettings.GMPEnabled);

        // --- Audit trail log objects ---
        Console.WriteLine("\n=== AuditTrails ===");
        try {
            var ats = hmi.AuditTrails;
            int n = 0;
            foreach (var at in ats) {
                n++;
                Console.WriteLine("  [" + n + "] " + at.GetType().Name);
                foreach (var p in at.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    object v = null;
                    try { v = p.GetValue(at, null); } catch { continue; }
                    if (v == null) continue;
                    string s = v.ToString();
                    if (s.Length > 90) s = s.Substring(0, 90) + "...";
                    Console.WriteLine(string.Format("        {0,-28} = {1}", p.Name, s));
                }
            }
            if (n == 0) Console.WriteLine("  (none configured)");
        } catch (Exception e) { Console.WriteLine("  ERR " + e.Message); }

        // --- Alarm audit classes ---
        Console.WriteLine("\n=== HmiAlarmAuditClass ===");
        try {
            int n = 0;
            foreach (var ac in hmi.HmiAlarmAuditClass) {
                n++;
                var props = ac.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                string line = "  [" + n + "]";
                foreach (var p in props) {
                    object v = null;
                    try { v = p.GetValue(ac, null); } catch { continue; }
                    if (v == null) continue;
                    line += "  " + p.Name + "=" + v;
                }
                Console.WriteLine(line);
            }
            if (n == 0) Console.WriteLine("  (none)");
        } catch (Exception e) { Console.WriteLine("  ERR " + e.Message); }

        // --- Tag survey ---
        Console.WriteLine("\n=== Tag tables / GmpRelevant survey ===");
        int total = 0, gmp = 0;
        var samples = new System.Collections.Generic.List<string>();
        foreach (var tt in hmi.TagTables) {
            int inTable = 0;
            foreach (HmiTag t in tt.Tags) {
                total++; inTable++;
                bool g = false;
                try { g = t.GmpRelevant; } catch { }
                if (g) gmp++;
                if (samples.Count < 12 &&
                    (t.Name.EndsWith("_OpenCmd") || t.Name.EndsWith("_CloseCmd") || t.Name.EndsWith("_Configured")))
                    samples.Add(string.Format("    {0,-24} table={1,-18} Gmp={2} Confirm={3}",
                        t.Name, tt.Name, g, SafeConfirm(t)));
            }
            Console.WriteLine(string.Format("  table {0,-24} tags={1}", tt.Name, inTable));
        }
        Console.WriteLine("  TOTAL tags = " + total + "   GmpRelevant=true count = " + gmp);
        Console.WriteLine("\n  Sample command tags:");
        foreach (var s in samples) Console.WriteLine(s);
    }

    static string SafeConfirm(HmiTag t) { try { return t.ConfirmationType.ToString(); } catch { return "?"; } }

    static HmiSoftware FindHmiSoftware(Device device)
    { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
    static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
    { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
}
