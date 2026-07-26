using System;
using System.Reflection;
using System.IO;

namespace InspectSiemens
{
    class Program
    {
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int index = args.Name.IndexOf(',');
            string assemblyName = (index == -1) ? args.Name : args.Name.Substring(0, index);
            
            // Try PublicAPI V20
            string publicApiPath = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20";
            string assemblyPath = Path.Combine(publicApiPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }
            
            // Try Bin\PublicAPI folder
            string binPublicApiPath = @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI";
            assemblyPath = Path.Combine(binPublicApiPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }

            // Try Bin folder
            string binPath = @"C:\Program Files\Siemens\Automation\Portal V20\Bin";
            assemblyPath = Path.Combine(binPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }
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
            Assembly hmiAssembly = Assembly.LoadFrom(Path.Combine(defaultPath, "Siemens.Engineering.Hmi.dll"));

            Console.WriteLine("Successfully loaded Siemens assemblies.");

            Console.WriteLine("\n=== Searching Siemens.Engineering.dll for types containing Screen or Item or Faceplate ===");
            SearchAssembly(engAssembly);

            Console.WriteLine("\n=== Searching Siemens.Engineering.Hmi.dll for types containing Screen or Item or Faceplate ===");
            SearchAssembly(hmiAssembly);
        }

        static void SearchAssembly(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error getting types: " + ex.Message);
                return;
            }

            if (types == null) return;

            foreach (Type type in types)
            {
                if (type == null) continue;
                string name = type.Name;
                if (name.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Item", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Faceplate", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine("Type: " + type.FullName + " (Base: " + (type.BaseType != null ? type.BaseType.Name : "None") + ")");
                }
            }
        }
    }
}
