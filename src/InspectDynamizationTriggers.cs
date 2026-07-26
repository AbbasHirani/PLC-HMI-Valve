using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;

namespace InspectDynamizationTriggers
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
            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\hmi_trigger_details.txt";
            using (var w = new StreamWriter(outFile))
            {
                w.WriteLine("=== Trigger Properties ===");
                Type triggerType = typeof(Trigger);
                foreach (var p in triggerType.GetProperties())
                {
                    w.WriteLine(string.Format("  Property: {0,-25} | Type: {1}", p.Name, p.PropertyType.FullName));
                }
                
                // Get type of Trigger.Tags
                PropertyInfo tagsProp = triggerType.GetProperty("Tags");
                if (tagsProp != null)
                {
                    w.WriteLine("\n=== Trigger.Tags Type Details ===");
                    Type tagsType = tagsProp.PropertyType;
                    w.WriteLine("  Type Full Name: " + tagsType.FullName);
                    w.WriteLine("  Base Type: " + (tagsType.BaseType != null ? tagsType.BaseType.FullName : "none"));
                    w.WriteLine("  Properties:");
                    foreach (var p in tagsType.GetProperties())
                    {
                        w.WriteLine(string.Format("    Property: {0,-25} | Type: {1}", p.Name, p.PropertyType.FullName));
                    }
                    w.WriteLine("  Methods:");
                    foreach (var m in tagsType.GetMethods())
                    {
                        if (m.DeclaringType == tagsType)
                            w.WriteLine("    Method: " + m.Name);
                    }
                }
                
                // Check TriggerType enum values
                w.WriteLine("\n=== TriggerType Enum Values ===");
                foreach (var name in Enum.GetNames(typeof(TriggerType)))
                {
                    w.WriteLine("  " + name);
                }
            }
            Console.WriteLine("Trigger details written to " + outFile);
        }
    }
}
