using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;

namespace InspectContainerInterface
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

            Device hmiDevice = null;
            foreach (var d in project.Devices) if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) { hmiDevice = d; break; }
            HmiSoftware hmi = FindHmiSoftware(hmiDevice);
            HmiScreen sc = FindScreen(hmi, "Screen_Popup");
            if (sc == null) { Console.WriteLine("Screen_Popup not found!"); return; }

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\container_interface_inspect.txt";
            using (var w = new StreamWriter(outFile))
            {
                foreach (var item in sc.ScreenItems)
                {
                    w.WriteLine("Item: " + item.Name + " (" + item.GetType().FullName + ")");
                    foreach (var prop in item.GetType().GetProperties())
                    {
                        try {
                            object val = prop.GetValue(item, null);
                            w.WriteLine("  Prop: " + prop.Name + " = " + (val ?? "null"));
                            var enumable = val as IEnumerable;
                            if (enumable != null && !(val is string))
                            {
                                foreach (var sub in enumable)
                                {
                                    w.WriteLine("    SubItem: " + sub.GetType().FullName);
                                    foreach (var sp in sub.GetType().GetProperties())
                                    {
                                        try {
                                            object sval = sp.GetValue(sub, null);
                                            w.WriteLine("      SubProp: " + sp.Name + " = " + (sval ?? "null"));
                                        } catch {}
                                    }
                                }
                            }
                        } catch (Exception ex) {
                            w.WriteLine("  Prop: " + prop.Name + " [ERROR: " + ex.Message + "]");
                        }
                    }
                }
            }
            Console.WriteLine("Details written to " + outFile);
        }

        static HmiScreen FindScreen(HmiSoftware hmi, string name)
        { foreach (HmiScreen s in hmi.Screens) if (s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return s; return null; }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
    }
}
