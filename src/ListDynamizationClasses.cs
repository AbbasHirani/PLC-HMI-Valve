using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;

namespace ListDynamizationClasses
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
            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\hmi_dynamization_classes.txt";
            using (var w = new StreamWriter(outFile))
            {
                Assembly hmiAss = typeof(HmiSoftware).Assembly;
                w.WriteLine("=== Classes under Dynamization namespace in " + hmiAss.FullName + " ===");
                foreach (Type t in hmiAss.GetTypes())
                {
                    if (t.Namespace != null && t.Namespace.Contains("Dynamization"))
                    {
                        w.WriteLine("  Class: " + t.FullName);
                        w.WriteLine("    BaseType: " + (t.BaseType != null ? t.BaseType.FullName : "none"));
                        
                        // List properties of the class
                        w.WriteLine("    Properties:");
                        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            w.WriteLine(string.Format("      Property: {0,-30} | Type: {1}", p.Name, p.PropertyType.Name));
                        }
                    }
                }
            }
            Console.WriteLine("Dynamization classes written to " + outFile);
        }
    }
}
