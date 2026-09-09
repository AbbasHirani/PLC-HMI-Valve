// Read-only: dumps project / PLC / HMI language configuration so the runtime-language
// compile warning can be diagnosed instead of guessed at. Touches nothing, never saves.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.SW;

namespace ProbeLanguages
{
    class Program
    {
        static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int i = args.Name.IndexOf(',');
            string n = i == -1 ? args.Name : args.Name.Substring(0, i);
            string[] bases = {
                @"D:\Siemens\Portal V20\PublicAPI\V20",
                @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20"
            };
            foreach (var b in bases) { string p = Path.Combine(b, n + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        }

        static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try { Run(); } catch (Exception ex) { Console.WriteLine("[ERROR] " + ex.Message); Environment.ExitCode = 1; }
        }

        static void Dump(object o, string label)
        {
            if (o == null) { Console.WriteLine(label + ": <null>"); return; }
            Console.WriteLine(label + "  (" + o.GetType().Name + ")");
            foreach (var p in o.GetType().GetProperties())
            {
                if (p.GetIndexParameters().Length > 0) continue;
                string v;
                try
                {
                    var raw = p.GetValue(o, null);
                    if (raw is IEnumerable && !(raw is string))
                    {
                        var parts = (from object x in (IEnumerable)raw select x.ToString()).ToList();
                        v = "[" + string.Join(", ", parts) + "]  (" + parts.Count + ")";
                    }
                    else v = raw == null ? "<null>" : raw.ToString();
                }
                catch (Exception ex) { v = "<err: " + ex.GetType().Name + ">"; }
                if (v.Length > 140) v = v.Substring(0, 140) + "...";
                Console.WriteLine("    " + p.Name.PadRight(26) + " = " + v);
            }
        }

        static void DumpAttrs(IEngineeringObject eo, string label, string filter)
        {
            Console.WriteLine(label);
            try
            {
                foreach (var ai in eo.GetAttributeInfos())
                {
                    string name = ai.Name;
                    if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string v;
                    try
                    {
                        var raw = eo.GetAttribute(name);
                        if (raw is IEnumerable && !(raw is string))
                            v = "[" + string.Join(", ", (from object x in (IEnumerable)raw select x.ToString()).ToArray()) + "]";
                        else v = raw == null ? "<null>" : raw.ToString();
                    }
                    catch (Exception ex) { v = "<err: " + ex.GetType().Name + ">"; }
                    Console.WriteLine("    " + name.PadRight(34) + " = " + v);
                }
            }
            catch (Exception ex) { Console.WriteLine("    <cannot enumerate: " + ex.Message + ">"); }
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("TIA Portal not running."); return; }
            Project project = null;
            foreach (var p in procs)
            {
                try { var a = p.Attach(); if (a != null && a.Projects.Count > 0) { project = a.Projects[0]; break; } }
                catch { }
            }
            if (project == null) { Console.WriteLine("No open project."); return; }

            Console.WriteLine("PROJECT: " + project.Name);
            Console.WriteLine(new string('=', 70));
            Dump(project.LanguageSettings, "project.LanguageSettings");
            Console.WriteLine();
            Console.WriteLine("  ActiveLanguages:");
            foreach (var l in project.LanguageSettings.ActiveLanguages) Console.WriteLine("    - " + l.Culture);
            Console.WriteLine("  EditingLanguage  = " + project.LanguageSettings.EditingLanguage.Culture);
            Console.WriteLine("  ReferenceLanguage= " + project.LanguageSettings.ReferenceLanguage.Culture);

            foreach (Device d in project.Devices)
            {
                Console.WriteLine();
                Console.WriteLine(new string('=', 70));
                Console.WriteLine("DEVICE: " + d.Name);
                foreach (DeviceItem it in d.DeviceItems) Walk(it, "  ");
            }
        }

        static void Walk(DeviceItem it, string ind)
        {
            var sc = it.GetService<SoftwareContainer>();
            if (sc != null && sc.Software is HmiSoftware)
            {
                var hmi = (HmiSoftware)sc.Software;
                Console.WriteLine(ind + "[HMI software] " + hmi.Name);
                DumpAttrs(hmi, ind + "  HmiSoftware attributes (language*):", "lang");
                // Runtime settings often hang off a child object
                foreach (var p in hmi.GetType().GetProperties())
                {
                    if (p.Name.IndexOf("Language", StringComparison.OrdinalIgnoreCase) < 0
                        && p.Name.IndexOf("Runtime", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    object rs = null;
                    try { rs = p.GetValue(hmi, null); } catch { }
                    Dump(rs, ind + "  hmi." + p.Name);
                    if (rs == null) continue;

                    // The runtime language list itself - this is what the compile warning is about.
                    var lafProp = rs.GetType().GetProperty("LanguageAndFonts");
                    if (lafProp == null) continue;
                    var seq = lafProp.GetValue(rs, null) as IEnumerable;
                    if (seq == null) continue;
                    Console.WriteLine();
                    Console.WriteLine(ind + "  >>> RUNTIME LANGUAGES (LanguageAndFonts):");
                    int n = 0;
                    foreach (var entry in seq)
                    {
                        n++;
                        Dump(entry, ind + "    [" + n + "]");
                    }
                    Console.WriteLine(ind + "  >>> total runtime language entries: " + n);
                }
            }
            if (sc != null && sc.Software is PlcSoftware)
            {
                var plc = (PlcSoftware)sc.Software;
                Console.WriteLine(ind + "[PLC software] " + plc.Name);
                DumpAttrs(plc, ind + "  PlcSoftware attributes (language*):", "lang");
            }
            DumpAttrs(it, ind + "DeviceItem \"" + it.Name + "\" (language*):", "lang");
            foreach (DeviceItem s in it.DeviceItems) Walk(s, ind + "  ");
        }
    }
}
