// Read-only. What PROFINET device name does the project expect for each station?
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;

class CheckPnNames {
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
    static readonly string[] Want = { "PnDeviceName", "PnDeviceNameAutoGeneration", "Address", "IpAddress", "MacAddress" };
    static void Main() {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("TIA not running."); return; }
        var proj = procs[0].Attach().Projects[0];
        foreach (Device d in proj.Devices) {
            Console.WriteLine("\n=== " + d.Name + " ===");
            foreach (DeviceItem it in d.DeviceItems) Walk(it, "  ");
        }
    }
    static void Walk(DeviceItem it, string ind) {
        try {
            var infos = it.GetAttributeInfos();
            var hits = infos.Where(i => Want.Any(w => i.Name.Equals(w, StringComparison.OrdinalIgnoreCase))).ToList();
            foreach (var i in hits) {
                object v = null;
                try { v = it.GetAttribute(i.Name); } catch { continue; }
                if (v == null || v.ToString() == "") continue;
                Console.WriteLine(ind + it.Name + " . " + i.Name + " = " + v);
            }
        } catch {}
        foreach (DeviceItem sub in it.DeviceItems) Walk(sub, ind + "  ");
    }
}
