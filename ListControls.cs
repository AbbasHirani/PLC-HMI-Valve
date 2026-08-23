// Is there a runtime control that DISPLAYS the Audit Trail, the way HmiAlarmControl displays
// alarms? The client wants to read the logs on the panel, which is a different problem from
// writing them. Reflected off the assembly - offline, no TIA attach, no crash risk.
using System;
using System.Linq;
using System.Reflection;

class Program {
    const string API_DIR = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20";

    static void Main() {
        // Siemens.Engineering references Siemens.Engineering.Contract, which does not sit beside
        // the exe - resolve it out of the API folder or GetTypes() dies part-way through.
        AppDomain.CurrentDomain.AssemblyResolve += (s2, e) => {
            var name = new AssemblyName(e.Name).Name + ".dll";
            foreach (var dir in new[] { API_DIR, @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI",
                                        @"C:\Program Files\Siemens\Automation\Portal V20\Bin" }) {
                var f = System.IO.Path.Combine(dir, name);
                if (System.IO.File.Exists(f)) return Assembly.LoadFrom(f);
            }
            return null;
        };
        var asm = Assembly.LoadFrom(System.IO.Path.Combine(API_DIR, "Siemens.Engineering.dll"));
        Type[] all;
        try { all = asm.GetTypes(); }
        catch (ReflectionTypeLoadException rex) { all = rex.Types.Where(x => x != null).ToArray(); }

        Console.WriteLine("=== Every screen-item type under UI.Controls / UI.Widgets ===");
        foreach (var t in all.Where(x => Ns(x) != null
                                      && (Ns(x).Contains("HmiUnified.UI.Controls")
                                       || Ns(x).Contains("HmiUnified.UI.Widgets")))
                             .OrderBy(x => x.Name)) {
            if (t.Name.EndsWith("FactoryFacade") || t.Name.EndsWith("Composition")) continue;
            Console.WriteLine("   " + Ns(t) + "." + t.Name);
        }

        Console.WriteLine("\n=== Anything named *Control anywhere in HmiUnified ===");
        foreach (var t in all.Where(x => Ns(x) != null && Ns(x).Contains("HmiUnified") && x.Name.EndsWith("Control"))
                             .OrderBy(x => x.FullName))
            Console.WriteLine("   " + t.FullName);

        Console.WriteLine("\n=== Types mentioning log/journal/report/trail/protocol ===");
        foreach (var t in all.Where(x => Ns(x) != null && Ns(x).Contains("HmiUnified"))
                             .OrderBy(x => x.FullName)) {
            var n = t.Name.ToLowerInvariant();
            if (n.Contains("journal") || n.Contains("report") || n.Contains("trail")
                || n.Contains("protocol") || n.Contains("logview") || n.Contains("logcontrol"))
                Console.WriteLine("   " + t.FullName);
        }
    }

    static string Ns(Type t) { try { return t.Namespace; } catch { return null; } }
}
