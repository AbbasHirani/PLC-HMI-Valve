using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

class Program {
    static void Main() {
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("No TIA Portal running."); return; }
        var tia = procs[0].Attach();
        var proj = tia.Projects[0];

        HmiSoftware hmi = null;
        foreach (var d in proj.Devices) {
            foreach (var it in d.DeviceItems) { hmi = FindHmi(it); if (hmi != null) break; }
            if (hmi != null) break;
        }
        if (hmi == null) { Console.WriteLine("No HMI found."); return; }

        Console.WriteLine("=== GRAPHIC COLLECTIONS ON HmiSoftware ===");
        DumpGraphicSources(hmi, "hmi");

        Console.WriteLine();
        Console.WriteLine("=== PROJECT-LEVEL GRAPHIC COLLECTIONS ===");
        DumpGraphicSources(proj, "project");

        Console.WriteLine();
        Console.WriteLine("=== WHAT EACH SCREEN'S MIMIC IS BOUND TO ===");
        foreach (var sc in hmi.Screens.OrderBy(s => s.Name)) {
            foreach (var item in sc.ScreenItems) {
                if (item.Name != "Dg_Sheet") continue;
                string g = "(unreadable)";
                try {
                    var p = item.GetType().GetProperty("Graphic");
                    if (p != null) { var v = p.GetValue(item, null); g = v == null ? "(null)" : v.ToString(); }
                } catch (Exception ex) { g = "ERR: " + ex.Message; }
                Console.WriteLine("  " + sc.Name.PadRight(24) + " Dg_Sheet.Graphic = '" + g + "'");
            }
        }
    }

    static void DumpGraphicSources(object owner, string label) {
        foreach (var p in owner.GetType().GetProperties()) {
            if (p.GetIndexParameters().Length > 0) continue;
            if (p.Name.IndexOf("Graphic", StringComparison.OrdinalIgnoreCase) < 0) continue;
            object val = null;
            try { val = p.GetValue(owner, null); } catch { continue; }
            if (val == null) { Console.WriteLine("  " + label + "." + p.Name + " = null"); continue; }
            var en = val as IEnumerable;
            if (en == null) { Console.WriteLine("  " + label + "." + p.Name + " = " + val); continue; }
            int n = 0;
            Console.WriteLine("  " + label + "." + p.Name + ":");
            foreach (var o in en) {
                string nm = o.ToString();
                try {
                    var np = o.GetType().GetProperty("Name");
                    if (np != null) nm = (string)np.GetValue(o, null);
                } catch {}
                Console.WriteLine("      [" + (++n) + "] " + nm);
                Recurse(o, 2);
            }
            if (n == 0) Console.WriteLine("      (empty)");
        }
    }

    // graphics often sit one folder deep (folder -> Graphics)
    static void Recurse(object folder, int depth) {
        if (depth > 3) return;
        foreach (var p in folder.GetType().GetProperties()) {
            if (p.GetIndexParameters().Length > 0) continue;
            if (p.Name != "Graphics" && p.Name != "Folders") continue;
            object val = null;
            try { val = p.GetValue(folder, null); } catch { continue; }
            var en = val as IEnumerable; if (en == null) continue;
            foreach (var o in en) {
                string nm = o.ToString();
                try { var np = o.GetType().GetProperty("Name"); if (np != null) nm = (string)np.GetValue(o, null); } catch {}
                Console.WriteLine(new string(' ', depth * 4) + "  - " + p.Name + ": " + nm);
                if (p.Name == "Folders") Recurse(o, depth + 1);
            }
        }
    }

    static HmiSoftware FindHmi(DeviceItem it) {
        var c = it.GetService<SoftwareContainer>();
        if (c != null) { var h = c.Software as HmiSoftware; if (h != null) return h; }
        foreach (var sub in it.DeviceItems) { var r = FindHmi(sub); if (r != null) return r; }
        return null;
    }
}
