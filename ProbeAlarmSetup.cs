// What alarm infrastructure already exists? An operator-action log has to be built on the alarm
// system, because HMI scripts cannot read the audit SQLite database - the only control that can
// render a table of timestamped events is HmiAlarmControl.
using System;
using System.Linq;
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

        Console.WriteLine("=== Alarm classes ===");
        foreach (var ac in hmi.AlarmClasses) {
            string line = "   " + ac.Name;
            foreach (var pn in new[] { "Priority", "AcknowledgementMode", "LogOperatorActions", "AuditClass" }) {
                var p = ac.GetType().GetProperty(pn);
                if (p == null) continue;
                object v; try { v = p.GetValue(ac, null); } catch { continue; }
                if (v != null) line += "  " + pn + "=" + v;
            }
            Console.WriteLine(line);
        }

        Console.WriteLine("\n=== Discrete alarms ===");
        int n = 0; string first = null, last = null;
        foreach (var da in hmi.DiscreteAlarms) {
            n++;
            if (first == null) first = da.Name;
            last = da.Name;
        }
        Console.WriteLine("   count = " + n);
        Console.WriteLine("   first = " + first);
        Console.WriteLine("   last  = " + last);

        // Property surface of one alarm - shows what we can set when generating new ones
        foreach (var da in hmi.DiscreteAlarms) {
            Console.WriteLine("\n=== Properties of '" + da.Name + "' ===");
            foreach (var p in da.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if (p.Name == "Parent") continue;
                object v; try { v = p.GetValue(da, null); } catch { continue; }
                Console.WriteLine(string.Format("   {0,-26} : {1,-26} = {2}",
                    p.Name, p.PropertyType.Name, v == null ? "(null)" : v.ToString()));
            }
            break;
        }

        Console.WriteLine("\n=== System tags (looking for a current-user tag) ===");
        foreach (var st in hmi.SystemTags) {
            var nm = st.Name.ToLowerInvariant();
            if (nm.Contains("user") || nm.Contains("login")) Console.WriteLine("   " + st.Name);
        }
    }

    static HmiSoftware FindHmiSoftware(Device device)
    { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
    static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
    { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
}
