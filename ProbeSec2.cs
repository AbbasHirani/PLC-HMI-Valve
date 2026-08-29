using System;
using System.Linq;
using System.Reflection;

class ProbeSec2 {
    const string API = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20";
    static Type[] all;
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
        try { all = asm.GetTypes(); }
        catch (ReflectionTypeLoadException r) { all = r.Types.Where(x => x != null).ToArray(); }

        foreach (var name in new[] { "PlcProtectionAccessLevel", "PlcAccessLevelProvider",
                                     "DeviceUserGroup", "PlcPasswordPolicyService", "UmacProvider" })
            Dump(name);

        Console.WriteLine("\n=== anything with 'Umac' (user mgmt access control) ===");
        foreach (var t in all.Where(t => t.FullName != null && t.FullName.Contains("Umac")).OrderBy(t => t.FullName))
            Console.WriteLine("   " + t.FullName);
    }

    static void Dump(string name) {
        var t = all.FirstOrDefault(x => x.Name == name);
        Console.WriteLine("\n=== " + name + " ===");
        if (t == null) { Console.WriteLine("   NOT FOUND"); return; }
        if (t.IsEnum) { foreach (var v in Enum.GetNames(t)) Console.WriteLine("   " + v); return; }
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Console.WriteLine("   P  " + p.Name + " : " + p.PropertyType.Name + (p.CanWrite ? "  [WRITABLE]" : "  [read-only]"));
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
            if (m.IsSpecialName) continue;
            Console.WriteLine("   M  " + m.Name + "(" +
                string.Join(", ", m.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name)) + ")");
        }
    }
}
