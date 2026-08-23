// Retitles the last nav button (Nav_6) on every screen from LOGIN to AUDIT LOG.
//
// The nav bar is not shared - BuildNav() is called inside each screen's builder, so every screen
// carries its own copy of the seven buttons. Changing the label in source only reaches a screen
// when that screen is rebuilt, which is why AUDIT LOG appeared on Screen_Login alone.
//
// A full rebuild would fix it, but Screen_Alarms is deliberately excluded from every normal
// rebuild to protect its hand-configured Alarm Control columns, and rebuilding all six screens to
// change one caption is a poor trade. This patches the caption in place instead: no screen is
// deleted, no layout regenerated, nothing else touched.
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.HmiUnified;

class Program {
    const string NAV_ITEM  = "Nav_6";
    const string NEW_LABEL = "&#x1F4CB;  AUDIT LOG";
    const string OLD_HINT  = "LOGIN";

    static void Main(string[] args) {
        bool report = args.Any(a => a == "--report");
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
        var proj = procs[0].Attach().Projects[0];
        Device hmiDevice = null;
        foreach (var d in proj.Devices)
            if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) hmiDevice = d;
        var hmi = FindHmiSoftware(hmiDevice);

        int patched = 0, missing = 0;
        foreach (var sc in hmi.Screens) {
            var btn = sc.ScreenItems.FirstOrDefault(i => i.Name == NAV_ITEM);
            if (btn == null) { Console.WriteLine(string.Format("  {0,-24} no {1}", sc.Name, NAV_ITEM)); missing++; continue; }

            string before = GetText(btn);
            if (report) {
                Console.WriteLine(string.Format("  {0,-24} {1}", sc.Name, Flatten(before)));
                continue;
            }
            if (before != null && before.IndexOf(OLD_HINT, StringComparison.OrdinalIgnoreCase) < 0
                               && before.IndexOf("AUDIT", StringComparison.OrdinalIgnoreCase) >= 0) {
                Console.WriteLine(string.Format("  {0,-24} already AUDIT LOG", sc.Name));
                continue;
            }
            SetText(btn, "Text", NEW_LABEL);
            Console.WriteLine(string.Format("  {0,-24} {1}  ->  AUDIT LOG", sc.Name, Flatten(before)));
            patched++;
        }
        Console.WriteLine("\npatched=" + patched + "  screens without " + NAV_ITEM + "=" + missing);
        if (!report && patched > 0) { Console.WriteLine("Saving..."); proj.Save(); Console.WriteLine("Saved."); }
    }

    static string Flatten(string s) {
        if (s == null) return "(null)";
        s = s.Replace("<body><p>", "").Replace("</p></body>", "").Trim();
        return s.Length > 30 ? s.Substring(0, 30) : s;
    }

    static string GetText(object obj) {
        try {
            var p = obj.GetType().GetProperty("Text");
            if (p == null) return null;
            object ml = p.GetValue(obj, null);
            if (ml == null) return null;
            var itemsProp = ml.GetType().GetProperty("Items");
            if (itemsProp != null) {
                var items = itemsProp.GetValue(ml, null);
                if (items != null)
                    foreach (var it in (IEnumerable)items) {
                        var tp = it.GetType().GetProperty("Text");
                        if (tp != null) return tp.GetValue(it, null) as string;
                    }
                return null;
            }
            var dt = ml.GetType().GetProperty("Text");
            return dt != null ? dt.GetValue(ml, null) as string : null;
        } catch { return null; }
    }

    // Same shape as the builder's own SetText - MultilingualText wants <body><p>..</p></body>.
    static void SetText(object obj, string propName, string text) {
        try {
            var p = obj.GetType().GetProperty(propName);
            if (p == null) return;
            object ml = p.GetValue(obj, null);
            if (ml == null) return;
            string fmt = "<body><p>" + text + "</p></body>";
            var itemsProp = ml.GetType().GetProperty("Items");
            if (itemsProp != null) {
                var items = itemsProp.GetValue(ml, null);
                if (items != null) {
                    int count = (int)items.GetType().GetProperty("Count").GetValue(items, null);
                    if (count > 0) {
                        foreach (var it in (IEnumerable)items) {
                            var tp = it.GetType().GetProperty("Text");
                            if (tp != null && tp.CanWrite) tp.SetValue(it, fmt, null);
                        }
                    } else {
                        var createM = items.GetType().GetMethod("Create", new Type[] { typeof(string), typeof(string) });
                        if (createM != null) createM.Invoke(items, new object[] { "", fmt });
                    }
                }
                return;
            }
            var dt = ml.GetType().GetProperty("Text");
            if (dt != null && dt.CanWrite) dt.SetValue(ml, fmt, null);
        } catch (Exception ex) { Console.WriteLine("  [SetText ERR] " + ex.Message); }
    }

    static HmiSoftware FindHmiSoftware(Device device)
    { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
    static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
    { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
}
