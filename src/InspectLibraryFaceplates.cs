using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;

namespace InspectLibraryFaceplates
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
            if (procs.Count == 0) return;
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\library_faceplates.txt";
            using (var w = new StreamWriter(outFile))
            {
                w.WriteLine("=== PROJECT LIBRARY TYPES ===");
                var lib = project.ProjectLibrary;
                if (lib != null)
                {
                    var typesFolder = lib.TypeFolder;
                    DumpFolder(typesFolder, w, 0);
                }
            }
            Console.WriteLine("Library details written to " + outFile);
        }

        static void DumpFolder(object folder, StreamWriter w, int indent)
        {
            string ind = new string(' ', indent * 2);
            var nameProp = folder.GetType().GetProperty("Name");
            string fName = nameProp != null ? (nameProp.GetValue(folder, null) ?? "").ToString() : "Folder";
            w.WriteLine(ind + "+ Folder: " + fName);

            // Types
            var typesProp = folder.GetType().GetProperty("Types");
            if (typesProp != null) {
                var types = typesProp.GetValue(folder, null) as IEnumerable;
                if (types != null) {
                    foreach (var t in types) {
                        var tn = GetPropStr(t, "Name");
                        var tt = t.GetType().Name;
                        w.WriteLine(ind + "  - Type: " + tn + " (" + tt + ")");
                    }
                }
            }

            // Subfolders
            var foldersProp = folder.GetType().GetProperty("Folders");
            if (foldersProp != null) {
                var subFolders = foldersProp.GetValue(folder, null) as IEnumerable;
                if (subFolders != null) {
                    foreach (var sf in subFolders) {
                        DumpFolder(sf, w, indent + 1);
                    }
                }
            }
        }

        static string GetPropStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }
    }
}
