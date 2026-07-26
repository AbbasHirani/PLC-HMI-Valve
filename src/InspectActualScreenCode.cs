using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;

namespace InspectActualScreenCode
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
            if (procs.Count == 0) { Console.WriteLine("No TIA Portal process."); return; }
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];

            Device hmiDevice = null;
            foreach (var d in project.Devices) if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) { hmiDevice = d; break; }
            HmiSoftware hmi = FindHmiSoftware(hmiDevice);
            HmiScreen sc1 = FindScreen(hmi, "Screen_1");

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\actual_screen_code.txt";
            using (var w = new StreamWriter(outFile))
            {
                w.WriteLine("=== Inspecting Screen_1 Items ===");
                if (sc1 != null)
                {
                    foreach (var item in sc1.ScreenItems)
                    {
                        if (item.Name == "FPC_V001")
                        {
                            w.WriteLine("\nItem: " + item.Name);
                            var evProp = item.GetType().GetProperty("EventHandlers");
                            if (evProp != null)
                            {
                                var evObj = evProp.GetValue(item, null) as IEnumerable;
                                if (evObj != null)
                                {
                                    foreach (var h in evObj)
                                    {
                                        var sp = h.GetType().GetProperty("Script");
                                        if (sp != null)
                                        {
                                            var script = sp.GetValue(h, null);
                                            var codeP = script.GetType().GetProperty("ScriptCode");
                                            w.WriteLine("ScriptCode:\n" + codeP.GetValue(script, null));
                                        }
                                    }
                                }
                            }
                            
                            w.WriteLine("\nDynamizations:");
                            var dynProp = item.GetType().GetProperty("Dynamizations");
                            if (dynProp != null)
                            {
                                var dyns = dynProp.GetValue(item, null) as IEnumerable;
                                if (dyns != null)
                                {
                                    foreach (var dyn in dyns)
                                    {
                                        w.WriteLine("  Property: " + GetPropStr(dyn, "PropertyName") + " | Type: " + dyn.GetType().Name);
                                        var scp = dyn.GetType().GetProperty("ScriptCode");
                                        if (scp != null) w.WriteLine("    Code:\n" + scp.GetValue(dyn, null));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Console.WriteLine("Code dumped to " + outFile);
        }

        static string GetPropStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }

        static HmiScreen FindScreen(HmiSoftware hmi, string name)
        { foreach (HmiScreen s in hmi.Screens) if (s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return s; return null; }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
    }
}
