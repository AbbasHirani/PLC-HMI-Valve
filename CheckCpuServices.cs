// Read-only. Which network services is the CPU actually exposing? Item 41 context.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;

class CheckCpuServices {
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
    static readonly string[] Needles = { "Web", "Opc", "Put", "Get", "Snmp", "Protect",
                                         "Access", "Password", "Security", "Certificate", "Tls" };
    static void Main() {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("TIA not running."); return; }
        var proj = procs[0].Attach().Projects[0];
        foreach (Device d in proj.Devices) foreach (DeviceItem it in d.DeviceItems) Walk(it);
        Console.WriteLine("\nRead-only. Nothing written.");
    }
    static void Walk(DeviceItem it) {
        try {
            var infos = it.GetAttributeInfos();
            var hits = infos.Where(i => Needles.Any(n => i.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                            .OrderBy(i => i.Name).ToList();
            if (hits.Count > 0) {
                Console.WriteLine("\n=== " + it.Name + " ===");
                foreach (var i in hits) {
                    object v = "?";
                    try { v = it.GetAttribute(i.Name); } catch (Exception e) { v = "<" + e.GetType().Name + ">"; }
                    Console.WriteLine(string.Format("  {0,-46} {1}", i.Name, v));
                }
            }
        } catch {}
        foreach (DeviceItem sub in it.DeviceItems) Walk(sub);
    }
}
