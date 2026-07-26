using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

namespace InspectHmiButtonEvents
{
    class Program
    {
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int idx = args.Name.IndexOf(',');
            string name = idx == -1 ? args.Name : args.Name.Substring(0, idx);
            string[] bases = {
                @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20",
                @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI",
                @"C:\Program Files\Siemens\Automation\Portal V20\Bin"
            };
            foreach (var b in bases) { string p = Path.Combine(b, name + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try { Run(); } catch (Exception ex) { Console.WriteLine("[ERROR] " + ex); }
            Console.WriteLine("Press Enter..."); try { Console.ReadLine(); } catch {}
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) return;
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];

            Device hmiDevice = null;
            foreach (var d in project.Devices) if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) { hmiDevice = d; break; }
            HmiSoftware hmi = FindHmiSoftware(hmiDevice);

            // Reflect on HmiButtonEventType
            Assembly hmiAssembly = typeof(HmiSoftware).Assembly;
            Type evEnum = hmiAssembly.GetType("Siemens.Engineering.HmiUnified.UI.Widgets.HmiButtonEventType");
            
            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\button_event_enum.txt";
            using (var w = new StreamWriter(outFile))
            {
                if (evEnum != null)
                {
                    w.WriteLine("=== HmiButtonEventType Enum Names ===");
                    foreach (var name in Enum.GetNames(evEnum))
                    {
                        w.WriteLine("  " + name);
                    }
                }
                else
                {
                    w.WriteLine("Enum HmiButtonEventType not found in " + hmiAssembly.FullName);
                }
            }
            Console.WriteLine("Enum names written to " + outFile);
        }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
    }
}
