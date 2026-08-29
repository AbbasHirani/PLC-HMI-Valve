using System;
using System.Linq;
using System.Reflection;

class ProbeRoles {
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
        try { all = asm.GetTypes(); } catch (ReflectionTypeLoadException r) { all = r.Types.Where(x => x != null).ToArray(); }

        var proj = all.First(t => t.FullName == "Siemens.Engineering.Project");
        Console.WriteLine("=== Project members mentioning user / role / security / umac ===");
        foreach (var p in proj.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (new[]{"User","Role","Secur","Umac"}.Any(n => p.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                Console.WriteLine("   P " + p.Name + " : " + p.PropertyType.Name);
        foreach (var m in proj.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (new[]{"User","Role","Secur","Umac","Service"}.Any(n => m.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                Console.WriteLine("   M " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(x => x.ParameterType.Name)) + ")");

        Console.WriteLine("\n=== SystemDeviceFunctionRight / DeviceFunctionRight members ===");
        foreach (var nm in new[]{"SystemDeviceFunctionRight","DeviceFunctionRight","Role","UmacConfigurator"}) {
            var t = all.FirstOrDefault(x => x.Name == nm);
            if (t == null) { Console.WriteLine("  " + nm + ": not found"); continue; }
            Console.WriteLine("  --- " + t.FullName + " ---");
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Console.WriteLine("     " + p.Name + " : " + p.PropertyType.Name + (p.CanWrite ? " [W]" : ""));
        }
    }
}
