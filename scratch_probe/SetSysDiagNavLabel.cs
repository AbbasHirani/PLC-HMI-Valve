// Puts Nav_4 on Screen_SysDiag back to the FAULTS label the user set on every other screen.
// A --only=SysDiag rebuild had redrawn it from code that still said ALARMS.
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

namespace SetSysDiagNavLabel
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
        static object FirstItem(object btn)
        {
            var text = btn.GetType().GetProperty("Text").GetValue(btn, null);
            foreach (var it in (IEnumerable)text.GetType().GetProperty("Items").GetValue(text, null)) return it;
            return null;
        }
        static void Run()
        {
            Project pr = null;
            foreach (var p in TiaPortal.GetProcesses()) { var a = p.Attach(); if (a.Projects.Count > 0) { pr = a.Projects[0]; break; } }
            HmiSoftware hmi = null;
            foreach (Device d in pr.Devices) { foreach (DeviceItem it in d.DeviceItems) { hmi = Find(it); if (hmi != null) break; } if (hmi != null) break; }
            var model = FirstItem(hmi.Screens.Find("Screen_Home").ScreenItems.Find("Nav_4"));
            var target = FirstItem(hmi.Screens.Find("Screen_SysDiag").ScreenItems.Find("Nav_4"));
            var tp = model.GetType().GetProperty("Text");
            string wanted = (string)tp.GetValue(model, null);
            if (!wanted.Contains("FAULTS")) throw new Exception("Home label is not FAULTS: " + wanted);
            target.GetType().GetProperty("Text").SetValue(target, wanted, null);
            pr.Save();
            Console.WriteLine("Screen_SysDiag Nav_4 set and saved.");
        }
    }
}
