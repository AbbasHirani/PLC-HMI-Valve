using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;

namespace InspectHmiSysFctSignatures
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

            // Create a temporary screen and button to test System Functions
            var sc = hmi.Screens.Find("Screen_1");
            if (sc != null) {
                var btn = sc.ScreenItems.Find("Nav_Overview");
                if (btn != null) {
                    Console.WriteLine("=== Inspecting Nav_Overview Event Handlers ===");
                    var evProp = btn.GetType().GetProperty("EventHandlers");
                    if (evProp != null) {
                        var evObj = evProp.GetValue(btn, null) as IEnumerable;
                        if (evObj != null) {
                            foreach (var h in evObj) {
                                Console.WriteLine("Handler: " + h.GetType().FullName);
                                var sp = h.GetType().GetProperty("Script");
                                if (sp != null) {
                                    var script = sp.GetValue(h, null);
                                    var codeP = script.GetType().GetProperty("ScriptCode");
                                    Console.WriteLine("Current ScriptCode: " + codeP.GetValue(script, null));
                                }
                            }
                        }
                    }
                }
            }
        }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
    }
}
