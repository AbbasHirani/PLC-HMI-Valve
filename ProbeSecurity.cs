// Offline reflection - no TIA attach. What of item 41 is reachable through Openness?
using System;
using System.Linq;
using System.Reflection;

class ProbeSecurity {
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

        Section("A. PLC protection / access level types",
                new[] { "Protection", "AccessLevel", "PasswordProvider", "Password" });

        Section("B. HMI user administration types",
                new[] { "UserAdministration", "FunctionRight", "HmiUser", "UserGroup", "HmiRole" });

        Console.WriteLine("\n=== C. Writable members: protection / access / password ===");
        Members(new[] { "Protection", "AccessLevel", "Password", "FunctionRight", "UserAdmin", "Language" });
    }

    static void Section(string title, string[] needles) {
        Console.WriteLine("\n=== " + title + " ===");
        var hits = all.Where(t => t.FullName != null &&
                    needles.Any(n => t.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                  .OrderBy(t => t.FullName).ToList();
        if (hits.Count == 0) { Console.WriteLine("   (none)"); return; }
        foreach (var t in hits) Console.WriteLine("   " + t.FullName + (t.IsEnum ? "  (enum)" : ""));
    }

    static void Members(string[] needles) {
        foreach (var t in all) {
            if (t.FullName == null) continue;
            if (!t.FullName.Contains("Siemens.Engineering")) continue;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                if (!needles.Any(n => p.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                Console.WriteLine("   " + t.Name + "." + p.Name + " : " + p.PropertyType.Name +
                                  (p.CanWrite ? "   [WRITABLE]" : "   [read-only]"));
            }
        }
    }
}
