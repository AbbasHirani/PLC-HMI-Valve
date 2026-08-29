using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

class Program {
    static void Main() {
        var tia = TiaPortal.GetProcesses()[0].Attach();
        var proj = tia.Projects[0];

        Console.WriteLine("=== PROJECT LANGUAGES ===");
        try {
            foreach (var l in proj.LanguageSettings.Languages)
                Console.WriteLine("  project language: " + l.Culture.Name);
            Console.WriteLine("  reference language: " + proj.LanguageSettings.ReferenceLanguage.Culture.Name);
            Console.WriteLine("  editing language:   " + proj.LanguageSettings.EditingLanguage.Culture.Name);
        } catch (Exception ex) { Console.WriteLine("  [langs] " + ex.Message); }

        HmiSoftware hmi = null;
        foreach (var d in proj.Devices) { foreach (var it in d.DeviceItems) { hmi = F(it); if (hmi!=null) break; } if (hmi!=null) break; }
        if (hmi == null) { Console.WriteLine("no HMI"); return; }

        Console.WriteLine();
        Console.WriteLine("=== HMI RUNTIME LANGUAGES ===");
        Dump(hmi, "Languages");
        Dump(hmi, "RuntimeLanguages");

        Console.WriteLine();
        Console.WriteLine("=== DISCRETE ALARMS: " + hmi.DiscreteAlarms.Count + " ===");

        int blankName = 0, blankText = 0, checkedN = 0;
        string firstBlank = null;
        foreach (var a in hmi.DiscreteAlarms) {
            checkedN++;
            string n = a.Name ?? "";
            if (n.Trim().Length == 0) { blankName++; continue; }
            string t = ReadMl(a, "EventText");
            if (t.Trim().Length == 0) { blankText++; if (firstBlank == null) firstBlank = n; }
        }
        Console.WriteLine("  checked:            " + checkedN);
        Console.WriteLine("  blank NAME:         " + blankName);
        Console.WriteLine("  blank EventText:    " + blankText + (firstBlank != null ? "   first = " + firstBlank : ""));

        Console.WriteLine();
        Console.WriteLine("=== SAMPLES (full detail, all languages) ===");
        foreach (var nm in new[] { "V001_Unhealthy", "V021_Unhealthy", "V021_DoubleInd",
                                   "V021_FailOpen", "System_PLC_CPU_Fault" }) {
            var a = hmi.DiscreteAlarms.Find(nm);
            if (a == null) { Console.WriteLine("  " + nm + "  -> NOT FOUND"); continue; }
            Console.WriteLine("  " + nm);
            Console.WriteLine("     class=" + a.AlarmClass + "  pri=" + a.Priority +
                              "  origin='" + a.Origin + "'  area='" + a.Area + "'");
            Console.WriteLine("     tag='" + a.RaisedStateTag + "' bit=" + a.RaisedStateTagBitNumber);
            DumpMl(a, "EventText");
        }

        Console.WriteLine();
        Console.WriteLine("=== ANY ValveFault ALARM IN AREA 'BALLAST AFT' WITH BLANK TEXT ===");
        int aftBad = 0;
        foreach (var a in hmi.DiscreteAlarms) {
            string ar = ""; try { ar = a.Area ?? ""; } catch {}
            if (ar != "BALLAST AFT") continue;
            if (a.Priority != 14) continue;
            string t = ReadMl(a, "EventText");
            if (t.Trim().Length == 0) { aftBad++; Console.WriteLine("  BLANK: " + a.Name); }
        }
        Console.WriteLine("  blank-text ValveFault alarms in BALLAST AFT: " + aftBad);
    }

    static string ReadMl(object o, string prop) {
        try {
            var p = o.GetType().GetProperty(prop);
            if (p == null) return "";
            object ml = p.GetValue(o, null);
            if (ml == null) return "";
            var items = ml.GetType().GetProperty("Items");
            if (items == null) return ml.ToString();
            var en = items.GetValue(ml, null) as IEnumerable;
            if (en == null) return "";
            string all = "";
            foreach (var it in en) {
                var tp = it.GetType().GetProperty("Text");
                if (tp != null) all += (tp.GetValue(it, null) as string) ?? "";
            }
            return all;
        } catch { return ""; }
    }

    static void DumpMl(object o, string prop) {
        try {
            var p = o.GetType().GetProperty(prop);
            object ml = p.GetValue(o, null);
            var items = ml.GetType().GetProperty("Items").GetValue(ml, null) as IEnumerable;
            int n = 0;
            foreach (var it in items) {
                n++;
                string lang = "?";
                try { lang = it.GetType().GetProperty("Language").GetValue(it, null).ToString(); } catch {}
                string txt = "";
                try { txt = (it.GetType().GetProperty("Text").GetValue(it, null) as string) ?? ""; } catch {}
                Console.WriteLine("     [" + lang + "] '" + txt + "'");
            }
            if (n == 0) Console.WriteLine("     (NO TEXT ITEMS AT ALL)");
        } catch (Exception ex) { Console.WriteLine("     [ml err] " + ex.Message); }
    }

    static void Dump(object o, string prop) {
        try {
            var p = o.GetType().GetProperty(prop);
            if (p == null) { Console.WriteLine("  (no " + prop + " property)"); return; }
            var en = p.GetValue(o, null) as IEnumerable;
            if (en == null) { Console.WriteLine("  " + prop + " = " + p.GetValue(o, null)); return; }
            foreach (var it in en) Console.WriteLine("  " + prop + ": " + it);
        } catch (Exception ex) { Console.WriteLine("  [" + prop + "] " + ex.Message); }
    }

    static HmiSoftware F(DeviceItem it) {
        var c = it.GetService<SoftwareContainer>();
        if (c != null) { var h = c.Software as HmiSoftware; if (h != null) return h; }
        foreach (var s in it.DeviceItems) { var r = F(s); if (r != null) return r; }
        return null;
    }
}
