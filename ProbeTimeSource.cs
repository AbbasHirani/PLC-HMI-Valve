// Where does the clock come from? Every audit record is stamped with it, and the integrity
// checksum protects a record's contents - not whether its timestamp is true. Scans the hardware
// configuration of both devices for anything time related: NTP, time synchronisation, time zone,
// daylight saving, and the master/slave role of the time-of-day service.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;

class Program {
    static readonly string[] Wanted = { "time", "ntp", "clock", "daylight", "zone", "sntp", "synchron" };

    static void Main() {
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
        var proj = procs[0].Attach().Projects[0];

        foreach (Device d in proj.Devices) {
            Console.WriteLine("\n######## DEVICE: " + d.Name + " ########");
            foreach (DeviceItem it in d.DeviceItems) Walk(it, 1);
        }
    }

    static void Walk(DeviceItem item, int depth) {
        if (depth > 4) return;
        var hits = new List<string>();
        try {
            // Attribute access lives on IEngineeringObject, not on DeviceItem itself.
            var eo = (IEngineeringObject)item;
            var infos = eo.GetAttributeInfos();
            foreach (var info in infos) {
                string nm = null;
                try { nm = info.Name; } catch { continue; }
                if (nm == null) continue;
                var low = nm.ToLowerInvariant();
                if (!Wanted.Any(w => low.Contains(w))) continue;
                object v = null;
                try { v = eo.GetAttribute(nm); } catch { continue; }
                hits.Add(string.Format("      {0,-44} = {1}", nm, v == null ? "(null)" : v.ToString()));
            }
        } catch { }

        if (hits.Count > 0) {
            Console.WriteLine("  [" + item.Name + "]");
            foreach (var h in hits) Console.WriteLine(h);
        }
        foreach (DeviceItem sub in item.DeviceItems) Walk(sub, depth + 1);
    }
}
