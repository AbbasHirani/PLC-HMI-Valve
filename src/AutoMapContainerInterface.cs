using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Controls;
using Siemens.Engineering.HmiUnified.UI.Parts;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;

namespace AutoMapContainerInterface
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
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) return;
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];

            Device hmiDevice = null;
            foreach (var d in project.Devices) if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) { hmiDevice = d; break; }
            HmiSoftware hmi = FindHmiSoftware(hmiDevice);
            HmiScreen sc = FindScreen(hmi, "Screen_Popup");
            if (sc == null) { Console.WriteLine("Screen_Popup not found!"); return; }

            HmiFaceplateContainer container = null;
            foreach (var item in sc.ScreenItems)
            {
                var fpc = item as HmiFaceplateContainer;
                if (fpc != null) { container = fpc; break; }
            }

            if (container == null) { Console.WriteLine("Faceplate container not found on Screen_Popup!"); return; }

            Console.WriteLine("Found container: " + container.Name);
            var interfaceProp = container.GetType().GetProperty("Interface");
            if (interfaceProp != null)
            {
                var interfaces = interfaceProp.GetValue(container, null) as IEnumerable;
                if (interfaces != null)
                {
                    foreach (var faceInterface in interfaces)
                    {
                        string propName = GetPropStr(faceInterface, "PropertyName");
                        string tagToMap = "V001_" + propName;
                        Console.WriteLine("Mapping interface: " + propName + " -> " + tagToMap);

                        try {
                            var dynsProp = faceInterface.GetType().GetProperty("Dynamizations");
                            if (dynsProp != null)
                            {
                                object dyns = dynsProp.GetValue(faceInterface, null);
                                Type dynsType = dyns.GetType();
                                MethodInfo createGen = null;
                                foreach (var m in dynsType.GetMethods())
                                {
                                    if (m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                                    {
                                        createGen = m;
                                        break;
                                    }
                                }

                                if (createGen != null)
                                {
                                    Type tagDynType = typeof(TagDynamization);
                                    MethodInfo mi = createGen.MakeGenericMethod(tagDynType);
                                    object dyn = mi.Invoke(dyns, null);
                                    if (dyn != null)
                                    {
                                        var tagProp = dyn.GetType().GetProperty("Tag");
                                        if (tagProp != null)
                                        {
                                            tagProp.SetValue(dyn, tagToMap, null);
                                            Console.WriteLine("  Successfully mapped " + propName + " to " + tagToMap);
                                        }
                                    }
                                }
                            }
                        } catch (Exception ex) {
                            Console.WriteLine("  Mapping note for " + propName + ": " + ex.Message);
                        }
                    }
                }
            }
            Console.WriteLine("Auto-mapping complete!");
        }

        static string GetPropStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }

        static HmiScreen FindScreen(HmiSoftware hmi, string name)
        { foreach (HmiScreen s in hmi.Screens) if (s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return s; return null; }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
    }
}
