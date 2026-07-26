using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.Library.Types;

namespace InspectFaceplateCanvasSize
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

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\faceplate_canvas_inspect.txt";
            using (var w = new StreamWriter(outFile))
            {
                w.WriteLine("=== Faceplate Types Inspection ===");
                var lib = project.ProjectLibrary;
                if (lib != null)
                {
                    foreach (var t in lib.TypeFolder.Types)
                    {
                        w.WriteLine("Type: " + t.Name);
                        foreach (var v in t.Versions)
                        {
                            w.WriteLine("  Version: " + v.VersionNumber + " (Guid: " + v.Guid + ", Default: " + v.IsDefault + ")");
                            foreach (var p in v.GetType().GetProperties())
                            {
                                try {
                                    object val = p.GetValue(v, null);
                                    w.WriteLine("    Prop: " + p.Name + " = " + (val ?? "null"));
                                } catch {}
                            }
                        }
                    }
                }
            }
            Console.WriteLine("Details written to " + outFile);
        }
    }
}
