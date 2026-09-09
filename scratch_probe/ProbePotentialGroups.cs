// Read-only: prints each ET200SP module in rack order with its potential-group / BaseUnit
// setting, so the "which modules share a 24V feed" question is answered from the project
// rather than from memory. Touches nothing, never saves.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;

namespace ProbePotentialGroups
{
    class Program
    {
        static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int i = args.Name.IndexOf(',');
            string n = i == -1 ? args.Name : args.Name.Substring(0, i);
            string[] bases = {
                @"D:\Siemens\Portal V20\PublicAPI\V20",
                @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20"
            };
            foreach (var b in bases) { string p = Path.Combine(b, n + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        }

        static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try { Run(); } catch (Exception ex) { Console.WriteLine("[ERROR] " + ex.Message); Environment.ExitCode = 1; }
        }

        static string Attr(IEngineeringObject eo, string name)
        {
            try { var v = eo.GetAttribute(name); return v == null ? null : v.ToString(); }
            catch { return null; }
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("TIA Portal not running."); return; }
            Project project = null;
            foreach (var p in procs)
            {
                try { var a = p.Attach(); if (a != null && a.Projects.Count > 0) { project = a.Projects[0]; break; } }
                catch { }
            }
            if (project == null) { Console.WriteLine("No open project."); return; }

            foreach (Device d in project.Devices)
            {
                if (d.Name.IndexOf("ET200SP", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Console.WriteLine();
                Console.WriteLine("==== " + d.Name + " ====");

                var mods = new List<DeviceItem>();
                foreach (DeviceItem it in d.DeviceItems) Collect(it, mods);
                // Rack order
                mods = mods.OrderBy(m => { try { return m.PositionNumber; } catch { return 999; } }).ToList();

                int group = 0;
                foreach (var m in mods)
                {
                    string name = m.Name;
                    if (name.IndexOf("DI ", StringComparison.Ordinal) < 0
                        && name.IndexOf("DQ ", StringComparison.Ordinal) < 0
                        && name.IndexOf("Server", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    string pg = Attr((IEngineeringObject)m, "PotentialGroup");
                    string bu = Attr((IEngineeringObject)m, "BaseUnit");
                    int pos = 0; try { pos = m.PositionNumber; } catch { }

                    // PotentialGroup = 1 means "this module is LIGHT and STARTS a new group".
                    // 0 means "DARK - uses the group of the module to its left".
                    string kind;
                    if (pg == "1" || pg == "True") { group++; kind = "LIGHT  <-- own 24V feed, starts group " + group; }
                    else if (pg == null) kind = "(no PotentialGroup attribute)";
                    else kind = "dark   -- shares feed of group " + (group == 0 ? "?" : group.ToString());

                    Console.WriteLine(string.Format("  slot {0,2}  {1,-26} PotentialGroup={2,-5} {3}",
                        pos, name, pg ?? "-", kind));
                    if (bu != null && bu.Length > 0)
                        Console.WriteLine("            BaseUnit = " + bu);
                }
                Console.WriteLine("  --> " + group + " separate 24V feed(s) on this station");
            }
        }

        static void Collect(DeviceItem it, List<DeviceItem> outp)
        {
            outp.Add(it);
            foreach (DeviceItem s in it.DeviceItems) Collect(s, outp);
        }
    }
}
