using System;
using System.Reflection;
using System.IO;
using System.Collections;

namespace InspectHardwareTypes
{
    class Program
    {
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int index = args.Name.IndexOf(',');
            string assemblyName = (index == -1) ? args.Name : args.Name.Substring(0, index);
            
            string publicApiPath = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20";
            string assemblyPath = Path.Combine(publicApiPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath)) return Assembly.LoadFrom(assemblyPath);
            
            string binPublicApiPath = @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI";
            assemblyPath = Path.Combine(binPublicApiPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath)) return Assembly.LoadFrom(assemblyPath);

            string binPath = @"C:\Program Files\Siemens\Automation\Portal V20\Bin";
            assemblyPath = Path.Combine(binPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath)) return Assembly.LoadFrom(assemblyPath);
            return null;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try
            {
                RunInspect();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex);
            }
        }

        static void RunInspect()
        {
            var processes = Siemens.Engineering.TiaPortal.GetProcesses();
            if (processes.Count == 0)
            {
                Console.WriteLine("No TIA Portal processes found.");
                return;
            }
            var portal = processes[0].Attach();
            var project = portal.Projects[0];

            try
            {
                object hmiDevice = FindDevice(project, "HMI_1");
                object hmiSoftware = FindHmiSoftware(hmiDevice as Siemens.Engineering.HW.Device);
                object screen1 = FindScreen(hmiSoftware, "Screen_1");

                if (screen1 != null)
                {
                    PropertyInfo screenItemsProp = screen1.GetType().GetProperty("ScreenItems");
                    object screenItems = screenItemsProp.GetValue(screen1, null);
                    
                    Type fpcType = null;
                    foreach (var ass in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        fpcType = ass.GetType("Siemens.Engineering.HmiUnified.UI.Controls.HmiFaceplateContainer");
                        if (fpcType != null) break;
                    }
                    
                    if (fpcType == null)
                    {
                        string apiPath = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\Siemens.Engineering.Hmi.dll";
                        if (File.Exists(apiPath))
                        {
                            Assembly hmiAss = Assembly.LoadFrom(apiPath);
                            fpcType = hmiAss.GetType("Siemens.Engineering.HmiUnified.UI.Controls.HmiFaceplateContainer");
                        }
                    }

                    MethodInfo createMethod = null;
                    foreach (var m in screenItems.GetType().GetMethods())
                    {
                        if (m.Name == "Create" && m.IsGenericMethod)
                        {
                            var parameters = m.GetParameters();
                            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                            {
                                createMethod = m.MakeGenericMethod(fpcType);
                                break;
                            }
                        }
                    }

                    if (createMethod != null)
                    {
                        object tempFpc = createMethod.Invoke(screenItems, new object[] { "Fpc_Temp_Test" });
                        if (tempFpc != null)
                        {
                            try
                            {
                                PropertyInfo ctProp = tempFpc.GetType().GetProperty("ContainedType");
                                if (ctProp != null)
                                {
                                    ctProp.SetValue(tempFpc, "Valve_Faceplate", null);
                                    
                                    PropertyInfo interfaceProp = tempFpc.GetType().GetProperty("Interface");
                                    if (interfaceProp != null)
                                    {
                                        object interfaceColl = interfaceProp.GetValue(tempFpc, null);
                                        var items = interfaceColl as IEnumerable;
                                        if (items != null)
                                        {
                                            foreach (var item in items)
                                            {
                                                PropertyInfo nameP = item.GetType().GetProperty("PropertyName");
                                                object nameVal = nameP.GetValue(item, null);
                                                string paramName = nameVal != null ? nameVal.ToString() : "unnamed";
                                                Console.WriteLine("Testing parameter: " + paramName);
                                                
                                                PropertyInfo valueProp = item.GetType().GetProperty("Value");
                                                if (valueProp != null)
                                                {
                                                    try
                                                    {
                                                        Console.WriteLine("  Attempting to set Value to 'OpenCmd_V1' (as string)...");
                                                        valueProp.SetValue(item, "OpenCmd_V1", null);
                                                        object val = valueProp.GetValue(item, null);
                                                        Console.WriteLine("    SUCCESS! Value is now: " + (val ?? "null"));
                                                        
                                                        // Print Dynamizations to see if it created a tag binding
                                                        PropertyInfo dynsProp = item.GetType().GetProperty("Dynamizations");
                                                        object dyns = dynsProp.GetValue(item, null);
                                                        int count = (int)dyns.GetType().GetProperty("Count").GetValue(dyns, null);
                                                        Console.WriteLine("    Dynamizations count: " + count);
                                                        if (count > 0)
                                                        {
                                                            var dynColl = dyns as IEnumerable;
                                                            foreach (var dyn in dynColl)
                                                            {
                                                                Console.WriteLine("    Dynamization type: " + dyn.GetType().FullName);
                                                            }
                                                        }
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Console.WriteLine("    Failed direct string write: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
                                                    }
                                                }
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                MethodInfo deleteMethod = tempFpc.GetType().GetMethod("Delete");
                                if (deleteMethod != null) deleteMethod.Invoke(tempFpc, null);
                                Console.WriteLine("Deleted temporary faceplate container.");
                            }
                        }
                    }
                }
            }
            finally
            {
            }
        }

        static object FindDevice(Siemens.Engineering.Project project, string name)
        {
            foreach (var device in project.Devices)
            {
                if (device.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return device;
            }
            return null;
        }

        static object FindHmiSoftware(Siemens.Engineering.HW.Device device)
        {
            foreach (var item in device.DeviceItems)
            {
                var target = FindHmiSoftwareInItem(item);
                if (target != null) return target;
            }
            return null;
        }

        static object FindHmiSoftwareInItem(Siemens.Engineering.HW.DeviceItem item)
        {
            var container = item.GetService<Siemens.Engineering.HW.Features.SoftwareContainer>();
            if (container != null && container.Software != null)
            {
                if (container.Software.GetType().FullName.IndexOf("Hmi", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return container.Software;
                }
            }
            foreach (var subItem in item.DeviceItems)
            {
                var subTarget = FindHmiSoftwareInItem(subItem);
                if (subTarget != null) return subTarget;
            }
            return null;
        }

        private static object FindScreen(object software, string name)
        {
            PropertyInfo screensProp = software.GetType().GetProperty("Screens");
            if (screensProp != null)
            {
                object screens = screensProp.GetValue(software, null);
                var screensColl = screens as IEnumerable;
                if (screensColl != null)
                {
                    foreach (var screen in screensColl)
                    {
                        PropertyInfo nameP = screen.GetType().GetProperty("Name");
                        object nameVal = nameP != null ? nameP.GetValue(screen, null) : null;
                        if (nameVal != null && nameVal.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
                            return screen;
                    }
                }
            }
            return null;
        }
    }
}
