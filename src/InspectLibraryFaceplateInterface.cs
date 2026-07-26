using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;

namespace InspectLibraryFaceplateInterface
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

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\faceplate_interface_properties.txt";
            using (var w = new StreamWriter(outFile))
            {
                var lib = project.ProjectLibrary;
                var typesFolder = lib.TypeFolder;
                InspectFolder(typesFolder, w);
            }
            Console.WriteLine("Interface properties written to " + outFile);
        }

        static void InspectFolder(object folder, StreamWriter w)
        {
            var typesProp = folder.GetType().GetProperty("Types");
            if (typesProp != null) {
                var types = typesProp.GetValue(folder, null) as IEnumerable;
                if (types != null) {
                    foreach (var t in types) {
                        string name = GetPropStr(t, "Name");
                        if (name.Equals("Valve_Faceplate", StringComparison.OrdinalIgnoreCase))
                        {
                            w.WriteLine("=== Library Type: " + name + " ===");
                            var versionsProp = t.GetType().GetProperty("Versions");
                            if (versionsProp != null) {
                                var versions = versionsProp.GetValue(t, null) as IEnumerable;
                                if (versions != null) {
                                    foreach (var v in versions) {
                                        w.WriteLine("  Version: " + GetPropStr(v, "VersionNumber"));
                                        
                                        // Print all properties of v
                                        var props = v.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                                        foreach (var p in props)
                                        {
                                            try {
                                                object val = p.GetValue(v, null);
                                                w.WriteLine(string.Format("    Property: {0,-30} = {1}", p.Name, val != null ? val.GetType().FullName : "(null)"));
                                                
                                                // If it is a composition or collection, list its items
                                                if (val != null && !(val is string) && (val is IEnumerable))
                                                {
                                                    foreach (var sub in val as IEnumerable)
                                                    {
                                                        w.WriteLine("      SubItem: " + sub.GetType().FullName + " | Name: " + GetPropStr(sub, "Name"));
                                                    }
                                                }
                                            } catch (Exception ex) {
                                                w.WriteLine(string.Format("    Property: {0,-30} = (error: {1})", p.Name, ex.Message));
                                            }
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
                        InspectFolder(sf, w);
                    }
                }
            }
        }

        static string GetPropStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }
    }
}
