using System;
using System.Reflection;
using System.IO;

namespace InspectDynamization
{
    class Program
    {
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int index = args.Name.IndexOf(',');
            string assemblyName = (index == -1) ? args.Name : args.Name.Substring(0, index);
            string defaultPath = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20";
            string assemblyPath = Path.Combine(defaultPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath)) return Assembly.LoadFrom(assemblyPath);
            
            string binPublicApiPath = @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI";
            assemblyPath = Path.Combine(binPublicApiPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath)) return Assembly.LoadFrom(assemblyPath);

            string binPath = @"C:\Program Files\Siemens\Automation\Portal V20\Bin";
            assemblyPath = Path.Combine(binPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath)) return Assembly.LoadFrom(assemblyPath);
            return null;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try
            {
                RunInspect();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex);
            }
        }

        static void RunInspect()
        {
            string defaultPath = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20";
            Assembly engAssembly = Assembly.LoadFrom(Path.Combine(defaultPath, "Siemens.Engineering.dll"));

            Console.WriteLine("Searching for types containing 'Dynamization':");
            foreach (Type type in engAssembly.GetTypes())
            {
                if (type.FullName.IndexOf("Dynamization", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine("Type: " + type.FullName + " (Base: " + (type.BaseType != null ? type.BaseType.Name : "None") + ")");
                    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        Console.WriteLine("  Prop: " + prop.PropertyType.Name + " " + prop.Name);
                    }
                }
            }
        }
    }
}
