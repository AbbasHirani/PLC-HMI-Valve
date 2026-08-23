// Adds the header LOGIN/LOGOUT button to screens a rebuild cannot reach.
//
// The header is generated per-screen by BuildHomeHeader, so a screen only gets the button when it
// is rebuilt. Screen_Alarms is deliberately excluded from every rebuild - its Alarm Control
// columns were configured by hand and RecreateScreen would delete them - so it has to be patched
// in place. Nothing is deleted here; the button is added and the user text nudged left to fit.
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Drawing;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;

class Program {
    // Same geometry and colours BuildHomeHeader uses, so the patched screens match the rebuilt ones.
    const int BTN_X = 1700, BTN_Y = 8, BTN_W = 200, BTN_H = 30;
    const int USER_X = 1290;
    static readonly Color ACCENT = Color.FromArgb(255, 0, 112, 192);

    // Screen scaling - the builder maps a 1920x1080 design onto the device resolution.
    static double sx = 1.0, sy = 1.0;

    const string TEXT_JS =
        "var u = \"\"; try { u = Tags(\"@UserName\").Read(); } catch(e){}\n" +
        "if (!u || String(u).toUpperCase() === \"DEFAULTUSER\") return \"LOGIN\";\n" +
        "return \"LOGOUT\";";

    const string CLICK_JS =
        "var u = \"\"; try { u = Tags(\"@UserName\").Read(); } catch(e){}\n" +
        "if (!u || String(u).toUpperCase() === \"DEFAULTUSER\") {\n" +
        "  HMIRuntime.UI.UserManagement.SysFct.ShowLoginDialog();\n" +
        "} else {\n" +
        "  HMIRuntime.UI.SysFct.LogOff();\n" +
        "}\n";

    static void Main(string[] args) {
        bool report = args.Any(a => a == "--report");
        bool redo   = args.Any(a => a == "--redo");
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
        var proj = procs[0].Attach().Projects[0];
        Device hmiDevice = null;
        foreach (var d in proj.Devices)
            if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) hmiDevice = d;
        var hmi = FindHmiSoftware(hmiDevice);

        // Derive the scale from a screen the builder already produced, rather than assuming it.
        var refScreen = hmi.Screens.Find("Screen_Home");
        if (refScreen != null) {
            var refBtn = refScreen.ScreenItems.FirstOrDefault(i => i.Name == "Hdr_AuthBtn") as HmiButton;
            if (refBtn != null) {
                sx = refBtn.Left / (double)BTN_X;
                sy = refBtn.Top  / (double)BTN_Y;
                Console.WriteLine(string.Format("reference Hdr_AuthBtn on Screen_Home: {0},{1} {2}x{3}",
                    refBtn.Left, refBtn.Top, refBtn.Width, refBtn.Height));
                sx = refBtn.Width / (double)BTN_W;      // width is the reliable scale source
                sy = refBtn.Height / (double)BTN_H;
            }
        }
        Console.WriteLine(string.Format("scale x={0:0.000} y={1:0.000}", sx, sy));

        int added = 0, already = 0, skipped = 0;
        foreach (var sc in hmi.Screens) {
            bool hasHeader = sc.ScreenItems.Any(i => i.Name == "Hdr_User");
            if (!hasHeader) { Console.WriteLine(string.Format("  {0,-24} no header - skipped", sc.Name)); skipped++; continue; }
            var existing = sc.ScreenItems.FirstOrDefault(i => i.Name == "Hdr_AuthBtn") as HmiButton;
            if (existing != null && redo && !report) {
                try { existing.Delete(); existing = null; Console.WriteLine(string.Format("  {0,-24} old button deleted", sc.Name)); }
                catch (Exception ex) { Console.WriteLine(string.Format("  {0,-24} delete failed: {1}", sc.Name, Root(ex))); }
            }
            if (existing != null) {
                if (report) { Console.WriteLine(string.Format("  {0,-24} already has button", sc.Name)); already++; continue; }
                // A button added before the type-loading fix has a static caption and never
                // switches to LOGOUT. Re-applying the dynamization repairs it in place.
                if (HasTextDyn(existing)) {
                    Console.WriteLine(string.Format("  {0,-24} already has button", sc.Name)); already++;
                } else {
                    AddScriptDyn(existing, "Text", TEXT_JS);
                    Console.WriteLine(string.Format("  {0,-24} button present, dynamization REPAIRED", sc.Name));
                    added++;
                }
                continue;
            }
            if (report) { Console.WriteLine(string.Format("  {0,-24} WOULD ADD", sc.Name)); continue; }

            try {
                // make room: shift the user text left to match the rebuilt screens
                var ut = sc.ScreenItems.FirstOrDefault(i => i.Name == "Hdr_User") as HmiButton;
                if (ut != null) ut.Left = (int)Math.Round(USER_X * sx);

                var b = sc.ScreenItems.Create<HmiButton>("Hdr_AuthBtn");
                b.Left = (int)Math.Round(BTN_X * sx);
                b.Top = (int)Math.Round(BTN_Y * sy);
                b.Width = (uint)Math.Round(BTN_W * sx);
                b.Height = (uint)Math.Round(BTN_H * sy);
                b.BackColor = ACCENT; b.ForeColor = Color.White;
                b.BorderColor = ACCENT; b.BorderWidth = 0;
                SetFont(b, (int)Math.Round(16 * sy), true);
                SetText(b, "Text", "LOGIN");
                AddScriptDyn(b, "Text", TEXT_JS);
                AddTapped(b, CLICK_JS);
                Console.WriteLine(string.Format("  {0,-24} ADDED", sc.Name));
                added++;
            } catch (Exception ex) {
                Console.WriteLine(string.Format("  {0,-24} FAILED: {1}", sc.Name, Root(ex)));
            }
        }
        Console.WriteLine("\nadded=" + added + "  already=" + already + "  no-header=" + skipped);
        if (!report && added > 0) { Console.WriteLine("Saving..."); proj.Save(); Console.WriteLine("Saved."); }
    }

    static bool HasTextDyn(object item) {
        try {
            var dp = item.GetType().GetProperty("Dynamizations");
            var dyns = dp.GetValue(item, null) as IEnumerable;
            if (dyns == null) return false;
            foreach (var d in dyns) {
                var pn = d.GetType().GetProperty("PropertyName");
                if (pn != null && (pn.GetValue(d, null) as string) == "Text") return true;
            }
        } catch { }
        return false;
    }

    static string Root(Exception e) { while (e.InnerException != null) e = e.InnerException; return e.Message; }

    static void SetFont(object o, int px, bool bold) {
        try {
            var fp = o.GetType().GetProperty("Font"); if (fp == null) return;
            var f = fp.GetValue(o, null); if (f == null) return;
            var sp = f.GetType().GetProperty("Size");
            if (sp != null && sp.CanWrite) sp.SetValue(f, Convert.ChangeType(px, sp.PropertyType), null);
            var wp = f.GetType().GetProperty("Weight");
            if (wp != null && wp.CanWrite && wp.PropertyType.IsEnum)
                wp.SetValue(f, Enum.Parse(wp.PropertyType, bold ? "Bold" : "Normal"), null);
        } catch { }
    }

    static void SetText(object obj, string propName, string text) {
        try {
            var p = obj.GetType().GetProperty(propName); if (p == null) return;
            object ml = p.GetValue(obj, null); if (ml == null) return;
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
                        var cm = items.GetType().GetMethod("Create", new Type[] { typeof(string), typeof(string) });
                        if (cm != null) cm.Invoke(items, new object[] { "", fmt });
                    }
                }
                return;
            }
            var dt = ml.GetType().GetProperty("Text");
            if (dt != null && dt.CanWrite) dt.SetValue(ml, fmt, null);
        } catch (Exception ex) { Console.WriteLine("     [SetText] " + ex.Message); }
    }

    // Mirrors the builder's own Dyn() exactly. The first version set "Script" and Trigger.Name;
    // the real members are ScriptCode and Trigger.Type, so it produced a dynamization with neither
    // a script nor a trigger - which the compiler reports as "The configured tag is invalid".
    static void AddScriptDyn(object item, string propName, string js) {
        try {
            var dp = item.GetType().GetProperty("Dynamizations");
            if (dp == null) { Console.WriteLine("     [Dyn] no Dynamizations property"); return; }
            object dyns = dp.GetValue(item, null);
            if (dyns == null) return;

            MethodInfo create = null;
            foreach (var m in dyns.GetType().GetMethods()) {
                if (m.Name != "Create" || !m.IsGenericMethodDefinition) continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(string)) { create = m; break; }
            }
            if (create == null) { Console.WriteLine("     [Dyn] no Create<T>(string)"); return; }

            object d = create.MakeGenericMethod(typeof(ScriptDynamization)).Invoke(dyns, new object[] { propName });
            var sd = (ScriptDynamization)d;
            sd.ScriptCode = js;
            sd.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
        } catch (Exception ex) { Console.WriteLine("     [Dyn] " + Root(ex)); }
    }

    static void AddTapped(object btn, string js) {
        try {
            PropertyInfo evProp = null;
            foreach (var p in btn.GetType().GetProperties())
                if (p.Name == "EventHandlers") { evProp = p; if (p.DeclaringType == btn.GetType()) break; }
            if (evProp == null) return;
            object evObj = evProp.GetValue(btn, null);
            Type evEnum = null; MethodInfo cm = null;
            foreach (var m in evObj.GetType().GetMethods()) {
                if (m.Name != "Create") continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType.IsEnum) { cm = m; evEnum = ps[0].ParameterType; break; }
            }
            if (cm == null) return;
            object handler = cm.Invoke(evObj, new object[] { Enum.Parse(evEnum, "Tapped") });
            var sp = handler.GetType().GetProperty("Script");
            object script = sp.GetValue(handler, null);
            var cp = script.GetType().GetProperty("ScriptCode");
            if (cp != null && cp.CanWrite) cp.SetValue(script, js, null);
        } catch (Exception ex) { Console.WriteLine("     [Tapped] " + Root(ex)); }
    }

    static HmiSoftware FindHmiSoftware(Device device)
    { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
    static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
    { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
}
