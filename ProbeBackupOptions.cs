// What values do Backup.BackupMode / Settings.StorageDevice actually accept? Reflected off the
// assembly rather than guessed - a wrong storage-device string means the audit log silently
// writes nowhere on the real panel.
using System;
using System.Linq;
using System.Reflection;

class Program {
    static void Main() {
        var asm = Assembly.LoadFrom(@"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.dll");
        Type[] all;
        try { all = asm.GetTypes(); }
        catch (ReflectionTypeLoadException rex) { all = rex.Types.Where(x => x != null).ToArray(); }

        // 1) The log-config types themselves, with each property's declared type
        Console.WriteLine("=== LogBackup / LogSegment / LogSettings property types ===");
        foreach (var t in all.Where(x => x.Name == "LogBackup" || x.Name == "LogSegment" || x.Name == "LogSettings")) {
            Console.WriteLine("\n-- " + t.FullName);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if (p.Name == "Parent") continue;
                Console.WriteLine(string.Format("   {0,-22} : {1,-34} {2}",
                    p.Name, p.PropertyType.Name, p.CanWrite ? "(rw)" : "(ro)"));
            }
        }

        // 2) Every enum that could plausibly supply those values
        Console.WriteLine("\n=== Candidate enums (backup / storage / media / device) ===");
        foreach (var t in all.Where(x => x.IsEnum).OrderBy(x => x.FullName)) {
            var n = t.Name.ToLowerInvariant();
            if (!(n.Contains("backup") || n.Contains("storage") || n.Contains("media") || n.Contains("device"))) continue;
            Console.WriteLine("\n-- " + t.FullName);
            foreach (var v in Enum.GetValues(t))
                Console.WriteLine("      " + Convert.ToInt64(v) + " = " + v);
        }
    }
}
