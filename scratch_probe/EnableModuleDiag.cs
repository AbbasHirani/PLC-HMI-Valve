// Enables the module-level diagnostics that need NO field wiring change:
//   DI + DQ : DiagnosticsNoSupplyVoltage
//   DQ only : DiagnosticsShortCircuitToGround, DiagnosticsShortCircuitToLplus
//
// DELIBERATELY DOES NOT touch DiagnosticsWireBreak. Wire break requires a 25-45 kOhm resistor
// across every monitored contact (manual 6ES7131-6BH01-0BA0, p16). Enabling it without the
// resistors makes every open channel report a permanent wire break - the manual lists
// "channel not connected (open)" as a cause of that very fault. That is a field-wiring
// decision for the hardware team, not something to switch on from here.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;

namespace EnableModuleDiag
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

        static readonly string[] TO_ENABLE = {
            "DiagnosticsNoSupplyVoltage",
            "DiagnosticsShortCircuitToGround",
            "DiagnosticsShortCircuitToLplus"
        };

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            bool apply = args.Contains("--apply");
            try { Run(apply); } catch (Exception ex) { Console.WriteLine("[ERROR] " + ex.Message); Environment.ExitCode = 1; }
        }

        static void Run(bool apply)
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

            Console.WriteLine(apply ? "MODE: APPLY (will write and save)" : "MODE: DRY RUN (pass --apply to write)");
            Console.WriteLine();

            int changed = 0, already = 0, skipped = 0, failed = 0;

            foreach (Device d in project.Devices)
            {
                if (d.Name.IndexOf("ET200SP", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Console.WriteLine("==== " + d.Name + " ====");
                var mods = new List<DeviceItem>();
                foreach (DeviceItem it in d.DeviceItems) Collect(it, mods);

                foreach (var m in mods.OrderBy(x => { try { return x.PositionNumber; } catch { return 999; } }))
                {
                    if (m.Name.IndexOf("DI ", StringComparison.Ordinal) < 0
                        && m.Name.IndexOf("DQ ", StringComparison.Ordinal) < 0) continue;

                    var eo = (IEngineeringObject)m;
                    HashSet<string> have;
                    try { have = new HashSet<string>(eo.GetAttributeInfos().Select(a => a.Name)); }
                    catch { continue; }
                    if (!have.Contains("DiagnosticsNoSupplyVoltage")) continue;

                    int pos = 0; try { pos = m.PositionNumber; } catch { }
                    var notes = new List<string>();

                    foreach (var attr in TO_ENABLE)
                    {
                        if (!have.Contains(attr)) continue;
                        object cur = null;
                        try { cur = eo.GetAttribute(attr); } catch { }
                        if (cur is bool && (bool)cur) { already++; notes.Add(attr + "=already ON"); continue; }
                        if (!apply) { changed++; notes.Add(attr + ": False -> True"); continue; }
                        try
                        {
                            eo.SetAttribute(attr, true);
                            object back = eo.GetAttribute(attr);
                            if (back is bool && (bool)back) { changed++; notes.Add(attr + " -> True"); }
                            else { failed++; notes.Add(attr + " WRITE DID NOT STICK (reads " + back + ")"); }
                        }
                        catch (Exception ex) { failed++; notes.Add(attr + " FAILED: " + ex.GetType().Name + " " + ex.Message); }
                    }

                    // Explicitly report wire break as left alone, so it is visible that it was a
                    // decision and not an oversight.
                    if (have.Contains("DiagnosticsWireBreak"))
                    {
                        object wb = null; try { wb = eo.GetAttribute("DiagnosticsWireBreak"); } catch { }
                        notes.Add("DiagnosticsWireBreak=" + wb + " (LEFT ALONE - needs field resistors)");
                        skipped++;
                    }

                    Console.WriteLine(string.Format("  slot {0,2}  {1,-24}", pos, m.Name));
                    foreach (var n in notes) Console.WriteLine("            " + n);
                }
            }

            Console.WriteLine();
            Console.WriteLine("changed=" + changed + "  already=" + already + "  wireBreakLeftAlone=" + skipped + "  failed=" + failed);

            if (apply && failed == 0 && changed > 0)
            {
                Console.WriteLine("Saving project...");
                project.Save();
                Console.WriteLine("Saved.");
            }
            else if (apply && failed > 0)
            {
                Console.WriteLine("FAILURES PRESENT - project NOT saved.");
                Environment.ExitCode = 1;
            }
        }

        static void Collect(DeviceItem it, List<DeviceItem> outp)
        {
            outp.Add(it);
            foreach (DeviceItem s in it.DeviceItems) Collect(s, outp);
        }
    }
}
