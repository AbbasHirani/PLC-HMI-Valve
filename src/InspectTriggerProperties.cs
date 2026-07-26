using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;

namespace InspectTriggerProperties
{
    class Program
    {
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int i = args.Name.IndexOf(',');
            string n = i == -1 ? args.Name : args.Name.Substring(0, i);
            string[] dirs = {
                @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20",
                @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI",
                @"C:\Program Files\Siemens\Automation\Portal V20\Bin"
            };
            foreach (var d in dirs) { string p = Path.Combine(d, n + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try {
                Assembly asm = typeof(HmiSoftware).Assembly;
                Console.WriteLine("Assembly: " + asm.FullName);
                foreach (Type t in asm.GetTypes())
                {
                    if (t.Name.IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Console.WriteLine("Type: " + t.FullName);
                        foreach (var p in t.GetProperties())
                        {
                            Console.WriteLine("   Prop: " + p.Name + " (" + p.PropertyType.Name + ")");
                        }
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine("Error: " + ex);
            }
        }
    }
}
