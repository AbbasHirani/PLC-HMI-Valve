// Marks the operator-command valve tags as GMP relevant so WinCC Unified's own Audit Trail
// logs every change with user / old value / new value / timestamp.
//
// This replaces the InsertElectronicRecord approach, which returned 0x80000000 on every call
// no matter what arguments were varied. The audit trail itself was never the problem - the
// AuditTrail table already holds real rows from the User Management and ScriptingAuditLog
// providers - InsertElectronicRecord was simply the wrong mechanism. Per Siemens' docs
// ("GMP-relevant tags (RT Unified)"), tag value changes are logged when they come from a user
// action; the tag is the thing you flag, not the event.
//
// Usage: SetGmpTags.exe            -> set GmpRelevant on all command tags
//        SetGmpTags.exe --off      -> clear it again
//        SetGmpTags.exe --report   -> read-only survey, changes nothing
using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;

class Program {
    // The tags an operator actually commands from the HMI. Feedback signals (OpenFB, ClosedFB,
    // Healthy, LocalMode, State...) are driven by the PLC/valve, never by a person, so flagging
    // them would be pointless - the docs are explicit that PLC-driven changes are not logged.
    static readonly string[] CmdSuffixes = {
        "_OpenCmd",           // operator pressed OPEN
        "_CloseCmd",          // operator pressed CLOSE
        "_Configured",        // operator enabled / disabled the valve
        "_TimeoutOpenAlarm",  // operator pressed FAULT RESET (clears these four)
        "_TimeoutCloseAlarm",
        "_UnexpMove",
        "_DirFault",
    };

    static void Main(string[] args) {
        bool off    = args.Any(a => a == "--off");
        bool report = args.Any(a => a == "--report");

        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("[ERROR] No running TIA Portal instance found."); return; }
        var tia = procs[0].Attach();
        var proj = tia.Projects[0];
        Device hmiDevice = null;
        foreach (var d in proj.Devices)
            if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) hmiDevice = d;
        var hmi = FindHmiSoftware(hmiDevice);

        Console.WriteLine("Project    : " + proj.Name);
        Console.WriteLine("HMI device : " + hmiDevice.Name);
        Console.WriteLine("GMPEnabled : " + hmi.RuntimeSettings.GMPEnabled);
        if (!hmi.RuntimeSettings.GMPEnabled && !report) {
            hmi.RuntimeSettings.GMPEnabled = true;
            Console.WriteLine("             -> turned ON (required for audit logging)");
        }
        Console.WriteLine("Mode       : " + (report ? "REPORT (read-only)" : (off ? "CLEAR GmpRelevant" : "SET GmpRelevant")));
        Console.WriteLine();

        int total = 0, matched = 0, changed = 0, alreadyOk = 0, failed = 0;
        var perSuffix = new Dictionary<string, int>();
        foreach (var s in CmdSuffixes) perSuffix[s] = 0;
        var firstFew = new List<string>();

        foreach (var tt in hmi.TagTables) {
            foreach (HmiTag t in tt.Tags) {
                total++;
                string suf = CmdSuffixes.FirstOrDefault(s => t.Name.EndsWith(s, StringComparison.Ordinal));
                if (suf == null) continue;
                matched++;
                perSuffix[suf]++;

                bool cur = false;
                try { cur = t.GmpRelevant; } catch { }

                if (report) {
                    if (firstFew.Count < 10)
                        firstFew.Add(string.Format("    {0,-24} Gmp={1,-5} Confirm={2}", t.Name, cur, SafeConfirm(t)));
                    if (cur) alreadyOk++;
                    continue;
                }

                bool want = !off;
                if (cur == want) { alreadyOk++; continue; }
                try {
                    t.GmpRelevant = want;
                    // No confirmation prompt on the command itself - an operator closing a valve in
                    // a hurry should not be stopped by a dialog. The record is still written.
                    if (want) t.ConfirmationType = HmiConfirmationType.None;
                    changed++;
                    if (firstFew.Count < 10)
                        firstFew.Add(string.Format("    {0,-24} Gmp={1,-5} Confirm={2}", t.Name, want, SafeConfirm(t)));
                } catch (Exception ex) {
                    failed++;
                    if (failed <= 3) Console.WriteLine("  [FAIL] " + t.Name + ": " + ex.Message);
                }
            }
        }

        Console.WriteLine("Tags scanned      : " + total);
        Console.WriteLine("Command tags found: " + matched);
        foreach (var kv in perSuffix) Console.WriteLine(string.Format("    {0,-22} {1}", kv.Key, kv.Value));
        Console.WriteLine("Changed           : " + changed);
        Console.WriteLine("Already correct   : " + alreadyOk);
        Console.WriteLine("Failed            : " + failed);
        if (firstFew.Count > 0) {
            Console.WriteLine("\nSample:");
            foreach (var s in firstFew) Console.WriteLine(s);
        }

        if (!report && changed > 0) {
            Console.WriteLine("\nSaving project...");
            proj.Save();
            Console.WriteLine("Saved.");
        }
        Console.WriteLine("\n=== Done ===");
    }

    static string SafeConfirm(HmiTag t) { try { return t.ConfirmationType.ToString(); } catch { return "?"; } }

    static HmiSoftware FindHmiSoftware(Device device)
    { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
    static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
    { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
}
