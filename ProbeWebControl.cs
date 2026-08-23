// Can a Custom Web Control (the Audit Viewer is one) be created on a screen via Openness?
using System;
using System.Linq;
using System.Reflection;

class Program {
    const string API_DIR = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20";
    static void Main() {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            var name = new AssemblyName(e.Name).Name + ".dll";
            foreach (var dir in new[] { API_DIR, @"C:\Program Files\Siemens\Automation\Portal V20\Bin" }) {
                var f = System.IO.Path.Combine(dir, name);
                if (System.IO.File.Exists(f)) return Assembly.LoadFrom(f);
            }
            return null;
        };
        var asm = Assembly.LoadFrom(System.IO.Path.Combine(API_DIR, "Siemens.Engineering.dll"));
        Type[] all;
        try { all = asm.GetTypes(); }
        catch (ReflectionTypeLoadException rex) { all = rex.Types.Where(x => x != null).ToArray(); }

        Console.WriteLine("=== Types mentioning Web / CustomControl ===");
        foreach (var t in all.OrderBy(x => x.FullName)) {
            string n; try { n = t.Name; } catch { continue; }
            var l = n.ToLowerInvariant();
            if (l.Contains("web") || l.Contains("customcontrol") || l.Contains("custom"))
                Console.WriteLine("   " + Safe(t));
        }

        Console.WriteLine("\n=== HmiWebControl full property list ===");
        var wc = all.FirstOrDefault(x => { try { return x.Name == "HmiWebControl"; } catch { return false; } });
        if (wc != null)
            foreach (var p in wc.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Console.WriteLine(string.Format("   {0,-28} : {1,-30} {2}",
                    p.Name, p.PropertyType.Name, p.CanWrite ? "(rw)" : "(ro)"));
        else Console.WriteLine("   not found");
    }
    static string Safe(Type t) { try { return t.FullName; } catch { return t.Name; } }
}
