// Read-only: prints every device in the open project and which one carries PlcSoftware.
// Exists because UpdatePlcBlocks assumed the PLC is named "PLC_1" and bailed when it wasn't.
// Touches nothing and never saves.
using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

namespace ListDevices
{
    class Program
    {
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
            try { Run(); } catch (Exception ex) { Console.WriteLine("[ERROR] " + ex.Message); Environment.ExitCode = 1; }
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("TIA Portal not running."); return; }

            TiaPortal portal = null; Project project = null;
            foreach (var p in procs)
            {
                try { var a = p.Attach(); if (a != null && a.Projects.Count > 0) { portal = a; project = a.Projects[0]; break; } }
                catch { }
            }
            if (project == null) { Console.WriteLine("No open project."); return; }
            Console.WriteLine("Project: " + project.Name);
            Console.WriteLine();

            foreach (Device d in project.Devices) Report(d, "");
            foreach (DeviceUserGroup g in project.DeviceGroups) Walk(g, "");
        }

        static void Walk(DeviceUserGroup g, string indent)
        {
            Console.WriteLine(indent + "[group] " + g.Name);
            foreach (Device d in g.Devices) Report(d, indent + "  ");
            foreach (DeviceUserGroup s in g.Groups) Walk(s, indent + "  ");
        }

        static void Report(Device d, string indent)
        {
            var plc = FindPlc(d);
            string mark = plc != null ? "   <-- PLC (blocks live here)" : "";
            Console.WriteLine(indent + "Device: \"" + d.Name + "\"" + mark);
            if (plc != null)
                Console.WriteLine(indent + "        PlcSoftware.Name = \"" + plc.Name + "\", blocks = " + plc.BlockGroup.Blocks.Count);
        }

        static PlcSoftware FindPlc(Device d)
        {
            foreach (DeviceItem it in d.DeviceItems) { var r = In(it); if (r != null) return r; }
            return null;
        }
        static PlcSoftware In(DeviceItem it)
        {
            var c = it.GetService<SoftwareContainer>();
            if (c != null && c.Software is PlcSoftware) return (PlcSoftware)c.Software;
            foreach (DeviceItem s in it.DeviceItems) { var r = In(s); if (r != null) return r; }
            return null;
        }
    }
}
