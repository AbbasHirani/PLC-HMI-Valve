using System;
using System.Reflection;

namespace InspectDeviceServices
{
    class Program
    {
        static Program()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
                int idx = e.Name.IndexOf(',');
                string name = idx == -1 ? e.Name : e.Name.Substring(0, idx);
                string path = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\" + name + ".dll";
                return System.IO.File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
        }

        static void Main(string[] args)
        {
            try { Runner.Run(); } catch (Exception ex) { Console.WriteLine(ex); }
        }
    }

    class Runner
    {
        public static void Run()
        {
            var procs = Siemens.Engineering.TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("No TIA Portal processes."); return; }
            var portal = procs[0].Attach();
            var project = portal.Projects[0];
            foreach (Siemens.Engineering.HW.Device d in project.Devices) {
                Console.WriteLine("Device: " + d.Name);
                foreach (var m in d.GetType().GetMethods()) {
                    if (m.Name.IndexOf("Compile", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name == "GetService") {
                        Console.WriteLine("  Method: " + m.Name);
                    }
                }
            }
        }
    }
}
