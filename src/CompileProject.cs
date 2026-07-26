using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.Compiler;

namespace CompileProject
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
            if (procs.Count == 0) { Console.WriteLine("No TIA Portal process."); return; }
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];

            Console.WriteLine("Compiling project devices...");
            foreach (Device d in project.Devices)
            {
                Console.WriteLine("Checking device: " + d.Name);
                var cp = d.GetService<CompileProvider>();
                if (cp != null)
                {
                    Console.WriteLine("  Compiling device " + d.Name + "...");
                    CompilerResult res = cp.Compile();
                    Console.WriteLine(string.Format("  Result for {0}: State={1}, Errors={2}, Warnings={3}",
                        d.Name, res.State, res.ErrorCount, res.WarningCount));
                }
            }
        }
    }
}
