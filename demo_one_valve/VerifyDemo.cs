// Read-only check of the one-valve demo project. Refuses any other project.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;

namespace VerifyDemo
{
    class Program
    {
        static int fails = 0;

        static Assembly ResolveAssembly(object s, ResolveEventArgs a)
        {
            int i = a.Name.IndexOf(',');
            string n = i == -1 ? a.Name : a.Name.Substring(0, i);
            string[] bases = { @"D:\Siemens\Portal V20\PublicAPI\V20", @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20" };
            foreach (var b in bases) { string p = Path.Combine(b, n + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try { Run(args[0]); } catch (Exception ex) { Console.WriteLine("[ERROR] " + ex); Environment.ExitCode = 1; return; }
            Console.WriteLine(fails == 0 ? "\nALL CHECKS PASSED" : "\n" + fails + " CHECK(S) FAILED");
            Environment.ExitCode = fails == 0 ? 0 : 1;
        }

        static void Check(string label, bool ok, string detail = null)
        {
            Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + label + (detail == null ? "" : "   (" + detail + ")"));
            if (!ok) fails++;
        }

        static string P(object o, string name)
        {
            try { var p = o.GetType().GetProperty(name); if (p == null) return null; var v = p.GetValue(o, null); return v == null ? null : v.ToString(); }
            catch { return null; }
        }

        static T Soft<T>(DeviceItem it) where T : class
        {
            var c = it.GetService<SoftwareContainer>();
            if (c != null && c.Software is T) return c.Software as T;
            foreach (DeviceItem s in it.DeviceItems) { var r = Soft<T>(s); if (r != null) return r; }
            return null;
        }

        static T Find<T>(Project pr) where T : class
        {
            foreach (Device d in pr.Devices) foreach (DeviceItem it in d.DeviceItems) { var r = Soft<T>(it); if (r != null) return r; }
            return null;
        }

        static string Script(object item)
        {
            string s = "";
            foreach (var eh in (IEnumerable)item.GetType().GetProperty("EventHandlers").GetValue(item, null))
            {
                var so = eh.GetType().GetProperty("Script").GetValue(eh, null);
                s += P(so, "ScriptCode");
            }
            return s;
        }

        static void Run(string outDir)
        {
            Project pr = null;
            foreach (var p in TiaPortal.GetProcesses()) { var a = p.Attach(); if (a.Projects.Count > 0) { pr = a.Projects[0]; break; } }
            if (pr == null || pr.Name != "valveDemo_OneValve") throw new Exception("not the demo project: " + (pr == null ? "none" : pr.Name));
            Console.WriteLine("Project: " + pr.Name);
            var plc = Find<PlcSoftware>(pr);
            var hmi = Find<HmiSoftware>(pr);

            Console.WriteLine("Hardware");
            var devs = pr.Devices.Select(d => d.Name).ToList();
            Check("only the PLC and HMI remain", devs.Count == 2 && !devs.Any(d => d.Contains("ET200SP")), string.Join(", ", devs));

            Console.WriteLine("PLC start values");
            foreach (var n in new[] { "FC_PhysicalIoCopy", "FC_IoMapper", "FB_ValveLoop", "FC_ModuleEvent", "Valves_DB", "Valve_Channels_DB" })
            {
                var b = plc.BlockGroup.Blocks.Find(n);
                Check(n + " consistent", b != null && b.IsConsistent);
            }
            Directory.CreateDirectory(outDir);
            Func<string, string> export = n =>
            {
                var f = new FileInfo(Path.Combine(outDir, "verify_" + n + ".xml"));
                if (f.Exists) f.Delete();
                plc.BlockGroup.Blocks.Find(n).Export(f, ExportOptions.WithDefaults);
                return File.ReadAllText(f.FullName);
            };
            var vdb = export("Valves_DB");
            Check("SelIdx starts at 21", Regex.IsMatch(vdb, "Name=\"SelIdx\"[^>]*>\\s*(<AttributeList>.*?</AttributeList>\\s*)?<StartValue>21</StartValue>", RegexOptions.Singleline));
            Check("ConfiguredRet[21] starts TRUE", Regex.IsMatch(vdb, "Name=\"ConfiguredRet\".*?<Subelement Path=\"21\">\\s*<StartValue>true</StartValue>", RegexOptions.Singleline));
            var cdb = export("Valve_Channels_DB");
            Check("TravelTimeout[21] is 20 s", Regex.IsMatch(cdb, "Name=\"TravelTimeout\".*?<Subelement Path=\"21\">\\s*<StartValue>T#20s</StartValue>", RegexOptions.Singleline));
            Check("CM79 channels 81/82, no Healthy/Local (0/0), outputs 41/42",
                  new[] { "OpenFbChannel|81", "ClosedFbChannel|82", "HealthyChannel|0", "LocalModeChannel|0", "OpenCmdChannel|41", "CloseCmdChannel|42" }
                  .All(x => Regex.IsMatch(cdb, "Name=\"" + x.Split('|')[0] + "\".*?<Subelement Path=\"21\">\\s*<StartValue>" + x.Split('|')[1] + "</StartValue>", RegexOptions.Singleline)));

            // TIA exports SCL as tokens, and it replaces a bare %I/%Q address with a PLC tag it creates
            // (named Tag__<number>), so the address never appears as text. Rebuild the statements from
            // the tokens and swap each tag name for its real address from the PLC tag tables.
            var addr = new Dictionary<string, string>();
            Action<PlcTagTableGroup> walk = null;
            walk = g => {
                foreach (PlcTagTable t in g.TagTables) foreach (PlcTag tag in t.Tags) addr[tag.Name] = tag.LogicalAddress;
                foreach (PlcTagTableUserGroup u in g.Groups) walk(u);
            };
            walk(plc.TagTableGroup);
            var io = export("FC_PhysicalIoCopy");
            var code = string.Concat(Regex.Matches(io, "<Component Name=\"([^\"]+)\"|<Token Text=\"([^\"]+)\"|<ConstantValue[^>]*>([^<]+)<")
                .Cast<Match>().Select(m => {
                    string n = m.Groups[1].Value;
                    if (n != "") return addr.ContainsKey(n) && n.StartsWith("Tag__") ? addr[n] : n;
                    return m.Groups[2].Value != "" ? System.Net.WebUtility.HtmlDecode(m.Groups[2].Value) : m.Groups[3].Value;
                }));
            Console.WriteLine("  FC_PhysicalIoCopy: " + code);
            Check("Open limit from %I0.2, Closed limit from %I0.3",
                  code.Contains("IO_Buffer_DB.DI[81]:=%I0.2;") && code.Contains("IO_Buffer_DB.DI[82]:=%I0.3;"));
            Check("%I0.0 and %I0.1 not read", !code.Contains("%I0.0") && !code.Contains("%I0.1"));
            Check("OPEN to %Q0.0, CLOSE to %Q0.1",
                  code.Contains("%Q0.0:=IO_Buffer_DB.DQ[41];") && code.Contains("%Q0.1:=IO_Buffer_DB.DQ[42];"));
            Check("Healthy pinned TRUE, Local pinned FALSE",
                  code.Contains("Valves_DB.Valve[21].Healthy:=TRUE;") && code.Contains("Valves_DB.Valve[21].LocalMode:=FALSE;"));

            Console.WriteLine("Screens");
            var screens = hmi.Screens.Select(s => s.Name).OrderBy(s => s).ToList();
            Check("exactly the 4 screens", screens.SequenceEqual(new[] { "Screen_Alarms", "Screen_Home", "Screen_Login", "Screen_SysDiag" }), string.Join(", ", screens));
            foreach (var n in screens)
            {
                var sc = hmi.Screens.Find(n);
                bool ok = Enumerable.Range(0, 4).All(i => sc.ScreenItems.Find("Nav_" + i) != null) && sc.ScreenItems.Find("Nav_4") == null;
                Check(n + " has 4 tabs", ok);
            }
            var sys = hmi.Screens.Find("Screen_SysDiag");
            if (sys != null) Check("module reset button gone", sys.ScreenItems.Find("SD_ResetModules") == null);
            Check("start screen is Home", P(hmi.RuntimeSettings, "StartScreen") == "Screen_Home", P(hmi.RuntimeSettings, "StartScreen"));

            Console.WriteLine("Home buttons");
            var home = hmi.Screens.Find("Screen_Home");
            foreach (var x in new[] { "Btn_Open|V021_OpenCmd", "Btn_Close|V021_CloseCmd", "Btn_Reset|V021_UnexpMove" })
            {
                var b = home.ScreenItems.Find(x.Split('|')[0]);
                Check(x.Split('|')[0] + " writes " + x.Split('|')[1] + ", login " + (b == null ? "?" : P(b, "Authorization")),
                      b != null && Script(b).Contains(x.Split('|')[1]) && P(b, "Authorization") == "HMI.Operate");
            }

            Console.WriteLine("Alarms");
            var table = hmi.TagTables.Find("ValveTags");
            var alarms = hmi.DiscreteAlarms.ToList();
            Check("6 alarms, all CM79", alarms.Count == 6 && alarms.All(a => a.Name.StartsWith("V021_")), alarms.Count.ToString());
            Check("no Unhealthy or Local alarm", !alarms.Any(a => a.Name == "V021_Unhealthy" || a.Name == "V021_Local"));
            foreach (var a in alarms)
            {
                var t = table.Tags.Find(a.RaisedStateTag);
                Check(a.Name + " -> " + a.RaisedStateTag + " bit " + a.RaisedStateTagBitNumber,
                      t != null && a.RaisedStateTagBitNumber == 4 && (P(t, "PlcTag") ?? "").EndsWith("[1]"), t == null ? "TAG MISSING" : P(t, "PlcTag"));
            }

            Console.WriteLine("Tags");
            var wanted = new[] { "_OpenCmd|.OpenCmd", "_CloseCmd|.CloseCmd", "_OpenFB|.OpenFB", "_ClosedFB|.ClosedFB", "_OpenValve|.OpenValve", "_CloseValve|.CloseValve" };
            foreach (var w in wanted)
            {
                var t = table.Tags.Find("V021" + w.Split('|')[0]);
                Check("V021" + w.Split('|')[0], t != null && P(t, "PlcTag") == "Valves_DB.Valve[21]" + w.Split('|')[1] && !string.IsNullOrEmpty(P(t, "Connection")), t == null ? "missing" : P(t, "PlcTag"));
            }
            foreach (var g in new[] { "V021_Healthy", "V021_LocalMode", "Valves_DB_SelHealthy", "Valves_DB_SelLocalMode", "Valves_DB_W_Unhealthy_1", "Valves_DB_W_Local_1" })
                Check(g + " removed", table.Tags.Find(g) == null);
            Console.WriteLine("Home live signals");
            Check("4 signal lamps", home.ScreenItems.Find("D_Sig3") != null && home.ScreenItems.Find("D_Sig4") == null);
            var other = table.Tags.Count(t => Regex.IsMatch(t.Name, "^V\\d{3}_") && !t.Name.StartsWith("V021_"));
            Check("no other valve tags left", other == 0, other.ToString());
            Console.WriteLine("  tags in table: " + table.Tags.Count());
        }
    }
}
