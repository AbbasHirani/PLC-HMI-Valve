using System;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;

class Program {
    static void Main(string[] args) {
        bool doImport = args.Any(a => a == "--import");
        string path = @"C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\temp_ob1_work.xml";
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("No TIA Portal running."); return; }
        var tia = procs[0].Attach();
        var proj = tia.Projects[0];
        PlcSoftware plc = null;
        foreach (Device d in proj.Devices) {
            foreach (DeviceItem it in d.DeviceItems) {
                var c = it.GetService<SoftwareContainer>();
                if (c != null) { var p = c.Software as PlcSoftware; if (p != null) { plc = p; break; } }
            }
            if (plc != null) break;
        }
        if (plc == null) { Console.WriteLine("[ERROR] PlcSoftware not found."); return; }

        if (!doImport) {
            var blk = plc.BlockGroup.Blocks.Find("Main");
            if (blk == null) { Console.WriteLine("[ERROR] Main (OB1) not found."); return; }
            if (File.Exists(path)) File.Delete(path);
            blk.Export(new FileInfo(path), ExportOptions.WithDefaults);
            Console.WriteLine("[EXPORT] Main -> " + path);
        } else {
            if (!File.Exists(path)) { Console.WriteLine("[ERROR] " + path + " missing."); return; }
            var res = plc.BlockGroup.Blocks.Import(new FileInfo(path), ImportOptions.Override);
            Console.WriteLine("[IMPORT] returned " + (res == null ? "null" : res.Count + " block(s)"));
            if (res != null) foreach (var b in res) Console.WriteLine("   " + b.Name);
            proj.Save();
            Console.WriteLine("PROJECT SAVED");
        }
    }
}
