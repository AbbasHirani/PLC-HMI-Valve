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
            // D:\Siemens first: the project moved machines on 2026-08-31 and the old
            // C:\Program Files install no longer exists. Same fix as compile_tool.ps1.
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
            try { Run(args); } catch (Exception ex) { Console.WriteLine("[ERROR] " + ex); Environment.ExitCode = 1; }
        }

        // Which XML sources to import, in order. Passed on the command line so this tool is not
        // pinned to one block set - it used to hardcode two absolute paths under C:\Users\Admin,
        // which stopped existing when the project moved machines on 2026-08-31.
        // Order matters: import a DB before any block that references its new members.
        static void Run(string[] args)
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("TIA Portal not running."); return; }

            TiaPortal portal = null; Project project = null;
            foreach (var p in procs)
            {
                try { var a = p.Attach(); if (a != null && a.Projects.Count > 0) { portal = a; project = a.Projects[0]; break; } }
                catch { }
            }
            if (project == null) { Console.WriteLine("No open project."); return; }

            // Find the PLC by looking for the device that actually carries PlcSoftware, rather than
            // by device name. This used to search for a device called "PLC_1" and fail with
            // "PLC_1 device not found" - the DEVICE is "S7-1200 station_1"; only the software
            // inside it is named PLC_1. A name lookup breaks the moment the station is renamed.
            PlcSoftware plc = null;
            string plcDeviceName = null;
            foreach (Device d in project.Devices)
            {
                var s = FindPlcSoftware(d);
                if (s != null) { plc = s; plcDeviceName = d.Name; break; }
            }
            if (plc == null)
                foreach (DeviceUserGroup g in project.DeviceGroups)
                {
                    plc = FindPlcInGroup(g, out plcDeviceName);
                    if (plc != null) break;
                }
            if (plc == null) { Console.WriteLine("No device in this project carries PLC software."); return; }
            Console.WriteLine("PLC: \"" + plc.Name + "\" on device \"" + plcDeviceName + "\"");

            if (args.Length == 0)
            {
                Console.WriteLine("usage: UpdatePlcBlocks.exe <block1.xml> [block2.xml ...]");
                Console.WriteLine("  paths are relative to the repo root unless absolute");
                Environment.ExitCode = 2;
                return;
            }

            string repo = Directory.GetCurrentDirectory();
            int failed = 0;
            foreach (string arg in args)
            {
                string path = Path.IsPathRooted(arg) ? arg : Path.Combine(repo, arg);
                if (!File.Exists(path))
                {
                    Console.WriteLine("[FAIL] not found: " + path);
                    failed++;
                    continue;
                }
                Console.WriteLine("Importing " + Path.GetFileName(path) + " ...");
                try
                {
                    var imported = plc.BlockGroup.Blocks.Import(new FileInfo(path), ImportOptions.Override);
                    if (imported != null && imported.Count > 0)
                        foreach (var b in imported) Console.WriteLine("  [OK] " + b.Name);
                    else
                    {
                        Console.WriteLine("  [FAIL] import returned nothing");
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  [FAIL] " + ex.Message);
                    failed++;
                }
            }

            if (failed == 0)
            {
                Console.WriteLine("\nAll imports OK. Saving project...");
                project.Save();
                Console.WriteLine("Saved.");
            }
            else
            {
                Console.WriteLine("\n" + failed + " import(s) FAILED - project NOT saved.");
                Environment.ExitCode = 1;
            }
        }

        static PlcSoftware FindPlcInGroup(DeviceUserGroup group, out string deviceName)
        {
            foreach (Device d in group.Devices)
            {
                var s = FindPlcSoftware(d);
                if (s != null) { deviceName = d.Name; return s; }
            }
            foreach (DeviceUserGroup sub in group.Groups)
            {
                var s = FindPlcInGroup(sub, out deviceName);
                if (s != null) return s;
            }
            deviceName = null;
            return null;
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
