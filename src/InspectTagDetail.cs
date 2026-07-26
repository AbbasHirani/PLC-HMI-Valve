using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

namespace InspectTagDetail
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

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\tag_details.txt";
            using (var w = new StreamWriter(outFile))
            {
                var tagTablesProp = hmi.GetType().GetProperty("TagTables");
                var tables = tagTablesProp.GetValue(hmi, null) as IEnumerable;

                foreach (var table in tables)
                {
                    string tableName = GetPropStr(table, "Name");
                    w.WriteLine("=== Tag Table: " + tableName + " ===");

                    var tagsProp = table.GetType().GetProperty("Tags");
                    var tags = tagsProp.GetValue(table, null) as IEnumerable;
                    int i = 0;
                    foreach (var tag in tags)
                    {
                        string name = GetPropStr(tag, "Name");
                        if (name.StartsWith("OpenCmd_V1") || name.StartsWith("V001_OpenCmd"))
                        {
                            w.WriteLine("--- Tag: " + name + " ---");
                            var props = tag.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                            Array.Sort(props, (a, b) => string.Compare(a.Name, b.Name));
                            foreach (var p in props)
                            {
                                try {
                                    object val = p.GetValue(tag, null);
                                    w.WriteLine(string.Format("  {0,-35} {1,-30} = {2}", p.Name, p.PropertyType.Name, val));
                                } catch {
                                    w.WriteLine(string.Format("  {0,-35} {1,-30} = (error)", p.Name, p.PropertyType.Name));
                                }
                            }
                            w.WriteLine();
                        }
                        i++;
                    }
                }
            }
            Console.WriteLine("Tag details written to " + outFile);
        }

        static string GetPropStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
    }
}
