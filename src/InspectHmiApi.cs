using System;
using System.IO;
using System.Reflection;
using System.Collections;

namespace InspectHmiApi
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
            string dllPath = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.HmiUnified.dll";
            Assembly asm = Assembly.LoadFrom(dllPath);
            Console.WriteLine("Assembly: " + asm.FullName);

            foreach (Type t in asm.GetTypes()) {
                if (t.Name.Contains("Event") || t.Name.Contains("Script") || t.Name.Contains("SystemFunction")) {
                    Console.WriteLine("Type: " + t.FullName);
                }
            }
        }
    }
}
