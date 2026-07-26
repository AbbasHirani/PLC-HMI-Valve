using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

namespace InspectTagProps
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
            if (procs.Count == 0) { Console.WriteLine("No TIA Portal process found."); return; }
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];

            Device hmiDevice = null;
            foreach (var d in project.Devices) if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) { hmiDevice = d; break; }
            HmiSoftware hmi = FindHmiSoftware(hmiDevice);

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\tag_props_dump.txt";
            using (var w = new StreamWriter(outFile))
            {
                // Find ValveTags table and first tag
                var tablesProp = hmi.GetType().GetProperty("TagTables");
                if (tablesProp == null) { w.WriteLine("No TagTables property"); return; }
                var tables = tablesProp.GetValue(hmi, null) as IEnumerable;

                object firstTag = null;
                foreach (var table in tables)
                {
                    string tName = GetStr(table, "Name");
                    if (tName == "ValveTags")
                    {
                        var tagsProp = table.GetType().GetProperty("Tags");
                        var tags = tagsProp.GetValue(table, null) as IEnumerable;
                        foreach (var tag in tags) { firstTag = tag; break; }
                        break;
                    }
                }

                if (firstTag == null) { w.WriteLine("No tags found in ValveTags"); return; }

                w.WriteLine("=== Tag Type: " + firstTag.GetType().FullName + " ===");
                w.WriteLine("=== ALL Properties ===");
                foreach (var prop in firstTag.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        object val = prop.GetValue(firstTag, null);
                        w.WriteLine(string.Format("  [{0}] {1} = {2} (CanWrite={3})",
                            prop.PropertyType.Name, prop.Name,
                            val != null ? val.ToString() : "<null>",
                            prop.CanWrite));
                    }
                    catch (Exception ex)
                    {
                        w.WriteLine(string.Format("  [?] {0} = ERROR: {1}", prop.Name, ex.Message));
                    }
                }

                w.WriteLine("\n=== Tag Name: " + GetStr(firstTag, "Name") + " ===");
            }
            Console.WriteLine("Tag properties written to " + outFile);
        }

        static string GetStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
    }
}
