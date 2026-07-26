using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;

namespace ValveDemoOpenness
{
    class Program
    {
        // Path to the project file
        private const string ProjectPath = @"C:\Users\Admin\Documents\Automation\valveDemo2\valveDemo2.ap20";

        // Assembly resolver to dynamically load Siemens Openness DLLs and dependencies at runtime
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int index = args.Name.IndexOf(',');
            string assemblyName = (index == -1) ? args.Name : args.Name.Substring(0, index);

            // Path to V20 Public API
            string publicApiPath = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20";
            string assemblyPath = Path.Combine(publicApiPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }

            // Path to V20 Bin Public API folder
            string binPublicApiPath = @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI";
            assemblyPath = Path.Combine(binPublicApiPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }

            // Path to V20 Bin folder (for Siemens.Engineering.Contract etc.)
            string binPath = @"C:\Program Files\Siemens\Automation\Portal V20\Bin";
            assemblyPath = Path.Combine(binPath, assemblyName + ".dll");
            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }

            return null;
        }

        static void Main(string[] args)
        {
            // Register assembly resolver
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n[ERROR] An error occurred during execution:");
                Console.WriteLine(ex.ToString());
            }

            Console.WriteLine("\nPress Enter to exit...");
            try
            {
                Console.ReadLine();
            }
            catch {}
        }

        static void Run()
        {
            TiaPortal portal = null;
            Project project = null;
            bool isAttached = false;

            var processes = TiaPortal.GetProcesses();
            if (processes.Count > 0)
            {
                Console.WriteLine("Found running TIA Portal instance. Attaching...");
                try
                {
                    portal = processes[0].Attach();
                    isAttached = true;
                    if (portal.Projects.Count > 0)
                    {
                        project = portal.Projects[0];
                    }
                    else
                    {
                        Console.WriteLine("No project open in the running TIA Portal. Opening project: " + ProjectPath);
                        project = portal.Projects.Open(new FileInfo(ProjectPath));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to attach to the running TIA Portal instance: " + ex.Message);
                    Console.WriteLine("Falling back to launching TIA Portal directly...");
                }
            }

            // Fallback if no processes found or attachment failed
            if (project == null)
            {
                Console.WriteLine("Starting a new TIA Portal instance with UI and opening project directly...");
                portal = new TiaPortal(TiaPortalMode.WithUserInterface);
                project = portal.Projects.Open(new FileInfo(ProjectPath));
            }

            try
            {
                Console.WriteLine("\nConnected to project: " + project.Name);
                Console.WriteLine("Project path: " + project.Path.FullName);

                // 1. Navigate to HMI Device "HMI_1"
                Device hmiDevice = FindDevice(project, "HMI_1");
                if (hmiDevice == null)
                {
                    Console.WriteLine("\n[ERROR] Device 'HMI_1' not found in the project.");
                    return;
                }
                Console.WriteLine("\nFound Device: " + hmiDevice.Name);

                // 2. Find HMI Software Target
                object hmiSoftware = FindHmiSoftware(hmiDevice);
                if (hmiSoftware == null)
                {
                    Console.WriteLine("[ERROR] HMI software target not found on HMI_1.");
                    return;
                }
                Console.WriteLine("Found HMI Software: " + hmiSoftware.GetType().Name);

                // 3. Locate Screen "Screen_1"
                object screen1 = FindScreen(hmiSoftware, "Screen_1");
                if (screen1 == null)
                {
                    Console.WriteLine("[ERROR] Screen 'Screen_1' not found on HMI_1.");
                    return;
                }
                Console.WriteLine("Found Screen: Screen_1");

                // 4. Locate Faceplate container_1 specifically
                object faceplateContainer = FindScreenItem(screen1, "Faceplate container_1");
                if (faceplateContainer == null)
                {
                    Console.WriteLine("[ERROR] Faceplate container 'Faceplate container_1' not found on Screen_1.");
                    return;
                }
                Console.WriteLine("Found Faceplate Container: " + GetObjectName(faceplateContainer));

                // 5. Query Online Status via Openness OnlineProvider
                DeviceItem providerItem;
                object onlineProvider = FindOnlineProvider(hmiDevice, out providerItem);
                string onlineState = "Unknown (OnlineProvider service not available)";
                if (onlineProvider != null)
                {
                    PropertyInfo stateProp = onlineProvider.GetType().GetProperty("State");
                    if (stateProp != null)
                    {
                        object stateVal = stateProp.GetValue(onlineProvider, null);
                        if (stateVal != null) onlineState = stateVal.ToString();
                    }
                }
                Console.WriteLine(string.Format("Current Device Online State: {0} (checked via {1})", 
                    onlineState, 
                    providerItem != null ? providerItem.Name : "None"));

                // 6. Print interface bindings & dynamizations
                Console.WriteLine("\n--- FIRST PARAMETER BINDINGS READ ---");
                PrintInterfaceBindings(faceplateContainer);

                // 7. If online, call GoOffline() and read again
                if (onlineState.Equals("Online", StringComparison.OrdinalIgnoreCase) || 
                    onlineState.Equals("Connected", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\nDevice is currently online. Triggering GoOffline() via Openness...");
                    try
                    {
                        MethodInfo goOfflineMethod = onlineProvider.GetType().GetMethod("GoOffline");
                        if (goOfflineMethod != null)
                        {
                            goOfflineMethod.Invoke(onlineProvider, null);
                            Console.WriteLine("GoOffline() called successfully. Waiting 2 seconds for transition...");
                            Thread.Sleep(2000);

                            // Verify state again
                            object newStateVal = onlineProvider.GetType().GetProperty("State").GetValue(onlineProvider, null);
                            string newOnlineState = newStateVal != null ? newStateVal.ToString() : "Unknown";
                            Console.WriteLine(string.Format("New Device Online State: {0}", newOnlineState));

                            Console.WriteLine("\n--- SECOND PARAMETER BINDINGS READ (OFFLINE STATE) ---");
                            PrintInterfaceBindings(faceplateContainer);
                        }
                        else
                        {
                            Console.WriteLine("[LIMITATION] GoOffline() method not found on OnlineProvider.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error going offline: " + ex.Message);
                    }
                }
                else
                {
                    Console.WriteLine("\nDevice is already offline (or state is not 'Online'). No state transition performed.");
                }
            }
            finally
            {
                // Only dispose/close the portal if we started a new instance
                if (!isAttached && portal != null)
                {
                    Console.WriteLine("\nClosing the launched TIA Portal instance...");
                    portal.Dispose();
                }
            }
        }

        // Recursive search for device by name
        private static Device FindDevice(Project project, string name)
        {
            foreach (var device in project.Devices)
            {
                if (device.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return device;
            }
            foreach (var group in project.DeviceGroups)
            {
                var device = FindDeviceInGroup(group, name);
                if (device != null)
                    return device;
            }
            return null;
        }

        private static Device FindDeviceInGroup(DeviceUserGroup group, string name)
        {
            foreach (var device in group.Devices)
            {
                if (device.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return device;
            }
            foreach (var subgroup in group.Groups)
            {
                var device = FindDeviceInGroup(subgroup, name);
                if (device != null)
                    return device;
            }
            return null;
        }

        // Recursive search for HMI software
        private static object FindHmiSoftware(Device device)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                var target = FindHmiSoftwareInItem(item);
                if (target != null)
                    return target;
            }
            return null;
        }

        private static object FindHmiSoftwareInItem(DeviceItem item)
        {
            SoftwareContainer container = item.GetService<SoftwareContainer>();
            if (container != null && container.Software != null)
            {
                if (container.Software.GetType().FullName.IndexOf("Hmi", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return container.Software;
                }
            }
            foreach (DeviceItem subItem in item.DeviceItems)
            {
                var subTarget = FindHmiSoftwareInItem(subItem);
                if (subTarget != null)
                    return subTarget;
            }
            return null;
        }

        // Recursive search for screen by name supporting both Comfort folder and Unified screens/groups
        private static object FindScreen(object software, string name)
        {
            // Try Comfort Panel way (via ScreenFolder)
            PropertyInfo screenFolderProp = software.GetType().GetProperty("ScreenFolder");
            if (screenFolderProp != null)
            {
                object screenFolder = screenFolderProp.GetValue(software, null);
                if (screenFolder != null)
                {
                    return FindScreenInComfortFolder(screenFolder, name);
                }
            }

            // Try Unified way (via Screens property directly on software)
            PropertyInfo screensProp = software.GetType().GetProperty("Screens");
            if (screensProp != null)
            {
                object screens = screensProp.GetValue(software, null);
                if (screens != null)
                {
                    var screensColl = screens as IEnumerable;
                    if (screensColl != null)
                    {
                        foreach (var screen in screensColl)
                        {
                            string sName = GetObjectName(screen);
                            if (sName != null && sName.Equals(name, StringComparison.OrdinalIgnoreCase))
                                return screen;
                        }
                    }
                }
            }

            // Search in Unified ScreenGroups
            PropertyInfo screenGroupsProp = software.GetType().GetProperty("ScreenGroups");
            if (screenGroupsProp != null)
            {
                object screenGroups = screenGroupsProp.GetValue(software, null);
                if (screenGroups != null)
                {
                    var groupsColl = screenGroups as IEnumerable;
                    if (groupsColl != null)
                    {
                        foreach (var group in groupsColl)
                        {
                            object screen = FindScreenInUnifiedGroup(group, name);
                            if (screen != null)
                                return screen;
                        }
                    }
                }
            }

            return null;
        }

        private static object FindScreenInComfortFolder(object folder, string name)
        {
            PropertyInfo screensProp = folder.GetType().GetProperty("Screens");
            if (screensProp != null)
            {
                var screens = screensProp.GetValue(folder, null) as IEnumerable;
                if (screens != null)
                {
                    foreach (var screen in screens)
                    {
                        string sName = GetObjectName(screen);
                        if (sName != null && sName.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return screen;
                    }
                }
            }

            PropertyInfo foldersProp = folder.GetType().GetProperty("Folders");
            if (foldersProp != null)
            {
                var folders = foldersProp.GetValue(folder, null) as IEnumerable;
                if (folders != null)
                {
                    foreach (var subFolder in folders)
                    {
                        var screen = FindScreenInComfortFolder(subFolder, name);
                        if (screen != null)
                            return screen;
                    }
                }
            }
            return null;
        }

        private static object FindScreenInUnifiedGroup(object group, string name)
        {
            PropertyInfo screensProp = group.GetType().GetProperty("Screens");
            if (screensProp != null)
            {
                var screens = screensProp.GetValue(group, null) as IEnumerable;
                if (screens != null)
                {
                    foreach (var screen in screens)
                    {
                        string sName = GetObjectName(screen);
                        if (sName != null && sName.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return screen;
                    }
                }
            }

            PropertyInfo subgroupsProp = group.GetType().GetProperty("ScreenGroups");
            if (subgroupsProp == null)
            {
                subgroupsProp = group.GetType().GetProperty("Groups");
            }
            if (subgroupsProp != null)
            {
                var subgroups = subgroupsProp.GetValue(group, null) as IEnumerable;
                if (subgroups != null)
                {
                    foreach (var subgroup in subgroups)
                    {
                        var screen = FindScreenInUnifiedGroup(subgroup, name);
                        if (screen != null)
                            return screen;
                    }
                }
            }
            return null;
        }

        // Search for a specific screen item by name on a screen
        private static object FindScreenItem(object screen, string name)
        {
            PropertyInfo screenItemsProp = screen.GetType().GetProperty("ScreenItems");
            if (screenItemsProp == null) return null;

            object screenItems = screenItemsProp.GetValue(screen, null);
            if (screenItems == null) return null;

            var itemsColl = screenItems as IEnumerable;
            if (itemsColl == null) return null;

            foreach (var item in itemsColl)
            {
                string sName = GetObjectName(item);
                if (sName != null && sName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            return null;
        }

        // Recursive search for OnlineProvider service in DeviceItems
        private static object FindOnlineProvider(Device device, out DeviceItem providerItem)
        {
            providerItem = null;
            foreach (DeviceItem item in device.DeviceItems)
            {
                object provider = FindOnlineProviderInItem(item, out providerItem);
                if (provider != null) return provider;
            }
            return null;
        }

        private static object FindOnlineProviderInItem(DeviceItem item, out DeviceItem providerItem)
        {
            providerItem = null;
            try
            {
                Type onlineProviderType = typeof(TiaPortal).Assembly.GetType("Siemens.Engineering.Online.OnlineProvider");
                if (onlineProviderType != null)
                {
                    MethodInfo getServiceMethod = item.GetType().GetMethod("GetService").MakeGenericMethod(onlineProviderType);
                    object onlineProvider = getServiceMethod.Invoke(item, null);
                    if (onlineProvider != null)
                    {
                        providerItem = item;
                        return onlineProvider;
                    }
                }
            }
            catch {}

            foreach (DeviceItem subItem in item.DeviceItems)
            {
                object provider = FindOnlineProviderInItem(subItem, out providerItem);
                if (provider != null) return provider;
            }
            return null;
        }

        // Helper to retrieve Name property via reflection
        private static string GetObjectName(object obj)
        {
            if (obj == null) return null;
            PropertyInfo prop = obj.GetType().GetProperty("Name");
            if (prop != null)
            {
                object val = prop.GetValue(obj, null);
                if (val != null) return val.ToString();
            }
            return null;
        }

        // Accesses faceplate container interface parameters and checks tag/dynamization bindings
        private static void PrintInterfaceBindings(object faceplateInstance)
        {
            Type itemType = faceplateInstance.GetType();
            PropertyInfo interfaceProp = itemType.GetProperty("Interface");
            if (interfaceProp == null)
            {
                Console.WriteLine("[LIMITATION] 'Interface' property not exposed or accessible.");
                return;
            }

            object interfaceColl = interfaceProp.GetValue(faceplateInstance, null);
            if (interfaceColl == null)
            {
                Console.WriteLine("[LIMITATION] Interface collection is null.");
                return;
            }

            var items = interfaceColl as IEnumerable;
            if (items == null)
            {
                Console.WriteLine("[LIMITATION] Interface collection is not enumerable.");
                return;
            }

            foreach (var parameter in items)
            {
                Type paramType = parameter.GetType();
                
                PropertyInfo nameProp = paramType.GetProperty("PropertyName");
                string paramName = "unknown";
                if (nameProp != null)
                {
                    object nameVal = nameProp.GetValue(parameter, null);
                    if (nameVal != null) paramName = nameVal.ToString();
                }

                PropertyInfo valueProp = paramType.GetProperty("Value");
                object paramValue = valueProp != null ? valueProp.GetValue(parameter, null) : null;
                string staticValStr = paramValue != null ? paramValue.ToString() : "None";

                PropertyInfo dynProp = paramType.GetProperty("Dynamizations");
                object dynamizations = dynProp != null ? dynProp.GetValue(parameter, null) : null;
                
                string bindingType = "None";
                string boundTag = "None";

                if (dynamizations != null)
                {
                    var dynColl = dynamizations as IEnumerable;
                    if (dynColl != null)
                    {
                        foreach (var dyn in dynColl)
                        {
                            Type dynType = dyn.GetType();
                            
                            PropertyInfo typeP = dynType.GetProperty("DynamizationType");
                            string dynTypeStr = "";
                            if (typeP != null)
                            {
                                object typeVal = typeP.GetValue(dyn, null);
                                if (typeVal != null) dynTypeStr = typeVal.ToString();
                            }
                            bindingType = dynTypeStr;

                            if (dynType.Name.Contains("TagDynamization") || dynTypeStr.Equals("Tag", StringComparison.OrdinalIgnoreCase))
                            {
                                PropertyInfo tagProp = dynType.GetProperty("Tag");
                                if (tagProp != null)
                                {
                                    object tagVal = tagProp.GetValue(dyn, null);
                                    boundTag = tagVal != null ? tagVal.ToString() : "None";
                                }
                            }
                            else if (dynType.Name.Contains("ScriptDynamization") || dynTypeStr.Equals("Script", StringComparison.OrdinalIgnoreCase))
                            {
                                boundTag = "[Script Dynamized]";
                            }
                        }
                    }
                }

                Console.WriteLine(string.Format("  Parameter: {0}", paramName));
                Console.WriteLine(string.Format("    Static Value:      {0}", staticValStr));
                Console.WriteLine(string.Format("    Dynamization Type: {0}", bindingType));
                Console.WriteLine(string.Format("    Bound Tag:         {0}", boundTag));
            }
        }
    }
}
