using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;

namespace InspectScriptSysFct
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
            var processes = TiaPortal.GetProcesses();
            if (processes.Count == 0) { Console.WriteLine("No TIA Portal found."); return; }
            var portal = processes[0].Attach();
            var project = portal.Projects[0];
            Console.WriteLine("Attached to Project: " + project.Name);

            HmiSoftware hmi = null;
            foreach (var d in project.Devices)
            {
                foreach (var item in d.Items)
                {
                    var softwareContainer = item.GetService<HmiSoftware>();
                    if (softwareContainer != null) { hmi = softwareContainer; break; }
                }
                if (hmi != null) break;
            }

            if (hmi == null) { Console.WriteLine("HmiSoftware not found."); return; }
            foreach (var sc in hmi.Screens)
            {
                Console.WriteLine("Screen: " + sc.Name);
                foreach (var item in sc.ScreenItems)
                {
                    if (item.Name.StartsWith("Btn_") || item.Name.StartsWith("FPC_"))
                    {
                        Console.WriteLine("  Item: " + item.Name + " (" + item.GetType().Name + ")");
                        try {
                            PropertyInfo evProp = item.GetType().GetProperty("EventHandlers");
                            if (evProp != null)
                            {
                                object evObj = evProp.GetValue(item, null);
                                IEnumerable evList = evObj as IEnumerable;
                                if (evList != null)
                                {
                                    foreach (var h in evList)
                                    {
                                        var sp = h.GetType().GetProperty("Script");
                                        if (sp != null)
                                        {
                                            object scObj = sp.GetValue(h, null);
                                            var scp = scObj.GetType().GetProperty("ScriptCode");
                                            if (scp != null)
                                            {
                                                string code = (string)scp.GetValue(scObj, null);
                                                Console.WriteLine("     ScriptCode: " + code);
                                            }
                                        }
                                    }
                                }
                            }
                        } catch (Exception ex) { Console.WriteLine("     Error: " + ex.Message); }
                    }
                }
            }
        }
    }
}
