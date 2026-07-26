using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Controls;

namespace InspectHmiFaceplateContainerProps
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
            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\faceplate_container_properties.txt";
            using (var w = new StreamWriter(outFile))
            {
                Type type = typeof(HmiFaceplateContainer);
                w.WriteLine("=== HmiFaceplateContainer Properties ===");
                var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                Array.Sort(props, (a, b) => string.Compare(a.Name, b.Name));
                foreach (var p in props)
                {
                    w.WriteLine(string.Format("  Name: {0,-35} | Type: {1}", p.Name, p.PropertyType.FullName));
                }
                
                w.WriteLine("\n=== HmiFaceplateContainer Methods ===");
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                Array.Sort(methods, (a, b) => string.Compare(a.Name, b.Name));
                foreach (var m in methods)
                {
                    if (m.DeclaringType == type)
                    {
                        w.WriteLine("  Method: " + m.Name);
                    }
                }
            }
            Console.WriteLine("Properties written to " + outFile);
        }
    }
}
