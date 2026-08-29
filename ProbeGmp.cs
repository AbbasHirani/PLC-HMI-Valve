// Offline reflection - no TIA attach, no crash risk. What does Openness expose for GMP?
using System;
using System.Linq;
using System.Reflection;

class ProbeGmp {
    const string API = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20";
    static void Main() {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            var n = new AssemblyName(e.Name).Name + ".dll";
            foreach (var d in new[] { API, @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI",
                                      @"C:\Program Files\Siemens\Automation\Portal V20\Bin" }) {
                var f = System.IO.Path.Combine(d, n);
                if (System.IO.File.Exists(f)) return Assembly.LoadFrom(f);
            }
            return null;
        };
        var asm = Assembly.LoadFrom(System.IO.Path.Combine(API, "Siemens.Engineering.dll"));
        Type[] all;
        try { all = asm.GetTypes(); }
        catch (ReflectionTypeLoadException r) { all = r.Types.Where(x => x != null).ToArray(); }

        Console.WriteLine("=== Types mentioning GMP / Audit / Reason / Signature ===");
        foreach (var t in all.Where(x => x.FullName != null &&
                (x.Name.IndexOf("GMP", StringComparison.OrdinalIgnoreCase) >= 0
              || x.Name.IndexOf("Audit", StringComparison.OrdinalIgnoreCase) >= 0
              || x.Name.IndexOf("Reason", StringComparison.OrdinalIgnoreCase) >= 0
              || x.Name.IndexOf("Signature", StringComparison.OrdinalIgnoreCase) >= 0)).OrderBy(x => x.FullName))
            Console.WriteLine("  " + t.FullName);

        Console.WriteLine("\n=== Members mentioning GMP / Reason / Signature / Confirm ===");
        foreach (var t in all) {
            if (t.FullName == null || !t.FullName.Contains("HmiUnified")) continue;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                string n = p.Name;
                if (n.IndexOf("GMP", StringComparison.OrdinalIgnoreCase) >= 0
                 || n.IndexOf("Reason", StringComparison.OrdinalIgnoreCase) >= 0
                 || n.IndexOf("Signature", StringComparison.OrdinalIgnoreCase) >= 0
                 || n.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0
                 || n.IndexOf("Audit", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  " + t.Name + "." + n + "   : " + p.PropertyType.Name +
                                      (p.CanWrite ? "  [writable]" : "  [read-only]"));
            }
        }
    }
}
