using System;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

class Program {
    static void Main() {
        var tia = TiaPortal.GetProcesses()[0].Attach();
        var proj = tia.Projects[0];
        HmiSoftware hmi = null;
        foreach (var d in proj.Devices) { foreach (var it in d.DeviceItems) { hmi = F(it); if (hmi!=null) break; } if (hmi!=null) break; }
        Console.WriteLine("discrete alarms: " + hmi.DiscreteAlarms.Count);
        int blank = 0, v001 = 0;
        foreach (var a in hmi.DiscreteAlarms) {
            string n = a.Name ?? "";
            if (n.Trim().Length == 0) { blank++; Console.WriteLine("  [BLANK NAME] class=" + a.AlarmClass); }
            if (n.StartsWith("V001_")) { v001++; Console.WriteLine("  " + n.PadRight(20) + " class=" + a.AlarmClass + " pri=" + a.Priority); }
        }
        Console.WriteLine("blank-named alarms: " + blank);
        Console.WriteLine("V001 alarms: " + v001);
        Console.WriteLine();
        Console.WriteLine("alarm classes and priorities:");
        foreach (var c in hmi.AlarmClasses) {
            string log = "";
            try { log = c.Log ?? ""; } catch {}
            Console.WriteLine("  " + c.Name.PadRight(26) + " pri=" + c.Priority + "  log='" + log + "'  system=" + c.IsSystem);
        }
    }
    static HmiSoftware F(DeviceItem it) {
        var c = it.GetService<SoftwareContainer>();
        if (c != null) { var h = c.Software as HmiSoftware; if (h != null) return h; }
        foreach (var s in it.DeviceItems) { var r = F(s); if (r != null) return r; }
        return null;
    }
}
