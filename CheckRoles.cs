// Read-only. Lists project users, roles and the device function rights each role holds,
// so "grant User management" becomes a specific instruction. Writes nothing.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;

class CheckRoles {
    static string[] Bases = {
        @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20",
        @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI",
        @"C:\Program Files\Siemens\Automation\Portal V20\Bin" };
    static Assembly Resolve(object s, ResolveEventArgs a) {
        int i = a.Name.IndexOf(',');
        string n = (i == -1 ? a.Name : a.Name.Substring(0, i)) + ".dll";
        foreach (var b in Bases) { var p = Path.Combine(b, n); if (File.Exists(p)) return Assembly.LoadFrom(p); }
        return null;
    }
    static string Nm(object o) {
        try { var p = o.GetType().GetProperty("Name"); return p == null ? o.ToString() : (string)p.GetValue(o, null); }
        catch { return "?"; }
    }
    static void Main() {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("TIA not running."); return; }
        var proj = procs[0].Attach().Projects[0];
        Console.WriteLine("Project: " + proj.Name);

        // GetService<UmacConfigurator>() - resolved by reflection so a version difference
        // reports honestly instead of failing to compile.
        var asm = Assembly.LoadFrom(Path.Combine(Bases[0], "Siemens.Engineering.dll"));
        var umacType = asm.GetTypes().First(t => t.Name == "UmacConfigurator");
        object umac = null;
        try {
            var gs = proj.GetType().GetMethods().First(m => m.Name == "GetService" && m.IsGenericMethod);
            umac = gs.MakeGenericMethod(umacType).Invoke(proj, null);
        } catch (Exception e) { Console.WriteLine("GetService failed: " + e.GetType().Name); }

        if (umac == null) {
            Console.WriteLine("\nUmacConfigurator is NOT available on this project.");
            Console.WriteLine("That normally means project protection has never been enabled,");
            Console.WriteLine("so no users or roles exist yet at all.");
            return;
        }

        foreach (var coll in new[] { "ProjectUsers", "SystemRoles", "CustomRoles", "CustomDeviceFunctionRights" }) {
            Console.WriteLine("\n=== " + coll + " ===");
            object v = null;
            try { v = umacType.GetProperty(coll).GetValue(umac, null); }
            catch (Exception e) { Console.WriteLine("   <" + e.GetType().Name + ">"); continue; }
            var en = v as IEnumerable;
            if (en == null) { Console.WriteLine("   (not enumerable)"); continue; }
            int n = 0;
            foreach (var o in en) {
                n++;
                Console.WriteLine("   - " + Nm(o));
                // roles carry function-right associations; print them if present
                foreach (var pn in new[] { "DeviceFunctionRights", "EngineeringFunctionRights", "FunctionRights" }) {
                    var pr = o.GetType().GetProperty(pn);
                    if (pr == null) continue;
                    var sub = pr.GetValue(o, null) as IEnumerable;
                    if (sub == null) continue;
                    foreach (var f in sub) Console.WriteLine("        * " + pn + ": " + Nm(f));
                }
            }
            if (n == 0) Console.WriteLine("   (empty)");
        }
        Console.WriteLine("\nRead-only. Nothing written.");
    }
}
