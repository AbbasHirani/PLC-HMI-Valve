// Read-only: prints the text of the nav tab buttons on every screen that has them.
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;

namespace ProbeNavLabels
{
    class Program
    {
        static Assembly ResolveAssembly(object s, ResolveEventArgs a)
        {
            int i = a.Name.IndexOf(',');
            string n = i == -1 ? a.Name : a.Name.Substring(0, i);
            string[] bases = { @"D:\Siemens\Portal V20\PublicAPI\V20", @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20" };
            foreach (var b in bases) { string p = Path.Combine(b, n + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        }
        static void Main() { AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly; Run(); }
        static HmiSoftware Find(DeviceItem it)
        {
            var c = it.GetService<SoftwareContainer>();
            if (c != null && c.Software is HmiSoftware) return (HmiSoftware)c.Software;
            foreach (DeviceItem s in it.DeviceItems) { var r = Find(s); if (r != null) return r; }
            return null;
        }
        static string Text(object o)
        {
            foreach (var prop in new[] { "Text", "TextFromList", "Caption" })
            {
                var p = o.GetType().GetProperty(prop); if (p == null) continue;
                var v = p.GetValue(o, null); if (v == null) continue;
                var items = v.GetType().GetProperty("Items");
                if (items != null)
                    foreach (var it in (IEnumerable)items.GetValue(v, null))
                    { var t = it.GetType().GetProperty("Text").GetValue(it, null) as string; if (!string.IsNullOrEmpty(t)) return t; }
                return v.ToString();
            }
            return "?";
        }
        static void Run()
        {
            Project pr = null;
            foreach (var p in TiaPortal.GetProcesses()) { var a = p.Attach(); if (a.Projects.Count > 0) { pr = a.Projects[0]; break; } }
            HmiSoftware hmi = null;
            foreach (Device d in pr.Devices) { foreach (DeviceItem it in d.DeviceItems) { hmi = Find(it); if (hmi != null) break; } if (hmi != null) break; }
            foreach (HmiScreen sc in hmi.Screens)
            {
                var line = "";
                for (int i = 0; i < 8; i++)
                {
                    var item = sc.ScreenItems.Find("Nav_" + i);
                    if (item != null) { var t = Text(item); if (t.ToUpper().Contains("ALARM") || t.ToUpper().Contains("FAULT")) line = "Nav_" + i + " = " + t; }
                }
                if (line != "") Console.WriteLine(sc.Name.PadRight(24) + line);
            }
        }
    }
}
