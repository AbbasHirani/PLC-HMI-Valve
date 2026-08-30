using System;
using System.Linq;
using System.Reflection;

class Program {
    static void Main() {
        var asm = Assembly.LoadFrom(@"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.dll");
        Type[] all;
        try { all = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(x => x != null).ToArray(); }
        Console.WriteLine("types loaded: " + all.Length);
        var t = all.FirstOrDefault(x => x.Name == "HmiButton");
        if (t == null) { Console.WriteLine("HmiButton not found"); return; }
        Console.WriteLine("HmiButton properties containing Visible/Enable/Focus/Operab:");
        foreach (var p in t.GetProperties().OrderBy(p => p.Name)) {
            string n = p.Name;
            if (n.IndexOf("Visib", StringComparison.OrdinalIgnoreCase) >= 0
             || n.IndexOf("Enab",  StringComparison.OrdinalIgnoreCase) >= 0
             || n.IndexOf("Focus", StringComparison.OrdinalIgnoreCase) >= 0
             || n.IndexOf("Operab",StringComparison.OrdinalIgnoreCase) >= 0)
                Console.WriteLine("  " + n.PadRight(28) + " : " + p.PropertyType.Name + (p.CanWrite ? "  (writable)" : "  (read-only)"));
        }
        Console.WriteLine();
        Console.WriteLine("MappingTableEntryRange.Value type (what a value map can carry):");
        var mte = all.FirstOrDefault(x => x.Name == "MappingTableEntryRange");
        if (mte != null)
            foreach (var p in mte.GetProperties())
                Console.WriteLine("  " + p.Name.PadRight(12) + " : " + p.PropertyType.Name);
    }
}
