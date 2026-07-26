using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

namespace InspectHmiTagMultiplexing
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

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\hmi_tag_multiplexing_inspect.txt";
            using (var w = new StreamWriter(outFile))
            {
                var tablesProp = hmi.GetType().GetProperty("TagTables");
                if (tablesProp != null)
                {
                    var tables = tablesProp.GetValue(hmi, null) as IEnumerable;
                    if (tables != null)
                    {
                        foreach (var table in tables)
                        {
                            var tagsProp = table.GetType().GetProperty("Tags");
                            if (tagsProp != null)
                            {
                                var tags = tagsProp.GetValue(table, null) as IEnumerable;
                                if (tags != null)
                                {
                                    foreach (var tag in tags)
                                    {
                                        w.WriteLine("Tag: " + GetPropStr(tag, "Name") + " (" + tag.GetType().FullName + ")");
                                        foreach (var prop in tag.GetType().GetProperties())
                                        {
                                            try {
                                                object val = prop.GetValue(tag, null);
                                                w.WriteLine("  Prop: " + prop.Name + " = " + (val ?? "null"));
                                            } catch (Exception ex) {
                                                w.WriteLine("  Prop: " + prop.Name + " [ERROR: " + ex.Message + "]");
                                            }
                                        }
                                        break; // inspect 1 tag
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Console.WriteLine("Details written to " + outFile);
        }

        static string GetPropStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
    }
}
