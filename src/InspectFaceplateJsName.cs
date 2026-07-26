using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Library.Types;

namespace InspectFaceplateJsName
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
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) return;
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\faceplate_js_inspect.txt";
            using (var w = new StreamWriter(outFile))
            {
                var lib = project.ProjectLibrary;
                if (lib != null)
                {
                    foreach (var item in lib.TypeFolder.Types)
                    {
                        w.WriteLine("Type Name: " + item.Name);
                        w.WriteLine("Type Type: " + item.GetType().FullName);
                        foreach (var prop in item.GetType().GetProperties())
                        {
                            try {
                                object val = prop.GetValue(item, null);
                                w.WriteLine("  Prop: " + prop.Name + " = " + val);
                            } catch {}
                        }

                        var versionsProp = item.GetType().GetProperty("Versions");
                        if (versionsProp != null)
                        {
                            var versions = versionsProp.GetValue(item, null) as System.Collections.IEnumerable;
                            if (versions != null)
                            {
                                foreach (var v in versions)
                                {
                                    w.WriteLine("  Version Object: " + v.GetType().FullName);
                                    foreach (var vp in v.GetType().GetProperties())
                                    {
                                        try {
                                            object vval = vp.GetValue(v, null);
                                            w.WriteLine("    VProp: " + vp.Name + " = " + vval);
                                        } catch {}
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Console.WriteLine("Details written to " + outFile);
        }
    }
}
