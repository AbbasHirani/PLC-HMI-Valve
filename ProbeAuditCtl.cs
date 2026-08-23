// What string does HmiCustomWebControlContainer.ContainedType actually want? The manifest calls
// the Audit Viewer "Siemens.AuditViewer" with type "guid://4727C505-...", and I would rather find
// out on a scratch screen than guess on the real one. Creates a temp screen, inspects the
// control's defaults, tries each candidate, then deletes the screen again.
using System;
using System.Linq;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Base;

class Program {
    static readonly string[] Candidates = {
        "Siemens.AuditViewer",
        "guid://4727C505-0E12-46AB-BF7B-42ECD1E66FD2",
        "{4727C505-0E12-46AB-BF7B-42ECD1E66FD2}",
        "4727C505-0E12-46AB-BF7B-42ECD1E66FD2",
        "AuditViewer",
    };

    static void Main() {
        var procs = TiaPortal.GetProcesses();
        if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
        var proj = procs[0].Attach().Projects[0];
        Device hmiDevice = null;
        foreach (var d in proj.Devices)
            if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) hmiDevice = d;
        var hmi = FindHmiSoftware(hmiDevice);

        const string TMP = "Screen_TmpAuditProbe";
        var existing = hmi.Screens.Find(TMP);
        if (existing != null) { try { existing.Delete(); } catch { } }

        var sc = hmi.Screens.Create(TMP);
        Console.WriteLine("Created scratch screen: " + TMP);
        try {
            int i = 0;
            foreach (var c in Candidates) {
                i++;
                string nm = "Probe_" + i;
                try {
                    var av = sc.ScreenItems.Create<HmiCustomWebControlContainer>(nm, c);
                    Console.WriteLine(string.Format("  ACCEPTED  '{0}'", c));
                    Console.WriteLine("             read back ContainedType = '" + (av.ContainedType ?? "(null)") + "'");
                    int n = 0;
                    try {
                        foreach (var ip in av.Interface) {
                            n++;
                            object v = null; try { v = ip.Value; } catch { }
                            Console.WriteLine("             iface: " + ip.PropertyName + " = " + (v ?? "(null)"));
                        }
                    } catch (Exception ex2) { Console.WriteLine("             iface ERR " + Root(ex2)); }
                    if (n == 0) Console.WriteLine("             iface: (none exposed)");
                } catch (Exception ex) {
                    Console.WriteLine(string.Format("  rejected  '{0}'  -> {1}", c, Root(ex)));
                }
            }
        } catch (Exception ex) {
            Console.WriteLine("[ERROR] " + Root(ex));
        } finally {
            try { sc.Delete(); Console.WriteLine("Scratch screen deleted."); }
            catch (Exception ex) { Console.WriteLine("[WARN] could not delete scratch screen: " + Root(ex)); }
        }
    }

    static string Root(Exception e) { while (e.InnerException != null) e = e.InnerException; return e.Message; }

    static HmiSoftware FindHmiSoftware(Device device)
    { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
    static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
    { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
}
