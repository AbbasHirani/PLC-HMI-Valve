// Read-only: dumps the PLC's system constants, which is where each hardware object's
// numeric identifier (the LADDR that OB82 reports) actually lives. Never saves.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;

namespace ProbeSystemConstants
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
            try { Run(); } catch (Exception ex) { Console.WriteLine("[ERROR] " + ex); Environment.ExitCode = 1; }
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

            PlcSoftware plc = null;
            foreach (Device d in project.Devices)
            {
                foreach (DeviceItem it in d.DeviceItems) { plc = Find(it); if (plc != null) break; }
                if (plc != null) break;
            }
            if (plc == null) { Console.WriteLine("No PLC software."); return; }

            Console.WriteLine("SYSTEM CONSTANTS for " + plc.Name);
            Console.WriteLine();
            int n = 0;
            foreach (PlcTagTable t in plc.TagTableGroup.TagTables)
            {
                foreach (PlcSystemConstant c in t.SystemConstants)
                {
                    // Only the ET200SP racks and their modules matter here.
                    if (c.Name.IndexOf("ET200SP", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine(string.Format("  {0,-6} {1}", c.Value, c.Name));
                    n++;
                }
            }
            foreach (var g in plc.TagTableGroup.Groups)
                foreach (PlcTagTable t in g.TagTables)
                    foreach (PlcSystemConstant c in t.SystemConstants)
                    {
                        if (c.Name.IndexOf("ET200SP", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        Console.WriteLine(string.Format("  {0,-6} {1}", c.Value, c.Name));
                        n++;
                    }
            Console.WriteLine();
            Console.WriteLine("  " + n + " ET200SP-related constants");
        }

        static PlcSoftware Find(DeviceItem it)
        {
            var c = it.GetService<SoftwareContainer>();
            if (c != null && c.Software is PlcSoftware) return (PlcSoftware)c.Software;
            foreach (DeviceItem s in it.DeviceItems) { var r = Find(s); if (r != null) return r; }
            return null;
        }
    }
}
