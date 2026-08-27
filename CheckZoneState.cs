// Read-only. After the TIA crash on 2026-08-27, establishes which generation of work is
// actually in the project: the zone-screen rebuild (station-offline banner), the rejected
// legend strip, and the home-screen titles. Attaches, reads, prints. Never writes, never saves.
using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

class CheckZoneState
{
    static string[] Bases = {
        @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20",
        @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI",
        @"C:\Program Files\Siemens\Automation\Portal V20\Bin"
    };

    static Assembly Resolve(object s, ResolveEventArgs a) {
        int i = a.Name.IndexOf(',');
        string n = (i == -1 ? a.Name : a.Name.Substring(0, i)) + ".dll";
        foreach (var b in Bases) { var p = Path.Combine(b, n); if (File.Exists(p)) return Assembly.LoadFrom(p); }
        return null;
    }

    static HmiSoftware FindHmi(DeviceItem it) {
        var c = it.GetService<SoftwareContainer>();
        if (c != null && c.Software is HmiSoftware) return (HmiSoftware)c.Software;
        foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmi(sub); if (r != null) return r; }
        return null;
    }

    static void Main() {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        var procs = TiaPortal.GetProcesses();
        Console.WriteLine("TIA processes found: " + procs.Count);
        if (procs.Count == 0) { Console.WriteLine("RESULT: TIA not running."); return; }

        var tia = procs[0].Attach();
        if (tia.Projects.Count == 0) { Console.WriteLine("RESULT: TIA running but NO PROJECT OPEN."); return; }
        var proj = tia.Projects[0];
        Console.WriteLine("Project: " + proj.Name);
        try { Console.WriteLine("Project path: " + proj.Path); } catch {}
        try { Console.WriteLine("IsModified: " + proj.IsModified); } catch (Exception e) { Console.WriteLine("IsModified: n/a (" + e.GetType().Name + ")"); }

        HmiSoftware hmi = null;
        foreach (Device d in proj.Devices) {
            foreach (DeviceItem it in d.DeviceItems) { hmi = FindHmi(it); if (hmi != null) break; }
            if (hmi != null) break;
        }
        if (hmi == null) { Console.WriteLine("RESULT: no HmiSoftware found."); return; }

        Console.WriteLine("\n{0,-22} {1,6} {2,8} {3,8} {4,8} {5,8}",
                          "SCREEN", "ITEMS", "LegR_*", "Banner", "Nav_7", "Titles");
        Console.WriteLine(new string('-', 68));

        foreach (var sc in hmi.Screens) {
            int total = 0, legr = 0; bool banner = false, nav7 = false, titles = false;
            foreach (var item in sc.ScreenItems) {
                total++;
                string n = item.Name;
                if (n.StartsWith("LegR_", StringComparison.Ordinal)) legr++;
                if (n == "Zn_LostBar" || n == "Zn_LostTxt") banner = true;
                if (n == "Nav_7") nav7 = true;
                if (n == "Home_TtlBal" || n == "Home_TtlBlg") titles = true;
            }
            Console.WriteLine("{0,-22} {1,6} {2,8} {3,8} {4,8} {5,8}",
                              sc.Name, total, legr,
                              banner ? "YES" : "-", nav7 ? "YES" : "-", titles ? "YES" : "-");
        }

        Console.WriteLine("\nKey:");
        Console.WriteLine("  LegR_*  = rejected legend strip (want 0; 12 means strip never ran)");
        Console.WriteLine("  Banner  = station-offline banner (zone screens; proves the rebuild survived)");
        Console.WriteLine("  Nav_7   = 8th nav button / DIAGNOSTICS (proves the nav patch survived)");
        Console.WriteLine("  Titles  = Home BALLAST/BILGE corner titles");
        Console.WriteLine("\nRead-only. Nothing written, nothing saved.");
    }
}
