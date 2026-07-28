using System;
using System.IO;
using System.Reflection;

namespace InspectDynamizationProps
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
                foreach (string f in Directory.GetFiles(@"C:\Program Files\Siemens\Automation\Portal V20\Bin", "*Hmi*.dll")) {
                    try {
                        Assembly a = Assembly.LoadFrom(f);
                        foreach (Type t in a.GetTypes()) {
                            if (t.Name.StartsWith("Hmi")) {
                                Console.WriteLine(t.Name + "  [" + Path.GetFileName(f) + "]");
                            }
                        }
                    } catch {}
                }
            } catch (Exception ex) {
                Console.WriteLine("Error: " + ex);
            }
        }
    }
}
