using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;

namespace InspectAddresses
{
    class Program
    {
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int idx = args.Name.IndexOf(',');
            string name = idx == -1 ? args.Name : args.Name.Substring(0, idx);
            string[] bases = {
                @"D:\Siemens\Portal V20\PublicAPI\V20",
                @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20",
                @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI",
                @"C:\Program Files\Siemens\Automation\Portal V20\Bin"
            };
            foreach (var b in bases) { string p = Path.Combine(b, name + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try { Run(); } catch (Exception ex) { Console.WriteLine("[ERROR] " + ex); }
            Console.WriteLine("Press Enter to exit..."); try { Console.ReadLine(); } catch {}
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("No TIA Portal running."); return; }
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];
            Console.WriteLine("Attached: " + project.Name);
            Console.WriteLine();

            foreach (Device d in project.Devices) {
                if (d.Name.IndexOf("ET200", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Console.WriteLine("=== STATION: " + d.Name + " ===");
                foreach (DeviceItem di in d.DeviceItems) DumpItem(di, 1);
                Console.WriteLine();
            }
        }

        static void DumpItem(DeviceItem item, int depth)
        {
            string ind = new string(' ', depth * 2);
            string name = item.Name;
            bool isIoModule = name.IndexOf("DI ", StringComparison.OrdinalIgnoreCase) >= 0
                           || name.IndexOf("DQ ", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isIoModule) {
                Console.WriteLine(ind + "- " + name);
                DumpAddresses(item, depth + 1);
            }
            foreach (DeviceItem sub in item.DeviceItems) DumpItem(sub, depth + 1);
        }

        static void DumpAddresses(DeviceItem item, int depth)
        {
            string ind = new string(' ', depth * 2);
            try {
                // Address objects hang off the DeviceItem itself in V20, exposed as the
                // "Addresses" property on the item (not on AddressController as first assumed).
                var prop = item.GetType().GetProperty("Addresses");
                if (prop == null) {
                    Console.WriteLine(ind + "[no Addresses property on DeviceItem]");
                    return;
                }
                var addresses = prop.GetValue(item, null) as IEnumerable;
                if (addresses == null) { Console.WriteLine(ind + "[Addresses null]"); return; }

                bool any = false;
                foreach (var addr in addresses) {
                    any = true;
                    string ioType = GetStr(addr, "IoType");
                    string start  = GetStr(addr, "StartAddress");
                    string length = GetStr(addr, "Length");
                    string ctx    = GetStr(addr, "Context");
                    // StartAddress is in BITS for some module types and BYTES for others -
                    // print both interpretations so the mapping can be checked either way.
                    string asByte = "";
                    int s;
                    if (int.TryParse(start, out s)) {
                        asByte = "  => %I/Q byte " + s + "  (if bit-addressed: byte " + (s / 8) + ")";
                    }
                    Console.WriteLine(ind + "IoType=" + ioType + "  Start=" + start +
                                      "  Length=" + length + "  Ctx=" + ctx + asByte);
                }
                if (!any) Console.WriteLine(ind + "[no address entries]");
            } catch (Exception ex) {
                Console.WriteLine(ind + "[addr error: " + ex.Message + "]");
            }
        }

        static string GetStr(object obj, string name)
        {
            try {
                var p = obj.GetType().GetProperty(name);
                if (p == null) return "?";
                var v = p.GetValue(obj, null);
                return v != null ? v.ToString() : "null";
            } catch { return "?"; }
        }
    }
}
