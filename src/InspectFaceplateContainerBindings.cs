using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using Siemens.Engineering.HmiUnified.UI.Controls;

namespace InspectFaceplateContainerBindings
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

            // Recreate temporary screen
            string screenName = "Temp_Inspect_Screen";
            HmiScreen existing = FindScreen(hmi, screenName);
            if (existing != null) existing.Delete();

            var sp = hmi.GetType().GetProperty("Screens");
            var screens = sp.GetValue(hmi, null);
            var cm = screens.GetType().GetMethod("Create", new Type[]{ typeof(string) });
            HmiScreen sc = (HmiScreen)cm.Invoke(screens, new object[]{ screenName });

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\faceplate_interface_names.txt";
            using (var w = new StreamWriter(outFile))
            {
                if (sc != null)
                {
                    try {
                        w.WriteLine("Created temporary screen.");
                        
                        // Create Faceplate Container
                        var container = sc.ScreenItems.Create<HmiFaceplateContainer>("TempFPC");
                        w.WriteLine("Created HmiFaceplateContainer.");
                        
                        // Set ContainedType to latest Valve_Faceplate (without version suffix)
                        container.ContainedType = "Valve_Faceplate";
                        w.WriteLine("Set ContainedType to Valve_Faceplate_V_0_0_2.");
                        
                        // Inspect Interface
                        var interfaceProp = container.GetType().GetProperty("Interface");
                        if (interfaceProp != null)
                        {
                            var iface = interfaceProp.GetValue(container, null) as IEnumerable;
                            if (iface != null)
                            {
                                w.WriteLine("\n=== Interface Properties on HmiFaceplateContainer ===");
                                foreach (var item in iface)
                                {
                                    w.WriteLine(string.Format("  Property Name: '{0}' | Type: '{1}'", 
                                        GetPropStr(item, "PropertyName"), item.GetType().FullName));
                                }
                            }
                            else
                            {
                                w.WriteLine("Interface is null.");
                            }
                        }
                        else
                        {
                            w.WriteLine("Interface property not found.");
                        }

                        // Inspect Properties
                        var dynProp = container.GetType().GetProperty("Dynamizations");
                        if (dynProp != null)
                        {
                            var dyns = dynProp.GetValue(container, null) as IEnumerable;
                            if (dyns != null)
                            {
                                w.WriteLine("\n=== Dynamizations on HmiFaceplateContainer ===");
                                foreach (var d in dyns)
                                {
                                    w.WriteLine("  Dynamization: " + GetPropStr(d, "Name"));
                                }
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        w.WriteLine("[ERROR] " + ex);
                    }
                    finally
                    {
                        // Clean up
                        sc.Delete();
                        w.WriteLine("Temporary screen deleted.");
                    }
                }
            }
            Console.WriteLine("Interface names written to " + outFile);
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
