using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;

namespace UpdateTextAndLabels
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
            if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
            Console.WriteLine("Attaching to TIA Portal...");
            TiaPortal portal  = procs[0].Attach();
            Project   project = portal.Projects[0];

            Device hmiDevice = FindDeviceByPartialName(project, "HMI");
            if (hmiDevice == null) return;
            HmiSoftware hmi = FindHmiSoftware(hmiDevice);
            if (hmi == null) return;

            HmiScreen sc = FindScreen(hmi, "Screen_1");
            if (sc == null) { Console.WriteLine("[ERROR] Screen_1 not found."); return; }

            Console.WriteLine("Updating 88 valve button text script dynamizations...");
            for (int v = 1; v <= 88; v++) {
                string vTag = string.Format("V{0:D3}", v);
                string name = "FPC_" + vTag;
                var item = sc.ScreenItems.Find(name);
                if (item == null) continue;

                // Update Text script dynamization to return plain multiline text
                try {
                    var textDynProp = item.GetType().GetProperty("Dynamizations");
                    if (textDynProp != null) {
                        var dyns = textDynProp.GetValue(item, null) as IEnumerable;
                        if (dyns != null) {
                            foreach (var d in dyns) {
                                string targetProp = GetPropStr(d, "TargetProperty");
                                if (targetProp.Equals("Text", StringComparison.OrdinalIgnoreCase)) {
                                    string newScript = string.Format(
                                        "let healthy = Tags(\"{0}_Healthy\").Read();\n" +
                                        "let open = Tags(\"{0}_OpenFB\").Read();\n" +
                                        "let closed = Tags(\"{0}_ClosedFB\").Read();\n\n" +
                                        "let state = \"MOVING\";\n" +
                                        "if (!healthy || (open && closed)) {{ state = \"FAULT\"; }}\n" +
                                        "else if (open && !closed) {{ state = \"OPEN\"; }}\n" +
                                        "else if (!open && closed) {{ state = \"CLOSED\"; }}\n\n" +
                                        "return \"VALVE V-{1}\\n\" + state;",
                                        vTag, string.Format("{0:D3}", v)
                                    );
                                    SetStr(d, "ScriptCode", newScript);
                                }
                            }
                        }
                    }
                } catch {}
            }

            Console.WriteLine("Updating summary bar labels...");
            string[] labelTexts = { "OPEN VALVES", "CLOSED VALVES", "IN TRANSIT", "SYSTEM FAULTS", "LOCAL MODE", "UNCONFIGURED" };

            for (int i = 0; i < labelTexts.Length; i++) {
                string lblName = "Sum_Lbl_" + i;
                var lbl = sc.ScreenItems.Find(lblName);
                if (lbl != null) {
                    SetMLText(lbl, "Text", labelTexts[i]);
                    SetMLText(lbl, "ProcessValue", labelTexts[i]);
                }
            }

            Console.WriteLine("=== Updates Applied Successfully! ===");
        }

        static void SetMLText(object obj, string propName, string text)
        {
            try {
                var p = obj.GetType().GetProperty(propName);
                if (p == null) return;
                object ml = p.GetValue(obj, null);
                if (ml == null) return;
                var itemsProp = ml.GetType().GetProperty("Items");
                if (itemsProp != null) {
                    var items = itemsProp.GetValue(ml, null);
                    if (items != null) {
                        int count = (int)items.GetType().GetProperty("Count").GetValue(items, null);
                        if (count > 0) {
                            var iEnumerable = items as IEnumerable;
                            foreach (var it in iEnumerable) {
                                var tp = it.GetType().GetProperty("Text");
                                if (tp != null && tp.CanWrite) { tp.SetValue(it, text, null); }
                            }
                        } else {
                            var createM = items.GetType().GetMethod("Create", new Type[] { typeof(string), typeof(string) });
                            if (createM != null) { createM.Invoke(items, new object[] { "en-US", text }); }
                        }
                    }
                }
            } catch {}
        }

        static void SetStr(object obj, string name, string val)
        { try { var p = obj.GetType().GetProperty(name); if (p != null && p.CanWrite) p.SetValue(obj, val, null); } catch {} }

        static string GetPropStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }

        static HmiScreen FindScreen(HmiSoftware hmi, string name)
        { foreach (HmiScreen s in hmi.Screens) if (s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return s; return null; }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }

        static Device FindDeviceByPartialName(Project project, string partial)
        { foreach (var d in project.Devices) if (d.Name.IndexOf(partial, StringComparison.OrdinalIgnoreCase) >= 0) return d; return null; }
    }
}
