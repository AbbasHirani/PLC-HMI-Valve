// Read-only. Reports the PLC protection level and the project's roles / function rights,
// so item 41 starts from measured state rather than assumption. Writes nothing.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;

class CheckSecurity {
    static string[] Bases = {
        @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20",
        @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI",
        @"C:\Program Files\Siemens\Automation\Portal V20\Bin" };

    static Assembly Resolve(object s, ResolveEventArgs a) {
        int i = a.Name.IndexOf(',');
        string n = (i == -1 ? a.Name : a.Name.Substring(0, i)) + ".dll";
        foreach (var b in Bases) { var p = Path.Combine(b, n); if (File.Exists(p)) return Assembly.LoadFrom(p); }
        return null;
    }

    static void Main() {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("TIA not running."); return; }
        var tia = procs[0].Attach();
        if (tia.Projects.Count == 0) { Console.WriteLine("No project open."); return; }
        var proj = tia.Projects[0];
        Console.WriteLine("Project: " + proj.Name + "   IsModified: " + proj.IsModified);

        Console.WriteLine("\n=== PLC protection level ===");
        foreach (Device d in proj.Devices)
            foreach (DeviceItem it in d.DeviceItems) Walk(it, "");

        Console.WriteLine("\n=== Project roles / function rights ===");
        // Reach them by reflection: the exact composition name varies by version and guessing
        // wrong here would report "none" for something that exists.
        foreach (var p in proj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            string n = p.Name;
            if (n.IndexOf("Role", StringComparison.OrdinalIgnoreCase) < 0 &&
                n.IndexOf("User", StringComparison.OrdinalIgnoreCase) < 0 &&
                n.IndexOf("Security", StringComparison.OrdinalIgnoreCase) < 0) continue;
            object v = null;
            try { v = p.GetValue(proj, null); } catch (Exception e) { Console.WriteLine("  " + n + " -> " + e.GetType().Name); continue; }
            Console.WriteLine("  Project." + n + " : " + p.PropertyType.Name);
            var en = v as System.Collections.IEnumerable;
            if (en == null) continue;
            foreach (var o in en) {
                string nm = "?";
                try { nm = (string)o.GetType().GetProperty("Name").GetValue(o, null); } catch {}
                Console.WriteLine("      - " + nm + "   [" + o.GetType().Name + "]");
            }
        }
        Console.WriteLine("\nRead-only. Nothing written.");
    }

    static void Walk(DeviceItem it, string indent) {
        try {
            var prov = it.GetService<PlcAccessLevelProvider>();
            if (prov != null)
                Console.WriteLine("  " + it.Name + "  ->  " + prov.PlcProtectionAccessLevel);
        } catch {}
        foreach (DeviceItem sub in it.DeviceItems) Walk(sub, indent + "  ");
    }
}
