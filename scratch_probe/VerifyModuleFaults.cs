// Read-only: confirms the module-fault work landed in the project rather than trusting build logs.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.SW;

namespace VerifyModuleFaults
{
    class Program
    {
        static int fails = 0;
        static Assembly ResolveAssembly(object s, ResolveEventArgs a)
        {
            int i = a.Name.IndexOf(',');
            string n = i == -1 ? a.Name : a.Name.Substring(0, i);
            string[] bases = { @"D:\Siemens\Portal V20\PublicAPI\V20", @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20" };
            foreach (var b in bases) { string p = Path.Combine(b, n + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        }
        static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try { Run(); } catch (Exception ex) { Console.WriteLine("[ERROR] " + ex.Message); Environment.ExitCode = 1; return; }
            Console.WriteLine(fails == 0 ? "\nALL CHECKS PASSED" : "\n" + fails + " CHECK(S) FAILED");
            Environment.ExitCode = fails == 0 ? 0 : 1;
        }
        static void Check(string label, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + label + (detail == null ? "" : "   (" + detail + ")"));
            if (!ok) fails++;
        }
        static string P(object o, string name)
        {
            try { var p = o.GetType().GetProperty(name); if (p == null) return null; var v = p.GetValue(o, null); return v == null ? null : v.ToString(); }
            catch { return null; }
        }
        static T Soft<T>(DeviceItem it) where T : class
        {
            var c = it.GetService<SoftwareContainer>();
            if (c != null && c.Software is T) return c.Software as T;
            foreach (DeviceItem s in it.DeviceItems) { var r = Soft<T>(s); if (r != null) return r; }
            return null;
        }
        static T Find<T>(Project pr) where T : class
        {
            foreach (Device d in pr.Devices) foreach (DeviceItem it in d.DeviceItems) { var r = Soft<T>(it); if (r != null) return r; }
            return null;
        }
        static void Run()
        {
            Project pr = null;
            foreach (var p in TiaPortal.GetProcesses()) { var a = p.Attach(); if (a.Projects.Count > 0) { pr = a.Projects[0]; break; } }
            if (pr == null) throw new Exception("no project");
            var plc = Find<PlcSoftware>(pr); var hmi = Find<HmiSoftware>(pr);

            Console.WriteLine("PLC blocks");
            foreach (var n in new[] { "FC_ModuleEvent", "DiagnosticErrorInterrupt", "FB_ValveLoop", "Diag_DB" })
            {
                var b = plc.BlockGroup.Blocks.Find(n);
                Check(n + " present and consistent", b != null && b.IsConsistent, b == null ? "missing" : "number " + b.Number);
            }

            Console.WriteLine("Reset tag");
            var t = hmi.TagTables.Find("ValveTags").Tags.Find("Diag_ModuleFaultAck");
            Check("tag exists", t != null, null);
            if (t != null)
            {
                Check("bound to Diag_DB.ModuleFaultAck", P(t, "PlcTag") == "Diag_DB.ModuleFaultAck", P(t, "PlcTag"));
                Check("on a PLC connection", !string.IsNullOrEmpty(P(t, "Connection")), P(t, "Connection"));
                Check("audited (GmpRelevant)", P(t, "GmpRelevant") == "True", P(t, "GmpRelevant"));
            }

            Console.WriteLine("Fault-word tags");
            for (int w = 0; w < 3; w++)
            {
                var wt = hmi.TagTables.Find("ValveTags").Tags.Find("Diag_DB_W_ModuleFault_" + w);
                Check("word " + w, wt != null && P(wt, "PlcTag") == "Diag_DB.W_ModuleFault[" + w + "]", wt == null ? "missing" : P(wt, "PlcTag"));
            }

            Console.WriteLine("Alarms");
            int total = hmi.DiscreteAlarms.Count();
            Check("total is 754 (89x8 + 5 system + 36 modules + 1 unknown)", total == 754, total.ToString());
            Check("general System_IO_Module_Fault is gone", hmi.DiscreteAlarms.Find("System_IO_Module_Fault") == null, null);
            Check("Mod_AFT_S03 exists", hmi.DiscreteAlarms.Find("Mod_AFT_S03") != null, null);
            Check("Mod_Unknown exists", hmi.DiscreteAlarms.Find("Mod_Unknown") != null, null);

            Console.WriteLine("Button on Screen_SysDiag");
            var sc = hmi.Screens.Find("Screen_SysDiag");
            Check("screen exists", sc != null, null);
            var btn = sc.ScreenItems.Find("SD_ResetModules");
            Check("button exists", btn != null, null);
            if (btn != null)
            {
                // TIA stores the rule with a prefix, so compare with a button already using it.
                string sibling = null;
                foreach (var it in hmi.Screens.Find("Screen_Diagnostics").ScreenItems)
                {
                    var au = P(it, "Authorization");
                    if (!string.IsNullOrEmpty(au)) { sibling = P(it, "Name") + " = " + au; if (au == P(btn, "Authorization")) break; }
                }
                Check("login rule matches existing Operate buttons", sibling != null && sibling.EndsWith(" = " + P(btn, "Authorization")),
                      "this button = " + P(btn, "Authorization") + "; " + sibling);
                string script = "";
                var ehs = btn.GetType().GetProperty("EventHandlers").GetValue(btn, null) as IEnumerable;
                foreach (var eh in ehs)
                {
                    var sp = eh.GetType().GetProperty("Script"); if (sp == null) continue;
                    var so = sp.GetValue(eh, null); if (so == null) continue;
                    script += P(so, "ScriptCode") + P(so, "GlobalDefinitionAreaScriptCode");
                }
                Check("press writes the reset tag", script.Contains("Diag_ModuleFaultAck"), script.Trim());
            }
            foreach (var n in new[] { "SD_Matrix", "SD_Buffer", "SD_TabMatrix", "SD_TabBuffer" })
                Check(n + " still there", sc.ScreenItems.Find(n) != null, null);
        }
    }
}
