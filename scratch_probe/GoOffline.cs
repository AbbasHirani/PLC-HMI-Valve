// Takes the PLC offline (or reports state with no argument). Block imports are refused while
// TIA is connected - "This function is not supported in online mode" - so this exists to avoid
// a manual round trip. Reversible: going back online is a click in TIA.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.Online;

namespace GoOffline
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

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try { Run(args.Contains("--offline")); }
            catch (Exception ex) { Console.WriteLine("[ERROR] " + ex.Message); Environment.ExitCode = 1; }
        }

        static void Run(bool doOffline)
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
                foreach (DeviceItem it in d.DeviceItems)
                {
                    var op = Walk(it);
                    if (op == null) continue;
                    Console.WriteLine(d.Name + " : connected = " + op.IsConnected);
                    if (doOffline && op.IsConnected)
                    {
                        Console.WriteLine("  going offline...");
                        op.GoOffline();
                        Console.WriteLine("  now connected = " + op.IsConnected);
                    }
                }
            }
        }

        static OnlineProvider Walk(DeviceItem it)
        {
            var op = it.GetService<OnlineProvider>();
            if (op != null) return op;
            foreach (DeviceItem s in it.DeviceItems) { var r = Walk(s); if (r != null) return r; }
            return null;
        }
    }
}
