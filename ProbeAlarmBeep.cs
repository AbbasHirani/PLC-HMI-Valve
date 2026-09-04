using System;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;

// Item 38 step 4. The builder's own source no longer contains the localhost beep, but
// Screen_Alarms is only rebuilt by the slow --only=Alarms pass, so the dead code may still be
// LIVE in the project. This reads what is actually there.
class Program {
    // Openness will not initialise unless dependent assemblies resolve from the TIA install.
    // Register the handler BEFORE any Siemens type is touched - hence Main doing nothing but
    // wiring it up and calling Run(), which the JIT compiles only once it is actually invoked.
    // Without this the probe dies with "Cannot load assembly. Check your openness environment",
    // which is what happens on any machine where TIA is not in the default C:\Program Files path.
    static void Main() {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Run();
    }

    static System.Reflection.Assembly Resolve(object sender, ResolveEventArgs args) {
        int i = args.Name.IndexOf(',');
        string n = i == -1 ? args.Name : args.Name.Substring(0, i);
        foreach (var d in Dirs()) {
            string p = Path.Combine(d, n + ".dll");
            if (File.Exists(p)) return System.Reflection.Assembly.LoadFrom(p);
        }
        return null;
    }

    static string[] Dirs() {
        var dirs = new List<string>();
        string env = Environment.GetEnvironmentVariable("VALVEDEMO_OPENNESS");
        if (!string.IsNullOrEmpty(env)) dirs.Add(env);
        try {
            foreach (var proc in Process.GetProcessesByName("Siemens.Automation.Portal")) {
                try {
                    string bin  = Path.GetDirectoryName(proc.MainModule.FileName);
                    string root = Path.GetDirectoryName(bin);
                    dirs.Add(Path.Combine(root, @"PublicAPI\V20"));
                    dirs.Add(Path.Combine(root, @"Bin\PublicAPI"));
                    dirs.Add(bin);
                } catch { }
            }
        } catch { }
        foreach (var root in new[] { @"D:\Siemens\Portal V20",
                                     @"C:\Program Files\Siemens\Automation\Portal V20" }) {
            dirs.Add(Path.Combine(root, @"PublicAPI\V20"));
            dirs.Add(Path.Combine(root, @"Bin\PublicAPI"));
            dirs.Add(Path.Combine(root, "Bin"));
        }
        return dirs.ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run() {
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("TIA not running"); return; }
        var portal = procs[0].Attach();
        var proj = portal.Projects[0];
        Console.WriteLine("Project: " + proj.Name);

        HmiSoftware hmi = null;
        foreach (var d in proj.Devices) { foreach (var it in d.DeviceItems) { hmi = F(it); if (hmi!=null) break; } if (hmi!=null) break; }
        if (hmi == null) { Console.WriteLine("no HMI"); return; }

        HmiScreen sc = null;
        foreach (HmiScreen s in hmi.Screens) if (s.Name == "Screen_Alarms") { sc = s; break; }
        if (sc == null) { Console.WriteLine("Screen_Alarms not found"); return; }

        Console.WriteLine("=== Screen_Alarms: scripts mentioning beep / fetch / setInterval ===");
        int hits = 0, scanned = 0;
        foreach (var item in sc.ScreenItems) {
            scanned++;
            foreach (var pair in Scripts(item)) {
                string code = pair.Item2 ?? "";
                bool bad = code.IndexOf("beep", StringComparison.OrdinalIgnoreCase) >= 0
                        || code.IndexOf("8081") >= 0
                        || code.IndexOf("setInterval", StringComparison.OrdinalIgnoreCase) >= 0
                        || code.IndexOf("fetch(", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!bad) continue;
                hits++;
                Console.WriteLine();
                Console.WriteLine("  ITEM: " + item.Name + "   PROPERTY: " + pair.Item1);
                foreach (var line in code.Replace("\r","").Split('\n'))
                    Console.WriteLine("      | " + line);
            }
            // event handler scripts too (buttons)
            foreach (var pair in EventScripts(item)) {
                string code = pair.Item2 ?? "";
                if (code.IndexOf("beep", StringComparison.OrdinalIgnoreCase) < 0
                 && code.IndexOf("8081") < 0
                 && code.IndexOf("setInterval", StringComparison.OrdinalIgnoreCase) < 0) continue;
                hits++;
                Console.WriteLine();
                Console.WriteLine("  ITEM: " + item.Name + "   EVENT: " + pair.Item1);
                foreach (var line in code.Replace("\r","").Split('\n'))
                    Console.WriteLine("      | " + line);
            }
        }
        Console.WriteLine();
        Console.WriteLine("  screen items scanned: " + scanned);
        Console.WriteLine("  scripts containing beep/fetch/setInterval/8081: " + hits);
        if (hits == 0) Console.WriteLine("  => the localhost beep is NOT present in the live project.");
    }

    static System.Collections.Generic.List<Tuple<string,string>> Scripts(object item) {
        var res = new System.Collections.Generic.List<Tuple<string,string>>();
        try {
            var dp = item.GetType().GetProperty("Dynamizations");
            if (dp == null) return res;
            var dyns = dp.GetValue(item, null) as IEnumerable;
            if (dyns == null) return res;
            foreach (var d in dyns) {
                string prop = "?";
                try { prop = (d.GetType().GetProperty("PropertyName") ?? d.GetType().GetProperty("Name")).GetValue(d, null) as string; } catch {}
                var scp = d.GetType().GetProperty("ScriptCode");
                if (scp == null) continue;
                res.Add(Tuple.Create(prop, scp.GetValue(d, null) as string));
            }
        } catch {}
        return res;
    }

    static System.Collections.Generic.List<Tuple<string,string>> EventScripts(object item) {
        var res = new System.Collections.Generic.List<Tuple<string,string>>();
        try {
            var ep = item.GetType().GetProperty("EventHandlers");
            if (ep == null) return res;
            var evs = ep.GetValue(item, null) as IEnumerable;
            if (evs == null) return res;
            foreach (var e in evs) {
                string nm = "?";
                try { nm = (e.GetType().GetProperty("Name")).GetValue(e, null) as string; } catch {}
                try {
                    object scriptObj = e.GetType().GetProperty("Script").GetValue(e, null);
                    var scp = scriptObj.GetType().GetProperty("ScriptCode");
                    res.Add(Tuple.Create(nm, scp.GetValue(scriptObj, null) as string));
                } catch {}
            }
        } catch {}
        return res;
    }

    static HmiSoftware F(DeviceItem it) {
        var c = it.GetService<SoftwareContainer>();
        if (c != null) { var h = c.Software as HmiSoftware; if (h != null) return h; }
        foreach (var s in it.DeviceItems) { var r = F(s); if (r != null) return r; }
        return null;
    }
}
