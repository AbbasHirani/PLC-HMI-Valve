using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;

namespace InspectHmiSysFctNames
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
            // Inspect types in Siemens.Engineering.HmiUnified.dll for SysFct or SystemFunction names
            Assembly asm = Assembly.LoadFrom(@"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.Hmi.dll");
            Console.WriteLine("=== Searching Siemens.Engineering.Hmi.dll for SysFct / Popup types ===");
            foreach (Type t in asm.GetTypes())
            {
                if (t.Name.IndexOf("Popup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("SysFct", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine("Type: " + t.FullName);
                }
            }
        }
    }
}
