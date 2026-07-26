using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.HmiUnified;

namespace InspectSystemFunctions
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
            Assembly asm = Assembly.LoadFrom(@"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.Hmi.dll");
            Console.WriteLine("=== System Function Types in Siemens.Engineering.Hmi.dll ===");
            foreach (Type t in asm.GetTypes())
            {
                if (t.Name.IndexOf("Sys", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("Function", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine("Type: " + t.FullName);
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                    {
                        if (m.Name.IndexOf("Popup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.Name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Console.WriteLine("   Method: " + m.Name);
                        }
                    }
                }
            }
        }
    }
}
