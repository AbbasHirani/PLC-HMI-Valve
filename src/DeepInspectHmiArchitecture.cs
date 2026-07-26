using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.Library.Types;

namespace DeepInspectHmiArchitecture
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
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) return;
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\deep_hmi_architecture.txt";
            using (var w = new StreamWriter(outFile))
            {
                w.WriteLine("=== PROJECT DETAILS ===");
                w.WriteLine("Project Name: " + project.Name);
                w.WriteLine("Project Path: " + project.Path);

                Device hmiDevice = null;
                foreach (var d in project.Devices) if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) { hmiDevice = d; break; }
                if (hmiDevice == null) { w.WriteLine("[ERROR] No HMI Device found!"); return; }

                w.WriteLine("HMI Device: " + hmiDevice.Name);
                HmiSoftware hmi = FindHmiSoftware(hmiDevice);
                if (hmi == null) { w.WriteLine("[ERROR] No HmiSoftware found!"); return; }

                w.WriteLine("\n=== HMI SCREENS ===");
                foreach (var sc in hmi.Screens)
                {
                    w.WriteLine("Screen: " + sc.Name + " (Width: " + sc.Width + ", Height: " + sc.Height + ")");
                    w.WriteLine("  ScreenItems count: " + sc.ScreenItems.Count);
                    int btnCount = 0;
                    int fpcCount = 0;
                    foreach (var si in sc.ScreenItems)
                    {
                        if (si.GetType().Name.Contains("Button")) btnCount++;
                        else if (si.GetType().Name.Contains("Faceplate") || si.GetType().Name.Contains("Container")) fpcCount++;
                    }
                    w.WriteLine("  Buttons: " + btnCount + ", FaceplateContainers: " + fpcCount);
                }

                w.WriteLine("\n=== PROJECT LIBRARY TYPES ===");
                var lib = project.ProjectLibrary;
                if (lib != null)
                {
                    foreach (var t in lib.TypeFolder.Types)
                    {
                        w.WriteLine("Type: " + t.Name + " (Status: " + t.Status + ")");
                        foreach (var v in t.Versions)
                        {
                            w.WriteLine("  Version: " + v.VersionNumber + " (IsDefault: " + v.IsDefault + ", State: " + v.State + ", Guid: " + v.Guid + ")");
                        }
                    }
                }

                w.WriteLine("\n=== HMI TAG TABLES ===");
                var tablesProp = hmi.GetType().GetProperty("TagTables") ?? hmi.GetType().GetProperty("TagFolder");
                if (tablesProp != null)
                {
                    var tables = tablesProp.GetValue(hmi, null) as IEnumerable;
                    if (tables != null)
                    {
                        foreach (var table in tables)
                        {
                            var nameP = table.GetType().GetProperty("Name");
                            var nameStr = nameP != null ? nameP.GetValue(table, null) : "Table";
                            w.WriteLine("TagTable: " + nameStr);
                        }
                    }
                }
            }
            Console.WriteLine("Deep architecture analysis written to " + outFile);
        }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
    }
}
