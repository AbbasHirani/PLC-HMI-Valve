using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Widgets;

// One-valve client demo: CM79 on the bare CPU, with no stations and no panel.
// Runs ONLY on the project named below, because it deletes stations, screens, alarms and tags.
// It never calls ImportPlcBlocks: that would re-import the real sources and undo the demo's I/O.
namespace ValveDemoHmiBuilder
{
    partial class Program
    {
        const string DEMO_PROJECT = "valveDemo_OneValve";
        const string DEMO_DIR = @"C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\demo_one_valve";
        const string DEMO_V = "V021";

        static Project AttachDemoProject()
        {
            foreach (var p in TiaPortal.GetProcesses()) {
                TiaPortal att = null;
                try { att = p.Attach(); } catch { continue; }
                if (att == null || att.Projects.Count == 0) continue;
                Project pr = att.Projects[0];
                if (pr.Name != DEMO_PROJECT) {
                    Console.WriteLine("[REFUSED] Open project is '" + pr.Name + "', not '" + DEMO_PROJECT + "'. Nothing changed.");
                    return null;
                }
                return pr;
            }
            Console.WriteLine("[REFUSED] No open project found. Nothing changed.");
            return null;
        }

        // --demo-one-valve=plc : one-valve PLC sources in, stations out.
        // --demo-one-valve=hmi : one-valve screens, alarms and tags. Run after compiling the PLC.
        static void RunDemoOneValve(string step)
        {
            Project project = AttachDemoProject();
            if (project == null) { Environment.ExitCode = 1; return; }
            Console.WriteLine("Demo project confirmed: " + project.Name);
            if (step == "plc") DemoPlc(project);
            else if (step == "hmi") DemoHmi(project);
            else { Console.WriteLine("[ERROR] Unknown step: " + step); Environment.ExitCode = 2; }
        }

        static PlcSoftware FindDemoPlc(DeviceItem it)
        {
            var c = it.GetService<SoftwareContainer>();
            if (c != null && c.Software is PlcSoftware) return (PlcSoftware)c.Software;
            foreach (DeviceItem s in it.DeviceItems) { var r = FindDemoPlc(s); if (r != null) return r; }
            return null;
        }

        static void DemoPlc(Project project)
        {
            PlcSoftware plc = null;
            foreach (Device d in project.Devices) {
                foreach (DeviceItem it in d.DeviceItems) { plc = FindDemoPlc(it); if (plc != null) break; }
                if (plc != null) break;
            }
            if (plc == null) { Console.WriteLine("[ERROR] PLC not found."); Environment.ExitCode = 1; return; }

            foreach (var file in new[] { "demo_valves_db.xml", "demo_valve_channels_db.xml", "demo_fc_physical_io_copy.xml" }) {
                Console.WriteLine("  Importing " + file);
                plc.BlockGroup.Blocks.Import(new FileInfo(Path.Combine(DEMO_DIR, file)), ImportOptions.Override);
            }

            // Shared logic comes from the real source, so a logic fix reaches the demo unchanged.
            Console.WriteLine("  Importing temp_fb_valveloop.xml");
            plc.BlockGroup.Blocks.Import(new FileInfo(Path.Combine(Path.GetDirectoryName(DEMO_DIR), "temp_fb_valveloop.xml")), ImportOptions.Override);

            foreach (var name in new[] { "ET200SP_AFT", "ET200SP_MID", "ET200SP_FWD" }) {
                Device dev = project.Devices.FirstOrDefault(x => x.Name == name);
                if (dev == null) { Console.WriteLine("  already gone: " + name); continue; }
                dev.Delete();
                Console.WriteLine("  Deleted station: " + name);
            }
            project.Save();
            Console.WriteLine("[SAVE] Project saved.");
        }

        static void DemoHmi(Project project)
        {
            Device hmiDevice = FindDeviceByPartialName(project, "HMI");
            HmiSoftware hmi = hmiDevice == null ? null : FindHmiSoftware(hmiDevice);
            if (hmi == null) { Console.WriteLine("[ERROR] HMI not found."); Environment.ExitCode = 1; return; }

            CreateSummaryTag(hmi, DEMO_V + "_OpenValve", "Valves_DB.Valve[21].OpenValve", "Bool");
            CreateSummaryTag(hmi, DEMO_V + "_CloseValve", "Valves_DB.Valve[21].CloseValve", "Bool");

            // Screens first, so no script still refers to the tags removed afterwards.
            foreach (var name in new[] { "Screen_AftBallast", "Screen_Bilge", "Screen_FwdBallast", "Screen_Diagnostics",
                                         "Screen_ConfirmDisable", "Screen_Popup" }) {
                HmiScreen s = FindScreen(hmi, name);
                if (s == null) continue;
                s.Delete();
                Console.WriteLine("  Deleted screen: " + name);
            }

            HmiScreen home = RecreateScreen(hmi, "Screen_Home");
            if (home == null) { Console.WriteLine("[ERROR] Screen_Home not created."); Environment.ExitCode = 1; return; }
            BuildDemoHome(home);
            WireStartAuditLog(home);

            HmiScreen sys = FindScreen(hmi, "Screen_SysDiag");
            if (sys != null) {
                var reset = sys.ScreenItems.Find("SD_ResetModules");
                if (reset != null) { reset.Delete(); Console.WriteLine("  Removed module reset button"); }
            }

            foreach (var name in new[] { "Screen_Alarms", "Screen_SysDiag", "Screen_Login" }) {
                HmiScreen s = FindScreen(hmi, name);
                if (s == null) { Console.WriteLine("  [WARN] missing " + name); continue; }
                var old = s.ScreenItems.Where(i => i.Name.StartsWith("Nav_", StringComparison.Ordinal)).ToList();
                foreach (var item in old) item.Delete();
                BuildDemoNav(s, name);
                var title = s.ScreenItems.Find("Title_Main");
                if (title != null) SetMLText(title, "Text", DEMO_TITLE);
                Console.WriteLine("  Nav rebuilt: " + name);
            }

            // Keep only this valve's alarms. Module and station alarms cannot occur without stations.
            var doomed = hmi.DiscreteAlarms.Where(a => !a.Name.StartsWith(DEMO_V + "_", StringComparison.Ordinal)
                                                       || DEMO_GONE_ALARMS.Contains(a.Name)).Select(a => a.Name).ToList();
            Console.WriteLine("  Deleting " + doomed.Count + " alarms...");
            foreach (var n in doomed) hmi.DiscreteAlarms.Find(n).Delete();
            Console.WriteLine("  Alarms left: " + hmi.DiscreteAlarms.Count());

            // Valves 1-89 except this one, the other alarm words, and the station/module tags.
            var table = hmi.TagTables.Find("ValveTags");
            var doomedTags = table.Tags.Select(t => t.Name).Where(IsDemoTagToDelete).ToList();
            Console.WriteLine("  Deleting " + doomedTags.Count + " tags...");
            int done = 0;
            foreach (var n in doomedTags) {
                table.Tags.Find(n).Delete();
                if (++done % 250 == 0) Console.WriteLine("    " + done);
            }
            Console.WriteLine("  Tags left: " + table.Tags.Count());

            project.Save();
            Console.WriteLine("[SAVE] Project saved.");
        }

        static bool IsDemoTagToDelete(string n)
        {
            if (DEMO_GONE_TAGS.Contains(n)) return true;
            if (n.Length > 4 && n[0] == 'V' && char.IsDigit(n[1]) && char.IsDigit(n[2]) && char.IsDigit(n[3]) && n[4] == '_')
                return !n.StartsWith(DEMO_V + "_", StringComparison.Ordinal);
            if (n.StartsWith("Diag_", StringComparison.Ordinal)) return true;
            // CM79 is slot 21, so all its alarm bits live in word 1.
            if (n.StartsWith("Valves_DB_W_", StringComparison.Ordinal)) return !n.EndsWith("_1", StringComparison.Ordinal);
            return false;
        }

        // The demo's own copy of AddRemoteLockStyling, without the Local and Healthy terms: this
        // actuator reports neither. Colour only, like the original - the PLC still decides.
        static void DemoLockStyling(HmiButton btn, uint activeBackColor)
        {
            string guard = JS_READ +
                "var cfg = r(Tags(\"Valves_DB_SelConfigured\").Read());\n" +
                "var posfault = r(Tags(\"Valves_DB_SelPositionFault\").Read());\n" +
                "var locked = !cfg || posfault;\n";
            Dyn(btn, "BackColor", guard + "return locked ? 0xFF2A2E38 : " + string.Format("0x{0:X8}", activeBackColor) + ";\n", "AutomaticTags");
            Dyn(btn, "ForeColor", guard + "return locked ? 0xFF6B7280 : 0xFFFFFFFF;\n", "AutomaticTags");
        }

        // Healthy and Local are gone from the demo, so are their alarms, tags and alarm words.
        static readonly string[] DEMO_GONE_ALARMS = { DEMO_V + "_Unhealthy", DEMO_V + "_Local" };
        static readonly string[] DEMO_GONE_TAGS = {
            DEMO_V + "_Healthy", DEMO_V + "_LocalMode",
            "Valves_DB_SelHealthy", "Valves_DB_SelLocalMode",
            "Valves_DB_W_Unhealthy_1", "Valves_DB_W_Local_1",
        };

        const string DEMO_TITLE ="MV WESTERLY  &#xB7;  VALVE REMOTE CONTROL  &#xB7;  ONE-VALVE DEMO";

        static void BuildDemoNav(HmiScreen sc, string activeTarget)
        {
            string[] labels  = { "&#x2302;  HOME", "&#x1F514;  FAULTS", "&#x2699;  DIAGNOSTICS", "&#x1F4CB;  AUDIT LOG" };
            string[] targets = { "Screen_Home", "Screen_Alarms", "Screen_SysDiag", "Screen_Login" };
            MakeRect(sc, "Nav_BG", 0, 104, 1920, 58, M_BOX, M_LINE, 1);
            int w = 464, h = 46, y = 110, x0 = 20, gap = 8;
            for (int i = 0; i < labels.Length; i++) {
                bool active = targets[i] == activeTarget;
                var btn = MakeBtn(sc, "Nav_" + i, x0 + i * (w + gap), y, w, h, labels[i],
                                  active ? M_ACCENT : M_BOX, active ? Color.White : M_TEXT,
                                  active ? M_ACCENT : M_LINE, 1, 17, active);
                if (!active) AddNavClick(btn, targets[i]);
            }
        }

        static void BuildDemoHome(HmiScreen sc)
        {
            Console.WriteLine("  Drawing one-valve Home...");
            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);
            BuildHomeHeader(sc);
            SetMLText(sc.ScreenItems.Find("Title_Main"), "Text", DEMO_TITLE);
            BuildDemoNav(sc, "Screen_Home");

            // Identity band
            MakeRect(sc, "D_Id", 16, 176, 1888, 64, M_HDR, M_HDR, 0);
            MakeTb(sc, "D_IdText", 40, 186, 1840, 44,
                   "CM79   &#xB7;   11-011-A3   &#xB7;   Ballast Aft   &#xB7;   Bilge tie   &#xB7;   Cross connect",
                   M_TRANS, M_HDRTXT, 0, "Left", 26, true);

            // Position, the most important fact about the valve
            MakeRect(sc, "D_Card", 16, 252, 1888, 300, BG_CARD, BORDER, 1);
            var dot = sc.ScreenItems.Create<HmiEllipse>("D_Dot");
            dot.CenterX = SX(150); dot.CenterY = SY(402); dot.RadiusX = (uint)SX(95); dot.RadiusY = (uint)SY(95);
            dot.BackColor = TEAL; dot.BorderColor = Color.White;
            Dyn(dot, "BackColor", JS_READ +
                "var code = r(Tags(\"Valves_DB_SelPosCode\").Read());\n" +
                "var flt = r(Tags(\"Valves_DB_SelFaultCode\").Read());\n" +
                "var flash = r(Tags(\"Valves_DB_Clock1Hz\").Read());\n" +
                "if (code === 0) return 0xFF8E8E93;\n" +
                "var pos = 0xFF00A2FF;\n" +
                "if (code === 3) pos = 0xFF32C785;\n" +
                "if (code === 4) pos = 0xFF4B5563;\n" +
                "if (flt > 0) return flash ? 0xFFFF0000 : pos;\n" +
                "return pos;\n", "AutomaticTags");

            var pos2 = MakeTb(sc, "D_PosText", 290, 272, 1000, 80, "", BG_CARD, Color.White, 0, "Left", 54, true);
            Dyn(pos2, "Text", JS_READ +
                "var code = r(Tags(\"Valves_DB_SelPosCode\").Read());\n" +
                "var names = [\"UNCONFIGURED\", \"\", \"\", \"FULLY OPEN\", \"FULLY CLOSED\", \"NO POSITION\", \"OPENING\", \"CLOSING\"];\n" +
                "return names[code] || \"NO POSITION\";\n", "AutomaticTags");
            Dyn(pos2, "ForeColor", JS_READ +
                "var code = r(Tags(\"Valves_DB_SelPosCode\").Read());\n" +
                "if (code === 3) return 0xFF32C785;\n" +
                "if (code === 4) return 0xFFD8DEE6;\n" +
                "if (code === 0) return 0xFF8E8E93;\n" +
                "return 0xFF00A2FF;\n", "AutomaticTags");

            var cmd = MakeTb(sc, "D_CmdText", 290, 352, 1000, 40, "", BG_CARD, Color.FromArgb(255, 226, 168, 0), 0, "Left", 22, true);
            Dyn(cmd, "Text", JS_READ +
                "var c = r(Tags(\"Valves_DB_SelCmdPos\").Read());\n" +
                "var code = r(Tags(\"Valves_DB_SelPosCode\").Read());\n" +
                "if (!c) return \"\";\n" +
                "if (code === 6 || code === 7) return \"\";\n" +
                "if (code === ((c === 1) ? 3 : 4)) return \"\";\n" +
                "return \"COMMANDED:  \" + ((c === 1) ? \"OPEN\" : \"CLOSED\");\n", "AutomaticTags");

            var fault = MakeTb(sc, "D_Fault", 290, 396, 1000, 56, "", BG_CARD, Color.FromArgb(255, 240, 90, 78), 0, "Left", 30, true);
            Dyn(fault, "Text", JS_READ +
                "var flt = r(Tags(\"Valves_DB_SelFaultCode\").Read());\n" +
                "var f = [\"\", \"UNHEALTHY \\u2014 ACTUATOR FAULT\", \"DOUBLE INDICATION \\u2014 BOTH LIMITS MADE\", \"FAIL TO OPEN\", \"FAIL TO CLOSE\", \"LOSS OF POSITION\", \"UNEXPECTED MOVEMENT\", \"DIRECTION / LIMIT FAULT\"];\n" +
                "if (!flt) return \"\";\n" +
                "return \"\\u26A0  \" + (f[flt] || \"FAULT\");\n", "AutomaticTags");

            // No Local signal on this actuator, so the only mode left to show is in or out of service.
            var mode = MakeTb(sc, "D_Mode", 290, 460, 1000, 44, "", BG_CARD, TXT_MUTED, 0, "Left", 24, true);
            Dyn(mode, "Text", JS_READ +
                "var cfg = r(Tags(\"Valves_DB_SelConfigured\").Read());\n" +
                "return cfg ? \"\" : \"OUT OF SERVICE \\u2014 NOT CONFIGURED\";\n", "AutomaticTags");
            Dyn(mode, "ForeColor", JS_READ +
                "var cfg = r(Tags(\"Valves_DB_SelConfigured\").Read());\n" +
                "return cfg ? 0xFF9AA3B0 : 0xFFE2A800;\n", "AutomaticTags");

            // Commands
            var open = MakeBtn(sc, "Btn_Open", 1340, 272, 260, 120, "OPEN", Color.FromArgb(255, 16, 185, 129), Color.White,
                               Color.FromArgb(255, 52, 211, 153), 2, 30, true);
            SetStr(open, "Authorization", "Operate");
            AddScriptEvent(open, "Tags(\"" + DEMO_V + "_CloseCmd\").Write(false);\nTags(\"" + DEMO_V + "_OpenCmd\").Write(true);\n");
            DemoLockStyling(open, 0xFF10B981);

            var close = MakeBtn(sc, "Btn_Close", 1620, 272, 260, 120, "CLOSE", Color.FromArgb(255, 55, 65, 81), Color.White,
                                Color.FromArgb(255, 107, 114, 128), 2, 30, true);
            SetStr(close, "Authorization", "Operate");
            AddScriptEvent(close, "Tags(\"" + DEMO_V + "_OpenCmd\").Write(false);\nTags(\"" + DEMO_V + "_CloseCmd\").Write(true);\n");
            DemoLockStyling(close, 0xFF374151);

            // Same four latched faults as the real popup's RESET FAULT. It must never write the
            // valve-owned signals: see the ResetFault notes in AddPopupActionButton.
            var reset = MakeBtn(sc, "Btn_Reset", 1340, 412, 540, 100, "RESET FAULT", Color.FromArgb(255, 194, 65, 12), Color.White,
                                Color.FromArgb(255, 249, 115, 22), 2, 26, true);
            SetStr(reset, "Authorization", "Operate");
            AddScriptEvent(reset,
                "Tags(\"" + DEMO_V + "_TimeoutOpenAlarm\").Write(false);\n" +
                "Tags(\"" + DEMO_V + "_TimeoutCloseAlarm\").Write(false);\n" +
                "Tags(\"" + DEMO_V + "_UnexpMove\").Write(false);\n" +
                "Tags(\"" + DEMO_V + "_DirFault\").Write(false);\n");

            // Live signals, so a flipped switch shows up straight away
            MakeRect(sc, "D_SigHdr", 16, 564, 1888, 46, M_HDR, M_HDR, 0);
            MakeTb(sc, "D_SigTtl", 40, 570, 1800, 34, "LIVE SIGNALS", M_TRANS, M_HDRTXT, 0, "Left", 20, true);
            MakeRect(sc, "D_SigCard", 16, 610, 1888, 300, M_BOX, M_LINE, 1);
            // label, address, tag suffix, colour when on, colour when off
            // 2 inputs and 2 outputs: the actuator has no Healthy or Local terminal.
            var sig = new[] {
                new { L = "OPEN LIMIT",    A = "%I0.2", T = "_OpenFB",     On = "0xFF32C785", Off = "0xFF6B7280" },
                new { L = "CLOSED LIMIT",  A = "%I0.3", T = "_ClosedFB",   On = "0xFF32C785", Off = "0xFF6B7280" },
                new { L = "OPEN COMMAND",  A = "%Q0.0", T = "_OpenValve",  On = "0xFF00A2FF", Off = "0xFF6B7280" },
                new { L = "CLOSE COMMAND", A = "%Q0.1", T = "_CloseValve", On = "0xFF00A2FF", Off = "0xFF6B7280" },
            };
            for (int i = 0; i < sig.Length; i++) {
                int cx = 252 + i * 472;   // four equal columns across the 1888-wide card
                var s = sc.ScreenItems.Create<HmiEllipse>("D_Sig" + i);
                s.CenterX = SX(cx); s.CenterY = SY(700); s.RadiusX = (uint)SX(46); s.RadiusY = (uint)SY(46);
                s.BackColor = Color.Gray; s.BorderColor = M_BORDER;
                Dyn(s, "BackColor", JS_READ + "return r(Tags(\"" + DEMO_V + sig[i].T + "\").Read()) ? " + sig[i].On + " : " + sig[i].Off + ";\n", "AutomaticTags");
                MakeTb(sc, "D_SigL" + i, cx - 150, 762, 300, 40, sig[i].L, M_TRANS, M_TEXT, 0, "Center", 22, true);
                MakeTb(sc, "D_SigA" + i, cx - 150, 804, 300, 32, sig[i].A, M_TRANS, M_MUTED, 0, "Center", 18, false);
            }

            // Fault count, same PLC total the FAULTS screen shows
            MakeRect(sc, "D_Cnt", 16, 922, 1888, 70, BG_CARD, BORDER, 1);
            var cnt = MakeTb(sc, "D_CntText", 40, 934, 1840, 46, "", M_TRANS, M_HDRTXT, 0, "Left", 28, true);
            Dyn(cnt, "Text", JS_READ + "return \"ACTIVE FAULTS: \" + (r(Tags(\"Valves_DB_TotalFault\").Read()) || 0);\n", "AutomaticTags");
            Dyn(cnt, "ForeColor", JS_READ + "return (r(Tags(\"Valves_DB_TotalFault\").Read()) || 0) > 0 ? 0xFFF05A4E : 0xFF32C785;\n", "AutomaticTags");
        }
    }
}
