using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.Library.Types;

namespace ExportFaceplateXml
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

            string outDir = @"C:\Users\Admin\Documents\Automation\valveDemo2";
            var lib = project.ProjectLibrary;
            var typesFolder = lib.TypeFolder;
            FindAndExport(typesFolder, outDir);
        }

        static void FindAndExport(object folder, string outDir)
        {
            var typesProp = folder.GetType().GetProperty("Types");
            if (typesProp != null) {
                var types = typesProp.GetValue(folder, null) as IEnumerable;
                if (types != null) {
                    foreach (var t in types) {
                        string name = GetPropStr(t, "Name");
                        if (name.Equals("Valve_Faceplate", StringComparison.OrdinalIgnoreCase))
                        {
                            var versionsProp = t.GetType().GetProperty("Versions");
                            if (versionsProp != null) {
                                var versions = versionsProp.GetValue(t, null) as IEnumerable;
                                if (versions != null) {
                                    foreach (var v in versions) {
                                        string ver = GetPropStr(v, "VersionNumber");
                                        string outFile = Path.Combine(outDir, string.Format("Valve_Faceplate_V_{0}.xml", ver.Replace('.', '_')));
                                        Console.WriteLine("Exporting version " + ver + " to " + outFile + "...");
                                        
                                        // Invoke Export method via reflection
                                        var exportMethod = v.GetType().GetMethod("Export", new Type[] { typeof(FileInfo), typeof(ExportOptions) });
                                        if (exportMethod != null)
                                        {
                                            exportMethod.Invoke(v, new object[] { new FileInfo(outFile), ExportOptions.WithDefaults });
                                            Console.WriteLine("Export complete!");
                                        }
                                        else
                                        {
                                            Console.WriteLine("Export method not found!");
                                        }
                                    }
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
                        FindAndExport(sf, outDir);
                    }
                }
            }
        }

        static string GetPropStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }
    }
}
