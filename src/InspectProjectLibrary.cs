using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;

namespace InspectProjectLibrary
{
    class Program
    {
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int idx = args.Name.IndexOf(',');
            string name = idx == -1 ? args.Name : args.Name.Substring(0, idx);
            string[] bases = {
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
            Console.WriteLine("Press Enter..."); try { Console.ReadLine(); } catch {}
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("No TIA Portal running."); return; }
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];

            Console.WriteLine("=== Project Library Types ===");
            var lib = project.ProjectLibrary;
            InspectFolder(lib.TypeFolder, "");
        }

        static void InspectFolder(object folder, string indent)
        {
            if (folder == null) return;
            string name = GetPropStr(folder, "Name");
            Console.WriteLine(indent + "[Folder] " + name);

            var typesProp = folder.GetType().GetProperty("Types");
            if (typesProp != null) {
                var types = typesProp.GetValue(folder, null) as IEnumerable;
                if (types != null) {
                    foreach (var t in types) {
                        string tName = GetPropStr(t, "Name");
                        Console.WriteLine(indent + "  - Type: '" + tName + "' (Type: " + t.GetType().Name + ")");
                        var versionsProp = t.GetType().GetProperty("Versions");
                        if (versionsProp != null) {
                            var versions = versionsProp.GetValue(t, null) as IEnumerable;
                            if (versions != null) {
                                foreach (var v in versions) {
                                    string vName = GetPropStr(v, "Name");
                                    string vVersion = GetPropStr(v, "VersionNumber");
                                    Console.WriteLine(indent + "      Version Name: '" + vName + "', VersionNumber: '" + vVersion + "'");
                                }
                            }
                        }
                    }
                }
            }

            var foldersProp = folder.GetType().GetProperty("Folders");
            if (foldersProp != null) {
                var subFolders = foldersProp.GetValue(folder, null) as IEnumerable;
                if (subFolders != null) {
                    foreach (var sf in subFolders) {
                        InspectFolder(sf, indent + "  ");
                    }
                }
            }
        }

        static string GetPropStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }
    }
}
