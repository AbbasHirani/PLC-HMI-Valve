using System;
using System.Linq;
using System.Reflection;

class ProbeGmp2 {
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

        foreach (var name in new[] { "HmiConfirmationType", "AuditConfirmationMode" }) {
            var t = all.FirstOrDefault(x => x.Name == name);
            if (t == null) { Console.WriteLine(name + ": NOT FOUND"); continue; }
            Console.WriteLine("=== " + t.FullName + (t.IsEnum ? " (enum)" : "") + " ===");
            if (t.IsEnum) foreach (var v in Enum.GetNames(t)) Console.WriteLine("   " + v);
            Console.WriteLine();
        }

        // HmiAuditTrail: what can be configured on the audit trail object itself?
        var at = all.FirstOrDefault(x => x.Name == "HmiAuditTrail");
        if (at != null) {
            Console.WriteLine("=== HmiAuditTrail members ===");
            foreach (var p in at.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Console.WriteLine("   " + p.Name + " : " + p.PropertyType.Name + (p.CanWrite ? "  [writable]" : "  [read-only]"));
        }
    }
}
