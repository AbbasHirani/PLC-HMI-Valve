// Read-only: shows each running TIA process and the real reason an attach succeeds or fails.
// The other probes swallow this exception and report "No open project", which hides the cause.
using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;

namespace ProbeAttach
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
            Run();
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            Console.WriteLine("TIA processes visible to Openness: " + procs.Count);
            foreach (var p in procs)
            {
                Console.WriteLine("--- PID " + p.Id + "  mode=" + p.Mode + "  attached=" + p.AttachedSessions.Count);
                Console.WriteLine("    project path: " + (p.ProjectPath == null ? "<none>" : p.ProjectPath.FullName));
                try
                {
                    var a = p.Attach();
                    Console.WriteLine("    attach OK, projects open: " + a.Projects.Count);
                    foreach (var pr in a.Projects) Console.WriteLine("      " + pr.Name);
                    a.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("    ATTACH FAILED: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
    }
}
