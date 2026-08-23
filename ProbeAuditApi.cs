using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

class Program {
    static void Main() {
        string[] dlls = {
            @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.dll",
            @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.Hmi.dll",
        };
        var seenTypes = new HashSet<string>();
        foreach (var d in dlls) {
            Assembly asm;
            try { asm = Assembly.LoadFrom(d); }
            catch (Exception e) { Console.WriteLine("LOADFAIL " + d + " " + e.Message); continue; }
            Type[] all;
            try { all = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rex) { all = rex.Types.Where(x => x != null).ToArray(); }

            Console.WriteLine("###### ASSEMBLY: " + System.IO.Path.GetFileName(d) + "  types=" + all.Length);

            // 1) Any TYPE whose name mentions audit/gmp/signature/confirmation
            Console.WriteLine("\n--- TYPES matching audit/gmp/signature/confirm/electronic ---");
            foreach (var t in all.OrderBy(x => x.FullName)) {
                string n = t.Name.ToLowerInvariant();
                if (n.Contains("audit") || n.Contains("gmp") || n.Contains("signature")
                    || n.Contains("confirm") || n.Contains("electronic")) {
                    Console.WriteLine("  " + t.FullName + (t.IsEnum ? "   [ENUM]" : ""));
                    if (t.IsEnum)
                        foreach (var v in Enum.GetValues(t))
                            Console.WriteLine("        " + Convert.ToInt64(v) + " = " + v);
                }
            }

            // 2) Any PROPERTY on any type whose name mentions those words
            Console.WriteLine("\n--- PROPERTIES matching audit/gmp/signature/confirm/electronic ---");
            foreach (var t in all.OrderBy(x => x.FullName)) {
                PropertyInfo[] props;
                try { props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly); }
                catch { continue; }
                foreach (var p in props) {
                    string n = p.Name.ToLowerInvariant();
                    if (n.Contains("audit") || n.Contains("gmp") || n.Contains("signature")
                        || n.Contains("confirm") || n.Contains("electronic")) {
                        Console.WriteLine(string.Format("  {0,-55} . {1,-30} : {2}   {3}",
                            t.Name, p.Name, p.PropertyType.Name, p.CanWrite ? "(rw)" : "(ro)"));
                    }
                }
            }
        }
    }
}
