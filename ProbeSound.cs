using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

// The 2026-08-01 / 08-29 probe searched only for "Acoustic" and found nothing. That may have been
// the wrong word rather than proof of absence. This widens the search across every type in the
// Siemens assemblies, and dumps HmiAlarmClass in full so nothing is missed by keyword choice.
class Program {
    static readonly string[] Terms = {
        "acoustic","sound","buzz","audib","audio","signal","horn","beep","tone","siren","annunci"
    };

    static void Main() {
        string dir = FindDir();
        if (dir == null) { Console.WriteLine("Openness dir not found"); return; }
        Console.WriteLine("Scanning: " + dir);
        Console.WriteLine();

        var seen = new HashSet<string>();
        foreach (var file in Directory.GetFiles(dir, "Siemens*.dll")) {
            Type[] types;
            try { types = Assembly.LoadFrom(file).GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            catch { continue; }

            foreach (var t in types) {
                if (t.FullName == null) continue;
                foreach (var term in Terms) {
                    if (t.FullName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                        && seen.Add("T:" + t.FullName))
                        Console.WriteLine("  TYPE     " + t.FullName);
                }
                PropertyInfo[] props;
                try { props = t.GetProperties(); } catch { continue; }
                foreach (var p in props)
                    foreach (var term in Terms)
                        if (p.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                            && seen.Add("P:" + t.FullName + "." + p.Name))
                            Console.WriteLine("  PROPERTY " + t.Name + "." + p.Name + " : " + p.PropertyType.Name);
            }
        }
        Console.WriteLine();
        Console.WriteLine("  matches: " + seen.Count);

        // Whatever the keyword search says, dump the alarm-class surface in full.
        foreach (var file in Directory.GetFiles(dir, "Siemens*.dll")) {
            Type[] types;
            try { types = Assembly.LoadFrom(file).GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            catch { continue; }
            foreach (var t in types) {
                if (t.Name != "HmiAlarmClass" && t.Name != "HmiRaisedState" && t.Name != "HmiAcknowledgedState"
                 && t.Name != "HmiClearedState" && t.Name != "HmiAcknowledgedClearedState") continue;
                Console.WriteLine();
                Console.WriteLine("=== " + t.FullName + " ===");
                foreach (var p in t.GetProperties().OrderBy(x => x.Name))
                    Console.WriteLine("  " + p.Name.PadRight(34) + " : " + p.PropertyType.Name
                                      + (p.CanWrite ? "  (writable)" : ""));
            }
        }
    }

    static string FindDir() {
        var c = new List<string>();
        string env = Environment.GetEnvironmentVariable("VALVEDEMO_OPENNESS");
        if (!string.IsNullOrEmpty(env)) c.Add(env);
        c.Add(@"D:\Siemens\Portal V20\PublicAPI\V20");
        c.Add(@"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20");
        foreach (var d in c) if (Directory.Exists(d) && File.Exists(Path.Combine(d, "Siemens.Engineering.dll"))) return d;
        return null;
    }
}
