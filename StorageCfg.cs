using System;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

class Program {
    static bool doSet = false;
    static void Main(string[] args) {
        doSet = args.Any(a => a == "--set");
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("No TIA Portal running."); return; }
        var tia = procs[0].Attach();
        var proj = tia.Projects[0];
        HmiSoftware hmi = null;
        foreach (var d in proj.Devices) {
            foreach (var it in d.DeviceItems) { hmi = Find(it); if (hmi != null) break; }
            if (hmi != null) break;
        }
        if (hmi == null) { Console.WriteLine("No HMI found."); return; }

        var rsProp = hmi.GetType().GetProperty("RuntimeSettings");
        if (rsProp == null) { Console.WriteLine("No RuntimeSettings property."); return; }
        object rs = rsProp.GetValue(hmi, null);
        Console.WriteLine("=== RuntimeSettings tree (storage-relevant) ===");
        Walk(rs, "RuntimeSettings", 0);

        Console.WriteLine();
        Console.WriteLine("=== GMPEnabled / AuditTrails ===");
        var gmp = rs.GetType().GetProperty("GMPEnabled");
        Console.WriteLine("  GMPEnabled = " + (gmp == null ? "(no such property)" : "" + gmp.GetValue(rs, null)));
        var at = hmi.GetType().GetProperty("AuditTrails");
        if (at != null) {
            var coll = at.GetValue(hmi, null) as System.Collections.IEnumerable;
            int n = 0;
            if (coll != null) foreach (var o in coll) {
                n++;
                var nm = o.GetType().GetProperty("Name");
                Console.WriteLine("  AuditTrail: " + (nm != null ? nm.GetValue(o, null) : o));
                Walk(o, "    ", 1);
            }
            if (n == 0) Console.WriteLine("  (no audit trails - GMP is off)");
        }
        Console.WriteLine();
        Console.WriteLine("=== LOG COLLECTIONS ON HmiSoftware ===");
        foreach (var p2 in hmi.GetType().GetProperties()) {
            if (p2.GetIndexParameters().Length > 0) continue;
            if (p2.Name.IndexOf("Log", StringComparison.OrdinalIgnoreCase) < 0) continue;
            object v = null;
            try { v = p2.GetValue(hmi, null); } catch { continue; }
            var en = v as System.Collections.IEnumerable;
            Console.WriteLine("  hmi." + p2.Name + "  {" + (v==null?"null":v.GetType().Name) + "}");
            if (en == null) continue;
            int n = 0;
            foreach (var o in en) {
                n++;
                var nm = o.GetType().GetProperty("Name");
                Console.WriteLine("      - " + (nm != null ? nm.GetValue(o, null) : o));
                Walk(o, "        ", 1);
            }
            if (n == 0) Console.WriteLine("      (empty)");
        }
        if (doSet) {
            Console.WriteLine();
            Console.WriteLine("=== PROBING which storage media THIS DEVICE accepts ===");
            var atp = hmi.GetType().GetProperty("AuditTrails");
            var coll2 = atp.GetValue(hmi, null) as System.Collections.IEnumerable;
            foreach (var o in coll2) {
                object st = o.GetType().GetProperty("Settings").GetValue(o, null);
                var sdP = st.GetType().GetProperty("StorageDevice");
                object original = sdP.GetValue(st, null);
                Console.WriteLine("  original = " + original);
                foreach (var nm in Enum.GetNames(sdP.PropertyType)) {
                    try {
                        sdP.SetValue(st, Enum.Parse(sdP.PropertyType, nm), null);
                        object now = sdP.GetValue(st, null);
                        Console.WriteLine("    " + nm.PadRight(10) + " ACCEPTED  (reads back as " + now + ")");
                    } catch (Exception ex) {
                        var root = ex; while (root.InnerException != null) root = root.InnerException;
                        string m = root.Message.Replace((char)13, (char)32).Replace((char)10, (char)32);
                        if (m.Length > 90) m = m.Substring(0, 90);
                        Console.WriteLine("    " + nm.PadRight(10) + " REJECTED  " + m);
                    }
                }
                try { sdP.SetValue(st, original, null); Console.WriteLine("  restored to " + sdP.GetValue(st, null)); }
                catch (Exception ex) { Console.WriteLine("  [WARN] could not restore: " + ex.Message); }
            }
        }
        if (doSet) { proj.Save(); Console.WriteLine("\nPROJECT SAVED"); }
    }

    static void Walk(object o, string path, int depth) {
        if (o == null || depth > 3) return;
        foreach (var p in o.GetType().GetProperties()) {
            if (p.GetIndexParameters().Length > 0) continue;
            if (p.Name == "Parent") continue;
            object v = null;
            try { v = p.GetValue(o, null); } catch { continue; }
            bool storagey = p.Name.IndexOf("Storage", StringComparison.OrdinalIgnoreCase) >= 0
                         || p.Name.IndexOf("Logging", StringComparison.OrdinalIgnoreCase) >= 0
                         || p.Name.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0
                         || p.Name.IndexOf("Device", StringComparison.OrdinalIgnoreCase) >= 0
                         || p.Name.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0;
            if (v != null && v.GetType().Namespace != null && v.GetType().Namespace.StartsWith("Siemens")
                && !v.GetType().IsEnum) {
                Console.WriteLine(new string(' ', depth*2) + "  " + path + "." + p.Name + "  {" + v.GetType().Name + "}");
                Walk(v, path + "." + p.Name, depth + 1);
            } else if (storagey || depth > 0) {
                Console.WriteLine(new string(' ', depth*2) + "  " + path + "." + p.Name
                                  + " = " + (v == null ? "null" : v.ToString())
                                  + (p.CanWrite ? "   [RW]" : "   [R]")
                                  + (v != null && v.GetType().IsEnum ? "  opts: " + string.Join(",", Enum.GetNames(v.GetType())) : ""));
            }
        }
    }
    static HmiSoftware Find(DeviceItem it) {
        var c = it.GetService<SoftwareContainer>();
        if (c != null) { var h = c.Software as HmiSoftware; if (h != null) return h; }
        foreach (var sub in it.DeviceItems) { var r = Find(sub); if (r != null) return r; }
        return null;
    }
}
