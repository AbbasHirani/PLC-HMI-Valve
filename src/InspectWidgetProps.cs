using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Widgets;

namespace InspectWidgetProps
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
            foreach (var b in bases) {
                string p = Path.Combine(b, name + ".dll");
                if (File.Exists(p)) return Assembly.LoadFrom(p);
            }
            return null;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try { Run(); }
            catch (Exception ex) { Console.WriteLine("[ERROR] " + ex.Message); }
            Console.WriteLine("\nPress Enter..."); Console.ReadLine();
        }

        static void Run()
        {
            var processes = TiaPortal.GetProcesses();
            if (processes.Count == 0) { Console.WriteLine("No TIA Portal running."); return; }
            TiaPortal portal  = processes[0].Attach();
            Project   project = portal.Projects[0];

            // Find Screen_1
            Device hmi = null;
            foreach (var d in project.Devices)
                if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) { hmi = d; break; }
            HmiSoftware sw = null;
            foreach (DeviceItem item in hmi.DeviceItems) { sw = FindSw(item); if (sw != null) break; }
            HmiScreen sc = null;
            foreach (HmiScreen s in sw.Screens) { sc = s; break; }

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\widget_props.txt";
            using (var w = new StreamWriter(outFile))
            {
                // Create temp items, dump their properties, then delete
                DumpType(sc, w, "HmiRectangle", () => sc.ScreenItems.Create<HmiRectangle>("__tmp_rect"));
                DumpType(sc, w, "HmiEllipse",   () => sc.ScreenItems.Create<HmiEllipse>("__tmp_ellipse"));
                DumpType(sc, w, "HmiButton",    () => sc.ScreenItems.Create<HmiButton>("__tmp_btn"));
                DumpType(sc, w, "HmiIOField",   () => sc.ScreenItems.Create<HmiIOField>("__tmp_io"));
                DumpType(sc, w, "HmiLabel",     () => { try { return sc.ScreenItems.Create<HmiLabel>("__tmp_lbl"); } catch { return null; } });
            }

            Console.WriteLine("Written to " + outFile);
        }

        static void DumpType(HmiScreen sc, StreamWriter w, string typeName, Func<object> creator)
        {
            object obj = null;
            try { obj = creator(); } catch (Exception ex) { w.WriteLine("[" + typeName + "] CREATE FAILED: " + ex.Message); return; }
            if (obj == null) { w.WriteLine("[" + typeName + "] NULL"); return; }
            w.WriteLine("=== " + typeName + " ===");
            var props = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(props, (a, b) => string.Compare(a.Name, b.Name));
            foreach (var p in props)
            {
                try {
                    object val = p.GetValue(obj, null);
                    w.WriteLine(string.Format("  {0,-35} {1,-30} = {2}", p.Name, p.PropertyType.Name, val));
                } catch {
                    w.WriteLine(string.Format("  {0,-35} {1,-30} = (error)", p.Name, p.PropertyType.Name));
                }
            }
            // Cleanup
            try { var dm = obj.GetType().GetMethod("Delete"); if (dm != null) dm.Invoke(obj, null); } catch {}
            w.WriteLine();
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
