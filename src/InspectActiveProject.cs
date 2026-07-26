using System;
using System.Reflection;
using System.IO;
using System.Threading;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;

namespace InspectActiveProject
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
            string projectPath = @"C:\Users\Admin\Documents\Automation\valveDemo2\valveDemo2.ap20";
            var processes = TiaPortal.GetProcesses();
            TiaPortal portal = null;
            Project project = null;
            bool isAttached = false;

            if (processes.Count > 0)
            {
                portal = processes[0].Attach();
                isAttached = true;
                project = portal.Projects[0];
            }
            else
            {
                portal = new TiaPortal(TiaPortalMode.WithUserInterface);
                project = portal.Projects.Open(new FileInfo(projectPath));
            }

            try
            {
                Device plcDevice = FindDevice(project, "S7-1200 station_1");
                PlcSoftware plcSoftware = FindPlcSoftware(plcDevice);

                // Go offline
                DeviceItem providerItem;
                object onlineProvider = FindOnlineProvider(plcDevice, out providerItem);
                if (onlineProvider != null)
                {
                    try
                    {
                        MethodInfo goOfflineMethod = onlineProvider.GetType().GetMethod("GoOffline");
                        if (goOfflineMethod != null)
                        {
                            goOfflineMethod.Invoke(onlineProvider, null);
                            Thread.Sleep(1000);
                        }
                    }
                    catch {}
                }

                // Helper to delete files if they exist
                DeleteFile(@"C:\Users\Admin\Documents\Automation\valveDemo2\temp_hmi_interface_db.xml");
                DeleteFile(@"C:\Users\Admin\Documents\Automation\valveDemo2\temp_fb_hmi_mirror.xml");

                PlcBlock hmiDB = plcSoftware.BlockGroup.Blocks.Find("HMI_Interface_DB");
                if (hmiDB != null)
                {
                    Console.WriteLine("Exporting HMI_Interface_DB...");
                    hmiDB.Export(new FileInfo(@"C:\Users\Admin\Documents\Automation\valveDemo2\temp_hmi_interface_db.xml"), ExportOptions.WithDefaults);
                }

                PlcBlock fmMirror = plcSoftware.BlockGroup.Blocks.Find("FB_HMI_MIirror");
                if (fmMirror != null)
                {
                    Console.WriteLine("Exporting FB_HMI_MIirror...");
                    fmMirror.Export(new FileInfo(@"C:\Users\Admin\Documents\Automation\valveDemo2\temp_fb_hmi_mirror.xml"), ExportOptions.WithDefaults);
                }
                
                Console.WriteLine("Export complete.");
            }
            finally
            {
                if (!isAttached && portal != null)
                {
                    portal.Dispose();
                }
            }
        }

        static void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        static Device FindDevice(Project project, string name)
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

        static Device FindDeviceInGroup(DeviceGroup group, string name)
        {
            foreach (var device in group.Devices)
            {
                if (device.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return device;
            }
            var userGroup = group as DeviceUserGroup;
            if (userGroup != null)
            {
                foreach (var subgroup in userGroup.Groups)
                {
                    var device = FindDeviceInGroup(subgroup, name);
                    if (device != null)
                        return device;
                }
            }
            return null;
        }

        static PlcSoftware FindPlcSoftware(Device device)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                var software = FindPlcSoftwareInItem(item);
                if (software != null) return software;
            }
            return null;
        }

        static PlcSoftware FindPlcSoftwareInItem(DeviceItem item)
        {
            var container = item.GetService<SoftwareContainer>();
            if (container != null && container.Software is PlcSoftware)
            {
                return container.Software as PlcSoftware;
            }
            foreach (DeviceItem subItem in item.DeviceItems)
            {
                var software = FindPlcSoftwareInItem(subItem);
                if (software != null) return software;
            }
            return null;
        }

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
    }
}
