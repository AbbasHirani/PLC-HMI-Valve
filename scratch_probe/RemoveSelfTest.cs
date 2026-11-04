// Removes the temporary module self-test: FC_ModuleSelfTest and SelfTest_DB, only those two names.
// Run AFTER the clean temp_fb_valveloop.xml has been imported, so nothing still calls them.
using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

namespace RemoveSelfTest
{
    class Program
    {
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
            try { Run(); } catch (Exception ex) { Console.WriteLine("[ERROR] " + ex.Message); Environment.ExitCode = 1; }
        }
        static PlcSoftware Find(DeviceItem it)
        {
            var c = it.GetService<SoftwareContainer>();
            if (c != null && c.Software is PlcSoftware) return (PlcSoftware)c.Software;
            foreach (DeviceItem s in it.DeviceItems) { var r = Find(s); if (r != null) return r; }
            return null;
        }
        static void Run()
        {
            Project pr = null;
            foreach (var p in TiaPortal.GetProcesses()) { var a = p.Attach(); if (a.Projects.Count > 0) { pr = a.Projects[0]; break; } }
            if (pr == null) throw new Exception("no project");
            PlcSoftware plc = null;
            foreach (Device d in pr.Devices) { foreach (DeviceItem it in d.DeviceItems) { plc = Find(it); if (plc != null) break; } if (plc != null) break; }
            foreach (var n in new[] { "FC_ModuleSelfTest", "SelfTest_DB" })
            {
                var b = plc.BlockGroup.Blocks.Find(n);
                if (b == null) { Console.WriteLine("  already gone: " + n); continue; }
                b.Delete();
                Console.WriteLine("  deleted: " + n);
            }
            pr.Save();
            Console.WriteLine("Saved.");
        }
    }
}
