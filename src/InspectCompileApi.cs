using System;
using System.Reflection;
using Siemens.Engineering;

namespace InspectCompileApi
{
    class Program
    {
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
                string name = e.Name.Split(',')[0];
                string path = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\" + name + ".dll";
                return System.IO.File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            try {
                Assembly asm = Assembly.LoadFrom(@"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.dll");
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                foreach (Type t in types) {
                    if (t != null && t.Name.IndexOf("Compile", StringComparison.OrdinalIgnoreCase) >= 0) {
                        Console.WriteLine("Type: " + t.FullName);
                    }
                }
            } catch (Exception ex) { Console.WriteLine(ex); }
        }
    }
}
