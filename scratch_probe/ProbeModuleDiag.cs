// Read-only: dumps the diagnostics-related parameters on every ET200SP I/O module, so we know
// what is currently enabled and whether Openness can set them. Touches nothing, never saves.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;

namespace ProbeModuleDiag
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

            bool first = true;
            foreach (Device d in project.Devices)
            {
                if (d.Name.IndexOf("ET200SP", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Console.WriteLine();
                Console.WriteLine("==== " + d.Name + " ====");
                var mods = new List<DeviceItem>();
                foreach (DeviceItem it in d.DeviceItems) Collect(it, mods);

                foreach (var m in mods)
                {
                    if (m.Name.IndexOf("DI ", StringComparison.Ordinal) < 0
                        && m.Name.IndexOf("DQ ", StringComparison.Ordinal) < 0) continue;

                    var eo = (IEngineeringObject)m;
                    List<Siemens.Engineering.EngineeringAttributeInfo> infos;
                    try { infos = eo.GetAttributeInfos().ToList(); }
                    catch { continue; }

                    var diag = infos.Where(a =>
                        a.Name.IndexOf("Diag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        a.Name.IndexOf("WireBreak", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        a.Name.IndexOf("Supply", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        a.Name.IndexOf("Voltage", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    if (diag.Count == 0) continue;

                    if (first)
                    {
                        // Print the full attribute list once so we can see what else is there.
                        Console.WriteLine("  [full attribute list for " + m.Name + "]");
                        foreach (var a in infos.OrderBy(x => x.Name)) Console.WriteLine("      " + a.Name);
                        Console.WriteLine();
                        first = false;
                    }

                    Console.WriteLine("  " + m.Name);
                    foreach (var a in diag)
                    {
                        string v;
                        try { var raw = eo.GetAttribute(a.Name); v = raw == null ? "<null>" : raw.ToString(); }
                        catch (Exception ex) { v = "<err " + ex.GetType().Name + ">"; }
                        Console.WriteLine("      " + a.Name.PadRight(38) + " = " + v);
                    }
                }
            }
        }

        static void Collect(DeviceItem it, List<DeviceItem> outp)
        {
            outp.Add(it);
            foreach (DeviceItem s in it.DeviceItems) Collect(s, outp);
        }
    }
}
