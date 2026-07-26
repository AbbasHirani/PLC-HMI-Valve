using System;
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;

namespace UpdatePlcBlocks
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
            if (procs.Count == 0) { Console.WriteLine("TIA Portal not running."); return; }
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];

            Device plcDevice = FindDevice(project, "PLC_1");
            if (plcDevice == null) { Console.WriteLine("PLC_1 device not found."); return; }
            PlcSoftware plc = FindPlcSoftware(plcDevice);
            if (plc == null) { Console.WriteLine("PLC software not found."); return; }

            // Import Valves_DB
            string dbPath = @"C:\Users\Admin\Documents\Automation\valveDemo2\temp_valves_db.xml";
            Console.WriteLine("Importing Valves_DB from " + dbPath + "...");
            var dbBlock = plc.BlockGroup.Blocks.Import(new FileInfo(dbPath), ImportOptions.Override);
            if (dbBlock != null && dbBlock.Count > 0) Console.WriteLine("Import successful: " + dbBlock[0].Name);
            else Console.WriteLine("Import failed.");

            // Import FB_ValveLoop
            string loopPath = @"C:\Users\Admin\Documents\Automation\valveDemo2\temp_fb_valveloop.xml";
            Console.WriteLine("Importing FB_ValveLoop from " + loopPath + "...");
            var loopBlock = plc.BlockGroup.Blocks.Import(new FileInfo(loopPath), ImportOptions.Override);
            if (loopBlock != null && loopBlock.Count > 0) Console.WriteLine("Import successful: " + loopBlock[0].Name);
            else Console.WriteLine("Import failed.");
        }

        static Device FindDevice(Project project, string name)
        {
            foreach (var device in project.Devices) if (device.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return device;
            foreach (var group in project.DeviceGroups) { var device = FindDeviceInGroup(group, name); if (device != null) return device; }
            return null;
        }
        static Device FindDeviceInGroup(DeviceUserGroup group, string name)
        {
            foreach (var device in group.Devices) if (device.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return device;
            foreach (var subgroup in group.Groups) { var device = FindDeviceInGroup(subgroup, name); if (device != null) return device; }
            return null;
        }
        static PlcSoftware FindPlcSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindPlcSoftwareInItem(it); if (r != null) return r; } return null; }
        static PlcSoftware FindPlcSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is PlcSoftware) return c.Software as PlcSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindPlcSoftwareInItem(sub); if (r != null) return r; } return null; }
    }
}
