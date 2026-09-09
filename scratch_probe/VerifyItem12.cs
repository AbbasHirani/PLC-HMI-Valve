// Read-only verification that item 12's three HMI-side changes actually landed in the project.
// Checks the objects themselves rather than trusting the build log, which prints no per-tag lines.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Base;

namespace VerifyItem12
{
    class Program
    {
        static int fails = 0;

        static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int idx = args.Name.IndexOf(',');
            string name = idx == -1 ? args.Name : args.Name.Substring(0, idx);
            string[] bases = {
                @"D:\Siemens\Portal V20\PublicAPI\V20",
                @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20"
            };
            foreach (var b in bases) { string p = Path.Combine(b, name + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        }

        static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try { Run(); }
            catch (Exception ex) { Console.WriteLine("[ERROR] " + ex.Message); Environment.ExitCode = 1; return; }
            Console.WriteLine();
            Console.WriteLine(fails == 0 ? "ALL CHECKS PASSED" : fails + " CHECK(S) FAILED");
            Environment.ExitCode = fails == 0 ? 0 : 1;
        }

        static void Check(string label, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + label);
            if (detail != null) Console.WriteLine("         " + detail);
            if (!ok) fails++;
        }

        static string GetStr(object o, string prop)
        {
            try
            {
                var p = o.GetType().GetProperty(prop);
                if (p == null) return null;
                var v = p.GetValue(o, null);
                if (v == null) return null;
                // EventText and friends are MultilingualText, whose ToString() is just the type
                // name. Walk its Items and return the first non-empty translation.
                var items = v.GetType().GetProperty("Items");
                if (items != null)
                {
                    var seq = items.GetValue(v, null) as System.Collections.IEnumerable;
                    if (seq != null)
                        foreach (var it in seq)
                        {
                            var tp = it.GetType().GetProperty("Text");
                            if (tp == null) continue;
                            var s = tp.GetValue(it, null) as string;
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    return "";
                }
                return v.ToString();
            }
            catch { return null; }
        }

        // Prints every readable property, so a wrong guess at a property name is visible
        // immediately instead of looking like a failed check.
        static void DumpProps(object o, string label)
        {
            Console.WriteLine("         --- " + label + " properties ---");
            foreach (var p in o.GetType().GetProperties())
            {
                if (p.GetIndexParameters().Length > 0) continue;
                string v;
                try { var raw = p.GetValue(o, null); v = raw == null ? "<null>" : raw.ToString(); }
                catch (Exception ex) { v = "<err: " + ex.GetType().Name + ">"; }
                if (v.Length > 90) v = v.Substring(0, 90) + "...";
                Console.WriteLine("         " + p.Name.PadRight(28) + " = " + v);
            }
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("TIA Portal not running."); Environment.ExitCode = 1; return; }
            Project project = null;
            foreach (var p in procs)
            {
                try { var a = p.Attach(); if (a != null && a.Projects.Count > 0) { project = a.Projects[0]; break; } }
                catch { }
            }
            if (project == null) { Console.WriteLine("No open project."); Environment.ExitCode = 1; return; }

            Device hmiDevice = project.Devices.FirstOrDefault(d => d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0);
            HmiSoftware hmi = FindHmi(hmiDevice);
            if (hmi == null) { Console.WriteLine("HMI software not found."); Environment.ExitCode = 1; return; }

            // ---- 1. the new tag exists and is bound to the right PLC member ------------
            Console.WriteLine("1. HMI tag Valves_DB_SelPositionFault");
            var table = hmi.TagTables.Find("ValveTags");
            if (table == null) Check("ValveTags table exists", false, null);
            else
            {
                var tag = table.Tags.Find("Valves_DB_SelPositionFault");
                Check("tag exists", tag != null, null);
                if (tag != null)
                {
                    if (Environment.GetEnvironmentVariable("DUMP") == "1") DumpProps(tag, "tag");

                    // PlcTag is the property that actually carries the binding. An earlier version
                    // of this probe scanned properties for one containing "SelHealthy" and matched
                    // Name first, so it asserted the tag was named correctly while claiming to
                    // have verified the binding - a false pass on the one thing most likely to be
                    // silently wrong (a tag created before the block compiled binds to nothing).
                    Check("PlcTag = Valves_DB.SelPositionFault",
                          GetStr(tag, "PlcTag") == "Valves_DB.SelPositionFault",
                          "PlcTag = '" + (GetStr(tag, "PlcTag") ?? "<null>") + "'");
                    Check("PlcName = PLC_1", GetStr(tag, "PlcName") == "PLC_1", "PlcName = " + GetStr(tag, "PlcName"));
                    Check("DataType = Bool", GetStr(tag, "DataType") == "Bool", "DataType = " + GetStr(tag, "DataType"));
                    Check("has a connection", !string.IsNullOrEmpty(GetStr(tag, "Connection")),
                          "Connection = " + GetStr(tag, "Connection"));

                    var sib = table.Tags.Find("Valves_DB_SelHealthy");
                    Check("sibling SelHealthy still bound", sib != null && GetStr(sib, "PlcTag") == "Valves_DB.SelHealthy",
                          sib == null ? "missing" : "PlcTag = " + GetStr(sib, "PlcTag"));
                }
            }

            // ---- 2. the _Unhealthy alarm text was sharpened ----------------------------
            Console.WriteLine();
            Console.WriteLine("2. _Unhealthy alarm text (checking V001, V045, V089)");
            foreach (string an in new[] { "V001_Unhealthy", "V045_Unhealthy", "V089_Unhealthy" })
            {
                var al = hmi.DiscreteAlarms.Find(an);
                if (al == null) { Check(an + " exists", false, null); continue; }
                string txt = GetStr(al, "EventText");
                bool ok = txt != null
                          && txt.IndexOf("actuator FAULT", StringComparison.Ordinal) >= 0
                          && txt.IndexOf("reported Unhealthy", StringComparison.Ordinal) < 0;
                Check(an, ok, "text = " + (txt ?? "<null>"));
            }

            // The double-indication alarm must still be there and still say what it says -
            // the whole point is that these two are now distinct.
            var di = hmi.DiscreteAlarms.Find("V001_DoubleInd");
            Check("V001_DoubleInd still distinct", di != null, di == null ? null : "text = " + GetStr(di, "EventText"));

            // ---- 3. the popup button lock reads the new tag ----------------------------
            Console.WriteLine();
            Console.WriteLine("3. Screen_Popup command-button lock script");
            HmiScreen pop = hmi.Screens.Find("Screen_Popup");
            if (pop == null) Check("Screen_Popup exists", false, null);
            else
            {
                int updated = 0, stale = 0;
                foreach (var item in pop.ScreenItems)
                {
                    foreach (var dyn in item.Dynamizations)
                    {
                        string code = GetStr(dyn, "ScriptCode");
                        if (code == null || code.IndexOf("SelHealthy", StringComparison.Ordinal) < 0) continue;
                        if (code.IndexOf("SelPositionFault", StringComparison.Ordinal) >= 0) updated++;
                        else { stale++; Console.WriteLine("         stale script on: " + GetStr(item, "Name")); }
                    }
                }
                Check("every SelHealthy script also reads SelPositionFault",
                      updated > 0 && stale == 0,
                      "scripts reading SelHealthy: " + (updated + stale) + ", updated: " + updated + ", stale: " + stale);
            }
        }

        static HmiSoftware FindHmi(Device d)
        {
            if (d == null) return null;
            foreach (DeviceItem it in d.DeviceItems) { var r = In(it); if (r != null) return r; }
            return null;
        }
        static HmiSoftware In(DeviceItem it)
        {
            var c = it.GetService<SoftwareContainer>();
            if (c != null && c.Software is HmiSoftware) return (HmiSoftware)c.Software;
            foreach (DeviceItem s in it.DeviceItems) { var r = In(s); if (r != null) return r; }
            return null;
        }
    }
}
