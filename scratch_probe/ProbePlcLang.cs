// Read-only: dumps EVERY attribute on the PLC device item and PlcSoftware, unfiltered.
// The "runtime language does not match the connected PLC" warning points at something on the
// PLC side, and a name filter of "lang" found nothing - so look at all of them.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

namespace ProbePlcLang
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

        static void AllAttrs(IEngineeringObject eo, string label)
        {
            Console.WriteLine();
            Console.WriteLine("### " + label);
            try
            {
                var infos = eo.GetAttributeInfos().OrderBy(a => a.Name).ToList();
                Console.WriteLine("    (" + infos.Count + " attributes)");
                foreach (var ai in infos)
                {
                    string v;
                    try
                    {
                        var raw = eo.GetAttribute(ai.Name);
                        if (raw is IEnumerable && !(raw is string))
                            v = "[" + string.Join(", ", (from object x in (IEnumerable)raw select x.ToString()).ToArray()) + "]";
                        else v = raw == null ? "<null>" : raw.ToString();
                    }
                    catch (Exception ex) { v = "<err: " + ex.GetType().Name + ">"; }
                    if (v.Length > 110) v = v.Substring(0, 110) + "...";
                    Console.WriteLine("    " + ai.Name.PadRight(38) + " = " + v);
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

            foreach (Device d in project.Devices)
            {
                if (d.Name.IndexOf("S7-1200", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Console.WriteLine("DEVICE: " + d.Name);
                AllAttrs(d, "Device \"" + d.Name + "\"");
                foreach (DeviceItem it in d.DeviceItems) Walk(it);
            }
        }

        static void Walk(DeviceItem it)
        {
            // Only the CPU item itself is interesting; the HSC/Pulse children are noise.
            if (it.Name.IndexOf("PLC_1", StringComparison.OrdinalIgnoreCase) >= 0
                || it.Name.IndexOf("Rack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AllAttrs(it, "DeviceItem \"" + it.Name + "\"");

                // MultilingualSupport is the PLC-side language table the HMI compile warning
                // complains about. It comes back as TableData, so walk it structurally.
                try
                {
                    var ms = it.GetAttribute("MultilingualSupport");
                    if (ms != null)
                    {
                        Console.WriteLine();
                        Console.WriteLine("### MultilingualSupport  (" + ms.GetType().FullName + ")");
                        foreach (var p in ms.GetType().GetProperties())
                        {
                            if (p.GetIndexParameters().Length > 0) continue;
                            object raw = null;
                            try { raw = p.GetValue(ms, null); } catch (Exception ex) { Console.WriteLine("    " + p.Name + " = <err " + ex.GetType().Name + ">"); continue; }
                            if (raw is IEnumerable && !(raw is string))
                            {
                                int r = 0;
                                Console.WriteLine("    " + p.Name + ":");
                                foreach (var row in (IEnumerable)raw)
                                {
                                    r++;
                                    Console.WriteLine("      row " + r + ":");
                                    // StructuredData rows expose their columns as ENGINEERING
                                    // ATTRIBUTES, not CLR properties - reflection over properties
                                    // only ever shows Parent, which is why the first pass looked empty.
                                    var reo = row as IEngineeringObject;
                                    if (reo != null)
                                    {
                                        foreach (var ai in reo.GetAttributeInfos())
                                        {
                                            string rv;
                                            try { var x = reo.GetAttribute(ai.Name); rv = x == null ? "<null>" : x.ToString(); }
                                            catch (Exception ex) { rv = "<err " + ex.GetType().Name + ">"; }
                                            Console.WriteLine("           " + ai.Name.PadRight(28) + " = " + rv);
                                        }
                                    }
                                    else Console.WriteLine("           <not an IEngineeringObject: " + row.GetType().FullName + ">");
                                }
                                if (r == 0) Console.WriteLine("      (EMPTY - no rows)");
                            }
                            else Console.WriteLine("    " + p.Name.PadRight(24) + " = " + (raw == null ? "<null>" : raw.ToString()));
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine("### MultilingualSupport unreadable: " + ex.Message); }

                var sc = it.GetService<SoftwareContainer>();
                if (sc != null && sc.Software is PlcSoftware)
                    AllAttrs((IEngineeringObject)sc.Software, "PlcSoftware \"" + sc.Software.Name + "\"");
            }
            foreach (DeviceItem s in it.DeviceItems) Walk(s);
        }
    }
}
