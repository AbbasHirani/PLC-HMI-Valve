using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;

namespace InspectScreenItems
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

            Device hmi = null;
            foreach (var d in project.Devices) if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) { hmi = d; break; }
            HmiSoftware sw = null;
            foreach (DeviceItem item in hmi.DeviceItems) { sw = FindSw(item); if (sw != null) break; }

            HmiScreen sc = null;
            foreach (HmiScreen s in sw.Screens) if (s.Name == "Screen_1") { sc = s; break; }
            if (sc == null) return;

            Console.WriteLine("Screen_1 Width: " + sc.Width + ", Height: " + sc.Height);
            Console.WriteLine("Total ScreenItems count: " + sc.ScreenItems.Count);

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\screen_items_dump.txt";
            using (var w = new StreamWriter(outFile))
            {
                w.WriteLine("Screen_1 Width: " + sc.Width + ", Height: " + sc.Height);
                w.WriteLine("Total items: " + sc.ScreenItems.Count);
                w.WriteLine();

                IEnumerable items = sc.ScreenItems as IEnumerable;
                foreach (object item in items)
                {
                    string name = GetProp(item, "Name");
                    string type = item.GetType().Name;
                    w.WriteLine(string.Format("Name: {0,-30} | Type: {1,-40} | Left: {2}, Top: {3}",
                        name, type, GetProp(item, "Left"), GetProp(item, "Top")));
                }
            }
            Console.WriteLine("Dump written to " + outFile);
        }

        static string GetProp(object obj, string name)
        {
            try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : "?"; } catch { return "?"; }
        }

        static HmiSoftware FindSw(DeviceItem item)
        {
            var c = item.GetService<SoftwareContainer>();
            if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware;
            foreach (DeviceItem sub in item.DeviceItems) { var r = FindSw(sub); if (r != null) return r; }
            return null;
        }
    }
}
