// Read-only: for each ET200SP, prints the PROFINET device number and every I/O module's
// slot + hardware identifier. This is what OB82's LOG2GEO result has to be matched against,
// so it needs to come from the project, not from assumption. Touches nothing, never saves.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;

namespace ProbeGeoAddr
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

        static string A(IEngineeringObject eo, string name)
        {
            try { var v = eo.GetAttribute(name); return v == null ? null : v.ToString(); }
            catch { return null; }
        }

        // Hunt for any attribute whose name hints at a device/station number or HW identifier.
        static void ShowMatching(IEngineeringObject eo, string indent, params string[] hints)
        {
            try
            {
                foreach (var ai in eo.GetAttributeInfos())
                    foreach (var h in hints)
                        if (ai.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Console.WriteLine(indent + ai.Name.PadRight(30) + " = " + (A(eo, ai.Name) ?? "<null>"));
                            break;
                        }
            }
            catch { }
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
                Console.WriteLine("======== " + d.Name + " ========");

                var all = new List<DeviceItem>();
                foreach (DeviceItem it in d.DeviceItems) Collect(it, all);

                // The PROFINET interface carries the device number / node info.
                foreach (var it in all)
                {
                    if (it.Name.IndexOf("PROFINET", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine("  [interface] " + it.Name);
                    ShowMatching((IEngineeringObject)it, "      ", "Number", "Identifier", "Address", "Node");
                    try
                    {
                        var ni = it.GetService<Siemens.Engineering.HW.Features.NetworkInterface>();
                        if (ni != null)
                        {
                            Console.WriteLine("      -- NetworkInterface --");
                            ShowMatching((IEngineeringObject)ni, "      ", "Number", "Device", "Name");
                            foreach (var node in ni.Nodes)
                            {
                                Console.WriteLine("      -- Node --");
                                ShowMatching((IEngineeringObject)node, "      ", "Address", "Number", "Name");
                            }
                        }
                    }
                    catch (Exception ex) { Console.WriteLine("      (NetworkInterface: " + ex.GetType().Name + ")"); }
                }

                Console.WriteLine("  [modules by rack slot]");
                foreach (var m in all.OrderBy(x => { try { return x.PositionNumber; } catch { return 999; } }))
                {
                    if (m.Name.IndexOf("DI ", StringComparison.Ordinal) < 0
                        && m.Name.IndexOf("DQ ", StringComparison.Ordinal) < 0
                        && m.Name.IndexOf("Server", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int pos = 0; try { pos = m.PositionNumber; } catch { }
                    if (pos == 0) continue;
                    string hwid = A((IEngineeringObject)m, "Identifier") ?? A((IEngineeringObject)m, "HwIdentifier");
                    Console.WriteLine(string.Format("      slot {0,2}  {1,-24} hwId={2}", pos, m.Name, hwid ?? "-"));
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
