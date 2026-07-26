using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;

namespace InspectGeneratedButton
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
            HmiScreen sc = FindScreen(hmi, "Screen_1");

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\button_inspect.txt";
            using (var w = new StreamWriter(outFile))
            {
                foreach (var item in sc.ScreenItems)
                {
                    if (item.Name.Equals("FPC_V001", StringComparison.OrdinalIgnoreCase) || item.Name.Equals("Nav_Overview", StringComparison.OrdinalIgnoreCase))
                    {
                        w.WriteLine("=== Button: " + item.Name + " ===");
                        
                        // Inspect Text property
                        var textProp = item.GetType().GetProperty("Text");
                        if (textProp != null)
                        {
                            object ml = textProp.GetValue(item, null);
                            w.WriteLine("Text Property Type: " + ml.GetType().FullName);
                            var itemsProp = ml.GetType().GetProperty("Items");
                            if (itemsProp != null)
                            {
                                var items = itemsProp.GetValue(ml, null) as IEnumerable;
                                if (items != null)
                                {
                                    foreach (var it in items)
                                    {
                                        w.WriteLine(string.Format("  Item Text: '{0}' | Culture: '{1}'", 
                                            GetPropStr(it, "Text"), GetPropStr(it, "Culture")));
                                    }
                                }
                            }
                        }

                        // Inspect Click Event Script
                        w.WriteLine("Event Handlers:");
                        var evProp = item.GetType().GetProperty("EventHandlers");
                        if (evProp != null)
                        {
                            var evs = evProp.GetValue(item, null) as IEnumerable;
                            if (evs != null)
                            {
                                foreach (var handler in evs)
                                {
                                    w.WriteLine("  Handler: " + handler.GetType().FullName);
                                    var sp = handler.GetType().GetProperty("Script");
                                    if (sp != null)
                                    {
                                        object script = sp.GetValue(handler, null);
                                        w.WriteLine("    Script: " + script.GetType().FullName);
                                        w.WriteLine("    Code: " + GetPropStr(script, "ScriptCode"));
                                    }
                                }
                            }
                        }
                        w.WriteLine();
                    }
                }
            }
            Console.WriteLine("Button details written to " + outFile);
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
