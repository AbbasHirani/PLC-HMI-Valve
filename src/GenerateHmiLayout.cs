using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Drawing;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Controls;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;

// ============================================================
// valveDemo2 — Full 88-Valve HMI Layout Generator v8
// Functional Fixes:
//   1. Corrected UI.OpenFaceplateInPopup parameters order to match Siemens standard:
//      UI.OpenFaceplateInPopup(faceplateType, title, interface, parentScreen, invisible, popupWindowName)
//      and mapped to version string "Valve_Faceplate_V_0_0_1".
//   2. Fixed empty MultilingualText issue by calling .Create("en-US", text)
//      on empty button text collections so the tags display as Valve names.
// ============================================================
namespace ValveDemoHmiBuilder
{
    partial class Program
    {
        private const int    VALVE_COUNT    = 88;
        private const string FACEPLATE_TYPE = "Valve_Faceplate_V_0_0_4";
        private const string HMI_CONNECTION = "HMI_Connection_1";

        private const int SCREEN_W    = 1920;
        private const int SCREEN_H    = 1080;

        // Real target panel (MTP1500, 6AV2128-3QB06-0AX1) is 1366x768, not 1920x1080. Every screen
        // in this file was designed against the SCREEN_W/SCREEN_H virtual canvas above; rather than
        // rewrite every coordinate, TARGET_W/TARGET_H + the SX()/SY() scale functions convert
        // design-space pixels to real-panel pixels at the point they're written to a screen item
        // (inside MakeRect/MakeBtn/MakeTb/MakeLiveText/MakePanel, plus a few raw assignment sites
        // that don't go through those helpers). Width and height scale by very slightly different
        // factors (1366/1920 vs 768/1080) because 1366x768 isn't an exact 16:9 ratio - it's rounded
        // up from 1365.33 for even pixel width - so SX/SY are kept separate rather than one constant.
        private const int TARGET_W = 1366;
        private const int TARGET_H = 768;
        private const int MIN_FONT_SIZE = 10; // floor so scaled-down text stays legible

        private static int SX(int v) { return (int)Math.Round(v * (double)TARGET_W / SCREEN_W); }
        private static int SY(int v) { return (int)Math.Round(v * (double)TARGET_H / SCREEN_H); }
        private static int SFont(int v) { return Math.Max(MIN_FONT_SIZE, (int)Math.Round(v * (double)TARGET_H / SCREEN_H)); }

        private const int HEADER_H    = 48;
        private const int SUMMARY_H   = 44;
        private const int CONTENT_TOP = HEADER_H + SUMMARY_H + 8; // 100px

        private const int CARD_W      = 165;
        private const int CARD_H      = 110;
        private const int CARD_GAP_X  = 8;
        private const int CARD_GAP_Y  = 8;
        private const int GRID_COLS   = 11;
        private const int GRID_LEFT   = 12;

        // Theme colors
        private static readonly Color BG_DARK    = Color.FromArgb(255, 30,  33,  40);
        private static readonly Color BG_HEADER  = Color.FromArgb(255, 15,  17,  23);
        private static readonly Color BG_SUMMARY = Color.FromArgb(255, 34,  38,  47);
        private static readonly Color BG_CARD    = Color.FromArgb(255, 42,  47,  58);
        private static readonly Color TEAL       = Color.FromArgb(255,  0, 168, 181);
        private static readonly Color COLOR_FAIL = Color.FromArgb(255, 231, 76,  60);
        private static readonly Color COLOR_OK   = Color.FromArgb(255,  46, 125, 50);
        private static readonly Color BORDER     = Color.FromArgb(255, 53,  60,  78);

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int i = args.Name.IndexOf(',');
            string n = i == -1 ? args.Name : args.Name.Substring(0, i);
            string[] dirs = {
                @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20",
                @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI",
                @"C:\Program Files\Siemens\Automation\Portal V20\Bin"
            };
            foreach (var d in dirs) { string p = Path.Combine(d, n + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            // --only=Home,Bilge  → rebuild just the listed screens (keys: Home, Bilge, Fwd, Aft,
            //                       Diag, Login, Alarms). Screen_Popup is always rebuilt regardless
            //                       — it's cheap and every screen's valve badges open it.
            // --fix-tags         → repair pass only: re-binds HMI tag addresses that got stuck
            //                       broken because they were created before the PLC was compiled.
            //                       Skips PLC import and ALL screen rebuilding.
            // (no flags)         → rebuild ALL screens (full run)
            HashSet<string> only = null;
            bool fixTags = false;
            string dumpTag = null;
            string exportBlock = null;
            bool importOnly = false;
            bool finishLoginAuth = false;
            foreach (var a in args) {
                if (a.StartsWith("--only=")) {
                    only = new HashSet<string>(a.Substring(7).Split(','), StringComparer.OrdinalIgnoreCase);
                } else if (a == "--fix-tags") {
                    fixTags = true;
                } else if (a.StartsWith("--dump-tag=")) {
                    dumpTag = a.Substring(11);
                } else if (a.StartsWith("--export-block=")) {
                    exportBlock = a.Substring(15);
                } else if (a == "--import-only") {
                    importOnly = true;
                } else if (a == "--finish-login-auth") {
                    finishLoginAuth = true;
                }
            }
            if (finishLoginAuth) { try { RunFinishLoginAuth(); } catch (Exception ex) { Console.WriteLine("\n[ERROR] " + ex); } Console.WriteLine("\nDone."); return; }
            try { Run(only, fixTags, dumpTag, exportBlock, importOnly); }
            catch (Exception ex) { Console.WriteLine("\n[ERROR] " + ex); }
            Console.WriteLine("\nDone."); 
        }

        static bool Want(HashSet<string> only, string key) { return only == null || only.Contains(key); }

        static void Run(HashSet<string> only, bool fixTags = false, string dumpTag = null, string exportBlock = null, bool importOnly = false)
        {
            var procs = TiaPortal.GetProcesses();
            Console.WriteLine("  [DEBUG] TiaPortal.GetProcesses() found " + procs.Count + " process(es).");
            if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
            Console.WriteLine("Searching for active TIA Portal project...");
            TiaPortal portal = null;
            Project project = null;
            foreach (var p in procs) {
                try {
                    var att = p.Attach();
                    Console.WriteLine("  [DEBUG] Attach() succeeded, Projects.Count=" + (att == null ? "null" : att.Projects.Count.ToString()));
                    if (att != null && att.Projects.Count > 0) {
                        portal = att;
                        project = att.Projects[0];
                        break;
                    }
                } catch (Exception ex) {
                    Console.WriteLine("  [DEBUG] Attach() threw: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            if (portal == null || project == null) {
                Console.WriteLine("[ERROR] Could not attach to active TIA Portal project.");
                return;
            }
            Console.WriteLine("Attached to Project: " + project.Name);

            if (fixTags) {
                Device fixHmiDevice = FindDeviceByPartialName(project, "HMI");
                if (fixHmiDevice == null) { Console.WriteLine("[ERROR] HMI device not found."); return; }
                HmiSoftware fixHmi = FindHmiSoftware(fixHmiDevice);
                if (fixHmi == null) { Console.WriteLine("[ERROR] HMI software not found."); return; }
                Console.WriteLine("\n[FIX-TAGS] Re-binding tags that were created before the PLC was compiled...");
                CreateSummaryHmiTags(fixHmi, true);
                Console.WriteLine("\n=== Fix-tags complete! ===");
                return;
            }

            if (dumpTag != null) {
                Device dumpHmiDevice = FindDeviceByPartialName(project, "HMI");
                if (dumpHmiDevice == null) { Console.WriteLine("[ERROR] HMI device not found."); return; }
                HmiSoftware dumpHmi = FindHmiSoftware(dumpHmiDevice);
                if (dumpHmi == null) { Console.WriteLine("[ERROR] HMI software not found."); return; }
                var table = dumpHmi.TagTables.Find("ValveTags");
                if (table == null) { Console.WriteLine("[ERROR] ValveTags table not found."); return; }
                var tag = table.Tags.Find(dumpTag);
                if (tag == null) { Console.WriteLine("[ERROR] Tag " + dumpTag + " not found."); return; }
                Console.WriteLine("\n[DUMP-TAG] " + dumpTag + " (" + tag.GetType().FullName + ")");
                foreach (var pp in tag.GetType().GetProperties()) {
                    try {
                        object val = pp.GetValue(tag, null);
                        Console.WriteLine("  " + pp.Name + " (" + pp.PropertyType.Name + ", CanWrite=" + pp.CanWrite + ") = " + (val == null ? "null" : val.ToString()));
                    } catch (Exception ex) {
                        Console.WriteLine("  " + pp.Name + " -> [threw " + ex.GetType().Name + ": " + ex.Message + "]");
                    }
                }
                Console.WriteLine("\n=== Dump-tag complete! ===");
                return;
            }

            if (exportBlock != null) {
                PlcSoftware plc = null;
                foreach (Device d in project.Devices) {
                    foreach (DeviceItem it in d.DeviceItems) {
                        var c = it.GetService<SoftwareContainer>();
                        if (c != null && c.Software is PlcSoftware) { plc = c.Software as PlcSoftware; break; }
                    }
                    if (plc != null) break;
                }
                if (plc == null) { Console.WriteLine("[ERROR] PlcSoftware not found."); return; }
                var block = plc.BlockGroup.Blocks.Find(exportBlock);
                if (block == null) { Console.WriteLine("[ERROR] Block " + exportBlock + " not found."); return; }
                string outPath = @"C:\Users\Admin\Documents\Automation\valveDemo2\temp_export_" + exportBlock + ".xml";
                block.Export(new FileInfo(outPath), ExportOptions.WithDefaults);
                Console.WriteLine("\n[EXPORT-BLOCK] " + exportBlock + " -> " + outPath);
                Console.WriteLine("\n=== Export-block complete! ===");
                return;
            }

            // Import updated PLC blocks (FB_ValveLoop with Configured headroom logic)
            ImportPlcBlocks(project);

            if (importOnly) {
                Console.WriteLine("\n=== Import-only complete! (PLC blocks re-imported, no HMI changes) ===");
                return;
            }

            Device hmiDevice = FindDeviceByPartialName(project, "HMI");
            if (hmiDevice == null) { Console.WriteLine("[ERROR] HMI device not found."); return; }
            HmiSoftware hmi = FindHmiSoftware(hmiDevice);
            if (hmi == null) { Console.WriteLine("[ERROR] HMI software not found."); return; }

            // Create HMI Summary Tags with valid PLC Tag references
            CreateSummaryHmiTags(hmi);

            // STEP 1 – Rebuild screens for Marine UI Redesign
            Console.WriteLine("\n[STEP 1] Rebuilding Marine screens for 1920x1080 resolution...");
            
            // Clean up legacy screens
            HmiScreen old1 = FindScreen(hmi, "Screen_1");
            if (old1 != null) { try { CleanScreen(old1); old1.Delete(); } catch {} }
            HmiScreen oldP = FindScreen(hmi, "Screen_Popup");
            if (oldP != null) { try { CleanScreen(oldP); oldP.Delete(); } catch {} }

            if (Want(only, "Home")) {
                HmiScreen scHome = RecreateScreen(hmi, "Screen_Home");
                if (scHome != null) BuildScreenHome(scHome);
            } else Console.WriteLine("  Skipping Screen_Home (not in --only)...");

            // Mimic valves open the SBO popup, so it must exist alongside every screen — always
            // rebuilt regardless of selection (cheap, ~30s, and a universal dependency).
            EnsurePopupScreen(hmi);

            // BuildAlarmScreen already exists (below) and was fully written previously but
            // never actually wired into Run() — the nav bar pointed at a screen that was
            // never created. Enabling it now that the nav is being made fully functional.
            if (Want(only, "Alarms")) EnsureAlarmScreen(hmi);
            else Console.WriteLine("  Skipping Screen_Alarms (not in --only)...");

            // AlarmColumns-only: patch columns on the EXISTING AlarmView without deleting the screen.
            // Run this after Pass-2 alarm additions (--only=DiscreteAlarms) to re-apply column config.
            if (Want(only, "AlarmColumns")) PatchAlarmColumns(hmi);

            // The remaining nav targets have no dedicated screen design yet.
            // Rather than leave their nav buttons dead-clicking, each gets a
            if (Want(only, "Bilge")) {
                HmiScreen scBilge = RecreateScreen(hmi, "Screen_Bilge");
                if (scBilge != null) BuildZoneScreen(scBilge, "Screen_Bilge", "BILGE AND FIRE", 29, 56, "Er", 14);
            } else Console.WriteLine("  Skipping Screen_Bilge (not in --only)...");

            if (Want(only, "Fwd")) {
                HmiScreen scFwd = RecreateScreen(hmi, "Screen_FwdBallast");
                if (scFwd != null) BuildZoneScreen(scFwd, "Screen_FwdBallast", "FORWARD BALLAST", 57, 88, "Fwd", 16);
            } else Console.WriteLine("  Skipping Screen_FwdBallast (not in --only)...");

            if (Want(only, "Aft")) {
                HmiScreen scAft = RecreateScreen(hmi, "Screen_AftBallast");
                if (scAft != null) BuildZoneScreen(scAft, "Screen_AftBallast", "AFT BALLAST", 1, 28, "Aft", 14);
            } else Console.WriteLine("  Skipping Screen_AftBallast (not in --only)...");

            if (Want(only, "Diag")) {
                HmiScreen scDiag = RecreateScreen(hmi, "Screen_Diagnostics");
                if (scDiag != null) BuildConfigScreen(scDiag);
            } else Console.WriteLine("  Skipping Screen_Diagnostics (not in --only)...");

            // Discrete alarms can be created/updated independently — does NOT touch Screen_Alarms layout.
            if (Want(only, "Alarms") || Want(only, "DiscreteAlarms")) {
                CreateAlarms(hmi, "Valves_DB");
            }

            if (Want(only, "Login")) {
                HmiScreen scLogin = RecreateScreen(hmi, "Screen_Login");
                if (scLogin != null) BuildLoginScreen(scLogin);
            } else Console.WriteLine("  Skipping Screen_Login (not in --only)...");

            Console.WriteLine("\n=== Complete! ===");
            Console.WriteLine("Screens: Screen_Home, Screen_Popup, Screen_Alarms, Screen_Bilge, Screen_FwdBallast, Screen_AftBallast, Screen_Diagnostics, Screen_Login");
            Console.WriteLine("All 7 nav bar buttons now target real screens.");
            Console.WriteLine("\nPress Enter to exit...");
        }

        static HmiScreen RecreateScreen(HmiSoftware hmi, string screenName)
        {
            HmiScreen existing = FindScreen(hmi, screenName);
            if (existing != null) {
                Console.WriteLine("  Deleting existing " + screenName + "...");
                try {
                    CleanScreen(existing);
                    existing.Delete();
                } catch (Exception ex) {
                    Console.WriteLine("[ERROR] Could not delete existing screen " + screenName + ": " + ex.Message);
                    Console.WriteLine("Please close the screen in the TIA Portal UI before running the build.");
                    throw;
                }
            }

            Console.WriteLine("  Creating new clean " + screenName + "...");
            var sp = hmi.GetType().GetProperty("Screens");
            if (sp == null) return null;
            var screens = sp.GetValue(hmi, null);
            var cm = screens.GetType().GetMethod("Create", new Type[]{ typeof(string) });
            if (cm == null) return null;

            HmiScreen newScreen = (HmiScreen)cm.Invoke(screens, new object[]{ screenName });
            if (newScreen != null) {
                // Exact target panel resolution (MTP1500, 1366x768) - not run through SX()/SY()
                // rounding since this is the actual physical canvas size, not a scaled element.
                SetPropUInt(newScreen, "Width",  (uint)TARGET_W);
                SetPropUInt(newScreen, "Height", (uint)TARGET_H);
            }
            return newScreen;
        }

        static void EnsureAlarmScreen(HmiSoftware hmi)
        {
            HmiScreen sc = RecreateScreen(hmi, "Screen_Alarms");
            if (sc != null) BuildAlarmScreen(sc);
        }

        // Patch columns on an EXISTING AlarmView without rebuilding the screen.
        // Use --only=AlarmColumns after adding new alarms via --only=DiscreteAlarms.
        static void PatchAlarmColumns(HmiSoftware hmi)
        {
            Console.WriteLine("  [AlarmColumns] Finding existing Screen_Alarms...");
            HmiScreen sc = FindScreen(hmi, "Screen_Alarms");
            if (sc == null) { Console.WriteLine("  [AlarmColumns] Screen_Alarms not found — run --only=Alarms first."); return; }
            foreach (var item in sc.ScreenItems) {
                if (item.Name == "AlarmView" && item is HmiAlarmControl) {
                    Console.WriteLine("  [AlarmColumns] Found AlarmView. Applying column config...");
                    ConfigureAlarmColumns((HmiAlarmControl)item);
                    Console.WriteLine("  [AlarmColumns] Done.");
                    return;
                }
            }
            Console.WriteLine("  [AlarmColumns] AlarmView not found on Screen_Alarms.");
        }

        static void EnsurePopupScreen(HmiSoftware hmi)
        {
            HmiScreen sc = RecreateScreen(hmi, "Screen_Popup");
            if (sc == null) return;
            SetPropUInt(sc, "Width", (uint)SX(460));
            SetPropUInt(sc, "Height", (uint)SY(360));
            sc.BackColor = BG_DARK;

            // Outer canvas
            MakeRect(sc, "Pop_BG", 0, 0, 460, 360, BG_DARK, BORDER, 2);

            // ─── HEADER (Y=0..38): Dark bar + Centered Valve Name ─────────
            MakeRect(sc, "Pop_Header", 0, 0, 460, 38, BG_HEADER, BORDER, 1);

            // Valve name (centered horizontally across 460px width)
            var titleIO = sc.ScreenItems.Create<HmiIOField>("Pop_Title");
            titleIO.Left = SX(0); titleIO.Top = SY(6); titleIO.Width = (uint)SX(460); titleIO.Height = (uint)SY(26);
            titleIO.BackColor = BG_HEADER; titleIO.ForeColor = Color.White;
            titleIO.BorderColor = BG_HEADER; titleIO.BorderWidth = 0;
            SetPropEnum(titleIO, "IOFieldType", "Output");
            SetPropEnum(titleIO, "TextHorizontalAlignment", "Center");
            SetPropEnum(titleIO, "HorizontalAlignment", "Center");
            SetMLText(titleIO, "Text", "VALVE CONTROL PANEL");
            try {
                var tDyn = titleIO.Dynamizations.Create<ScriptDynamization>("ProcessValue");
                tDyn.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                    "let num = (\"000\" + (idx || 1)).slice(-3);\n" +
                    "return \"VALVE V-\" + num + \" — CONTROL PANEL\";";
                tDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}

            // ─── STATUS CARD (Y=46..86): Text only ─────────────
            MakeRect(sc, "Pop_StatusCard", 15, 46, 430, 40, BG_CARD, BORDER, 1);

            var statusIO = sc.ScreenItems.Create<HmiIOField>("Pop_StatusText");
            statusIO.Left = SX(20); statusIO.Top = SY(50); statusIO.Width = (uint)SX(420); statusIO.Height = (uint)SY(32);
            statusIO.BackColor = BG_CARD; statusIO.ForeColor = Color.White;
            statusIO.BorderColor = BG_CARD; statusIO.BorderWidth = 0;
            SetPropEnum(statusIO, "IOFieldType", "Output");
            SetMLText(statusIO, "Text", "V-001  |  INITIALIZING  |  N/A  |  AUTO");
            try {
                var sDyn = statusIO.Dynamizations.Create<ScriptDynamization>("ProcessValue");
                sDyn.ScriptCode =
                    // Reads the same "_State" tag (Valves_DB.StateCode[i]) every other screen uses,
                    // instead of independently recomputing MOVING/FAULT/etc. from raw feedbacks here.
                    // That second copy of the logic never included the C/D timeout latches, let alone
                    // the later UnexpMove/Loss-of-Position conditions, and had silently drifted out
                    // of sync with StateCode's real priority chain (0=UNCONFIGURED 1=FAULT 2=LOCAL
                    // 3=OPEN 4=CLOSED 5=MOVING) - one source of truth now, not two.
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                    "let vNum = (\"000\" + (idx || 1)).slice(-3);\n" +
                    "let vTag = \"V\" + vNum;\n" +
                    "let code = readTag(Tags(vTag + \"_State\").Read());\n" +
                    "let healthy = readTag(Tags(vTag + \"_Healthy\").Read());\n" +
                    "let local   = readTag(Tags(vTag + \"_LocalMode\").Read());\n\n" +
                    "let stNames = [\"UNCONFIGURED\", \"FAULT\", \"LOCAL MODE\", \"OPEN\", \"CLOSED\", \"MOVING\"];\n" +
                    "let st = stNames[code] || \"MOVING\";\n" +
                    "let hl = (code === 0) ? \"N/A\" : (healthy ? \"HEALTHY\" : \"FAULT\");\n" +
                    "let md = local ? \"LOCAL\" : \"AUTO\";\n" +
                    "return \"V-\" + vNum + \"  |  \" + st + \"  |  \" + hl + \"  |  MODE: \" + md;";
                sDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}

            // ─── OPEN / CLOSE Buttons (Y=96..144) ─────────────────────────────────
            var btnOpen = sc.ScreenItems.Create<HmiButton>("Btn_Open");
            btnOpen.Left = SX(20); btnOpen.Top = SY(96); btnOpen.Width = (uint)SX(200); btnOpen.Height = (uint)SY(48);
            btnOpen.BackColor = Color.FromArgb(255, 16, 185, 129); btnOpen.ForeColor = Color.White;
            btnOpen.BorderColor = Color.FromArgb(255, 52, 211, 153); btnOpen.BorderWidth = 2;
            SetMLText(btnOpen, "Text", "▲ OPEN VALVE");
            SetStr(btnOpen, "Authorization", "Operate");
            AddPopupActionButton(btnOpen, "OpenCmd");

            var btnClose = sc.ScreenItems.Create<HmiButton>("Btn_Close");
            btnClose.Left = SX(240); btnClose.Top = SY(96); btnClose.Width = (uint)SX(200); btnClose.Height = (uint)SY(48);
            btnClose.BackColor = Color.FromArgb(255, 55, 65, 81); btnClose.ForeColor = Color.White;
            btnClose.BorderColor = Color.FromArgb(255, 107, 114, 128); btnClose.BorderWidth = 2;
            SetMLText(btnClose, "Text", "▼ CLOSE VALVE");
            SetStr(btnClose, "Authorization", "Operate");
            AddPopupActionButton(btnClose, "CloseCmd");

            // ─── Large Status Circle (Diameter=90, Y=155..245) ──
            var dot = sc.ScreenItems.Create<HmiEllipse>("Pop_Dot");
            dot.CenterX = SX(230); dot.CenterY = SY(200); dot.RadiusX = (uint)SX(45); dot.RadiusY = (uint)SY(45);
            dot.BackColor = TEAL; dot.BorderColor = Color.White;
            try {
                // Same fix as Pop_StatusText: read the single precomputed _State tag instead of an
                // independent copy of the raw-feedback logic (which was missing UnexpMove/Loss-of-
                // Position, same bug, just duplicated here too). Also cuts this from 7 tag reads
                // every 100ms down to 2, and AutomaticTags means it only re-evaluates when a
                // dependency actually changes - including the 1Hz clock tag on every tick, so the
                // FAULT flash still works exactly as before.
                var dotDyn = dot.Dynamizations.Create<ScriptDynamization>("BackColor");
                dotDyn.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                    "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                    "let code  = readTag(Tags(vTag + \"_State\").Read());\n" +
                    "let flash = readTag(Tags(\"Valves_DB_Clock1Hz\").Read());\n\n" +
                    "if (code === 0) return 0xFF8E8E93;\n" +
                    "if (code === 1) return flash ? 0xFFFF0000 : 0xFF3A0000;\n" +
                    "if (code === 2) return 0xFFFF9F0A;\n" +
                    "if (code === 3) return 0xFF32C785;\n" +
                    "if (code === 4) return 0xFF4B5563;\n" +
                    "return 0xFF00A2FF;\n";
                dotDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}

            // State label below big circle (Y=255..281)
            var stateLabel = sc.ScreenItems.Create<HmiIOField>("Pop_StateLabel");
            stateLabel.Left = SX(30); stateLabel.Top = SY(255); stateLabel.Width = (uint)SX(400); stateLabel.Height = (uint)SY(26);
            stateLabel.BackColor = BG_DARK; stateLabel.ForeColor = Color.White;
            stateLabel.BorderColor = BG_DARK; stateLabel.BorderWidth = 0;
            SetPropEnum(stateLabel, "IOFieldType", "Output");
            SetMLText(stateLabel, "Text", "STATE: INITIALIZING");
            try {
                // Same _State-based fix as Pop_Dot/Pop_StatusText above - this was the third
                // independent copy of the same now-fixed logic gap.
                var slDyn = stateLabel.Dynamizations.Create<ScriptDynamization>("ProcessValue");
                slDyn.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                    "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                    "let code = readTag(Tags(vTag + \"_State\").Read());\n" +
                    "let names = [\"⬤  UNCONFIGURED\", \"⬤  FAULT\", \"⬤  LOCAL MODE\", \"⬤  FULLY OPEN\", \"⬤  FULLY CLOSED\", \"⬤  MOVING\"];\n" +
                    "return names[code] || \"⬤  MOVING\";\n";
                slDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}

            // ─── RESET FAULT Button (Left=15, Y=292, Width=138, Height=46) ───────────
            var btnReset = sc.ScreenItems.Create<HmiButton>("Btn_Reset");
            btnReset.Left = SX(15); btnReset.Top = SY(292); btnReset.Width = (uint)SX(138); btnReset.Height = (uint)SY(46);
            btnReset.BackColor = Color.FromArgb(255, 194, 65, 12); btnReset.ForeColor = Color.White;
            btnReset.BorderColor = Color.FromArgb(255, 249, 115, 22); btnReset.BorderWidth = 2;
            SetMLText(btnReset, "Text", "⚡ RESET FAULT");
            SetStr(btnReset, "Authorization", "Operate");
            AddPopupActionButton(btnReset, "ResetFault");

            // ─── SERVICE ON/OFF Toggle Switch (Left=160, Y=292, Width=138, Height=46) ──
            var btnService = sc.ScreenItems.Create<HmiButton>("Btn_Service");
            btnService.Left = SX(160); btnService.Top = SY(292); btnService.Width = (uint)SX(138); btnService.Height = (uint)SY(46);
            btnService.BackColor = Color.FromArgb(255, 58, 58, 60); btnService.ForeColor = Color.White;
            btnService.BorderColor = TEAL; btnService.BorderWidth = 2;
            SetMLText(btnService, "Text", "🛠️ SERVICE:  OFF");
            SetStr(btnService, "Authorization", "Operate");
            AddPopupActionButton(btnService, "ToggleService");
            try {
                var srvDyn = btnService.Dynamizations.Create<ScriptDynamization>("Text");
                srvDyn.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                    "let vNum = (\"000\" + (idx || 1)).slice(-3);\n" +
                    "let vTag = \"V\" + vNum;\n" +
                    "let cfg = readTag(Tags(vTag + \"_Configured\").Read());\n" +
                    "return cfg ? \"🛠️ SERVICE:  ON\" : \"🛠️ SERVICE:  OFF\";\n";
                srvDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "T100ms");
            } catch {}
            try {
                var srvBg = btnService.Dynamizations.Create<ScriptDynamization>("BackColor");
                srvBg.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                    "let vNum = (\"000\" + (idx || 1)).slice(-3);\n" +
                    "let vTag = \"V\" + vNum;\n" +
                    "let cfg = readTag(Tags(vTag + \"_Configured\").Read());\n" +
                    "return cfg ? 0xFF00C7BE : 0xFF3A3A3C;\n";
                srvBg.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "T100ms");
            } catch {}

            // ─── SIMULATE STUCK Toggle Switch (Left=305, Y=292, Width=140, Height=46) ──
            var btnStuck = sc.ScreenItems.Create<HmiButton>("Btn_Stuck");
            btnStuck.Left = SX(305); btnStuck.Top = SY(292); btnStuck.Width = (uint)SX(140); btnStuck.Height = (uint)SY(46);
            btnStuck.BackColor = Color.FromArgb(255, 58, 58, 60); btnStuck.ForeColor = Color.White;
            btnStuck.BorderColor = Color.FromArgb(255, 234, 179, 8); btnStuck.BorderWidth = 2;
            SetMLText(btnStuck, "Text", "⚠️ STUCK: OFF");
            SetStr(btnStuck, "Authorization", "Operate");
            AddPopupActionButton(btnStuck, "ToggleStuck");
            try {
                var stkDyn = btnStuck.Dynamizations.Create<ScriptDynamization>("Text");
                stkDyn.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                    "let vNum = (\"000\" + (idx || 1)).slice(-3);\n" +
                    "let vTag = \"V\" + vNum;\n" +
                    "let stk = readTag(Tags(vTag + \"_Stuck\").Read());\n" +
                    "return stk ? \"⚠️ STUCK: ON\" : \"⚠️ STUCK: OFF\";\n";
                stkDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "T100ms");
            } catch {}
            try {
                var stkBg = btnStuck.Dynamizations.Create<ScriptDynamization>("BackColor");
                stkBg.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                    "let vNum = (\"000\" + (idx || 1)).slice(-3);\n" +
                    "let vTag = \"V\" + vNum;\n" +
                    "let stk = readTag(Tags(vTag + \"_Stuck\").Read());\n" +
                    "return stk ? 0xFF00A2FF : 0xFF3A3A3C;\n";
                stkBg.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "T100ms");
            } catch {}

            Console.WriteLine("  Screen_Popup built: Direct PLC tag reading for live status, bracket-notation close button.");
        }

        static void BuildAlarmScreen(HmiScreen sc)
        {
            Console.WriteLine("  Building 1920x1080 Alarm & Fault Diagnostics layout on Screen_Alarms...");

            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);
            BuildHomeHeader(sc);
            BuildNav(sc, "Screen_Alarms");

            // Total Fault Count Label
            var countLbl = MakeLiveText(sc, "TotalFaults", 16, 198, 200, 26, M_RED, "Left", 14, true);
            SetMLText(countLbl, "Text", "ACTIVE FAULTS: {0}");
            try {
                var cDyn = countLbl.Dynamizations.Create<ScriptDynamization>("Text");
                cDyn.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let n = readTag(Tags(\"Valves_DB_TotalFault\").Read()) || 0;\n" +
                    "let prev = readTag(Tags(\"Internal_PrevFaultCount\").Read()) || 0;\n" +
                    "if (prev == 0 && n > 0) {\n" +
                    "  if (!globalThis._beepTimer) globalThis._beepTimer = setInterval(function() { try { fetch('http://127.0.0.1:8081/beep/'); } catch(e){} }, 1500);\n" +
                    "}\n" +
                    "if (prev > 0 && n == 0) {\n" + 
                    "  if (globalThis._beepTimer) { clearInterval(globalThis._beepTimer); globalThis._beepTimer = null; }\n" +
                    "}\n" +
                    "Tags(\"Internal_PrevFaultCount\").Write(n);\n" +
                    "return \"ACTIVE FAULTS: \" + n;";
                cDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "T1s");
            } catch {}

            // SYSTEM STATUS panel removed per user request - the alarm table now takes the full
            // screen width instead. System health is still fully covered by the 9 System_* alarms
            // (PLC CPU/RIO x3/I-O Module/Power-UPS/Network/HMI Heartbeat/General) already showing
            // in the same table when active, so no information was lost, just the redundant
            // always-visible side panel.

            // ── UX Controls (marine-themed: MakeBtn uses M_* constants from MarineScreens.cs) ─────
            // ACTIVE ALARMS — accent blue, active state indicator
            var btnActive = MakeBtn(sc, "Btn_ActiveAlarms", 16, 230, 190, 46, "ACTIVE ALARMS", M_ACCENT, M_HDRTXT, M_BORDER, 1, 14, true);
            AddScriptEvent(btnActive, "Screen.Items(\"AlarmView\").AlarmSourceType = 1;");

            // ALARM HISTORY — navy panel header colour
            var btnHist = MakeBtn(sc, "Btn_AlarmHistory", 216, 230, 190, 46, "ALARM HISTORY", M_HDR, M_HDRTXT, M_BORDER, 1, 14, false);
            AddScriptEvent(btnHist, "Screen.Items(\"AlarmView\").AlarmSourceType = 2;");

            // ACKNOWLEDGE ALL — warning yellow, right-aligned to the widened alarm view edge
            // (16 + 1888 = 1904, the same 1920-16px right margin every other screen uses; 1904-300=1604)
            var btnAck = MakeBtn(sc, "Btn_AckAll", 1604, 230, 300, 46, "ACKNOWLEDGE ALL", M_YELLOW, M_TEXT, M_BORDER, 1, 14, true);
            SetStr(btnAck, "Authorization", "Operate");
            AddScriptEvent(btnAck,
                "if (globalThis._beepTimer) { clearInterval(globalThis._beepTimer); globalThis._beepTimer = null; }\n" +
                "try {\n" +
                "  HMIRuntime.Alarming.GetActiveAlarms(HMIRuntime.Language).then(function(alarms) {\n" +
                "    for (var i = 0; i < alarms.length; i++) {\n" +
                "      try { HMIRuntime.Alarming.Alarms(alarms[i].Name).Acknowledge(); } catch(e) {}\n" +
                "    }\n" +
                "  });\n" +
                "} catch(e) {}");

            // =========================================================================
            // COM DEADLOCK WARNING: Creating an HmiAlarmControl deadlocks Openness API.
            // It MUST be the absolute last thing we do on this screen.
            // =========================================================================
            try {
                Console.WriteLine("  [DEBUG] Placing HmiAlarmControl (this may take several minutes)...");
                Console.Out.Flush();
                var alarmCtrl = sc.ScreenItems.Create<HmiAlarmControl>("AlarmView");
                alarmCtrl.Left = SX(16);
                alarmCtrl.Top = SY(286);
                alarmCtrl.Width = (uint)SX(1888); // full width now that the System Status side panel is gone
                alarmCtrl.Height = (uint)SY(762);
                // Without this, native WinCC runtime system alarms (PlcInStopAlarm,
                // PlcDisconnectedAlarm, PhysicalMemorySpace, etc.) show up mixed in with our own
                // valve/system alarms - confirmed live via reflection that Filter defaults to "".
                alarmCtrl.Filter = "AlarmClassName=\"ValveFault\" OR AlarmClassName=\"ValveWarning\" OR AlarmClassName=\"ValveEvent\" OR AlarmClassName=\"System\"";
                Console.WriteLine("  [DEBUG] HmiAlarmControl placed. Configuring columns...");
                Console.Out.Flush();
                ConfigureAlarmColumns(alarmCtrl);
                Console.WriteLine("  [DEBUG] Column configuration done. Applying marine theme...");
                Console.Out.Flush();
                ApplyAlarmMarineTheme(alarmCtrl);
                Console.WriteLine("  [DEBUG] Theme applied.");
            } catch (Exception ex) {
                Console.WriteLine("  [WARN] AlarmControl creation/config failed: " + ex.Message);
            }
            
            Console.WriteLine("  Screen_Alarms built successfully.");
        }

        // ── Column configuration ────────────────────────────────────────────────────────
        // Live-verified column names from TIA V20 (49 total). Previous code used wrong
        // names (RaiseTime, AlarmID, EventText, AlarmState) — these are the real ones.
        // Widths: 180+100+150+600+200+180 = 1410px (AlarmView is 1420px wide).
        static void ConfigureAlarmColumns(HmiAlarmControl alarmCtrl)
        {
            int applied = 0;
            foreach (var col in alarmCtrl.AlarmView.Columns) {
                switch (col.Name) {
                    case "Raise time":       col.Visible = true;  col.Width = (uint)SX(170); SetMLText(col.Header, "Text", "TIME");        applied++; break;
                    case "Priority":         col.Visible = true;  col.Width = (uint)SX(90);  SetMLText(col.Header, "Text", "PRIORITY");     applied++; break;
                    case "Name":             col.Visible = true;  col.Width = (uint)SX(140); SetMLText(col.Header, "Text", "ALARM ID");     applied++; break;
                    case "Alarm text":       col.Visible = true;  col.Width = (uint)SX(750); SetMLText(col.Header, "Text", "DESCRIPTION");  applied++; break;
                    case "Area":             col.Visible = true;  col.Width = (uint)SX(180); SetMLText(col.Header, "Text", "SYSTEM");       applied++; break;
                    case "Alarm state":      col.Visible = true;  col.Width = (uint)SX(160); SetMLText(col.Header, "Text", "STATUS");       applied++; break;
                    // ACKNOWLEDGED BY ("User name") removed - confirmed never populates regardless
                    // of acknowledge path (native toolbar icon or our script), even when genuinely
                    // logged in. Real "who did what" tracking belongs to the Audit Trail work
                    // (InsertElectronicRecord) instead, not this column - see the memory note.
                    case "Acknowledge time": col.Visible = true;  col.Width = (uint)SX(170); SetMLText(col.Header, "Text", "ACK TIME");     applied++; break;
                    case "Duration":         col.Visible = true;  col.Width = (uint)SX(140); SetMLText(col.Header, "Text", "DURATION");     applied++; break;
                    default:                 col.Visible = false; break;
                }
            }
            Console.WriteLine("  [Columns] Applied to " + applied + "/8 target columns (" + alarmCtrl.AlarmView.Columns.Count + " total).");
            if (applied < 8) Console.WriteLine("  [Columns] WARNING: expected 8, got " + applied + " — check column name spelling.");
        }

        // The Alarm Control turned out to have a much richer styling surface than assumed - never
        // checked properly until now. UseAlarmColors stays true so active/raised alarms still show
        // their native red/priority coloring; this only re-themes the "normal" (no-alarm) look to
        // match the marine light theme used everywhere else, plus bumps row height/font size for a
        // touchscreen panel instead of the default desktop-grid sizing (28px rows, size-14 font).
        static void ApplyAlarmMarineTheme(HmiAlarmControl alarmCtrl)
        {
            try {
                alarmCtrl.BackColor = M_BOX;
                var av = alarmCtrl.AlarmView;
                av.BackColor = M_BOX;
                av.ForeColor = M_TEXT;
                av.AlternateBackColor = M_ZEBRA;
                av.AlternateForeColor = M_TEXT;
                av.GridLineColor = M_LINE;
                av.RowHeight = 40;
                av.Font.Size = 15;

                var hs = av.HeaderSettings;
                hs.HeaderBackColor = M_HDRBAND;
                hs.HeaderForeColor = M_TEXT;
                hs.HeaderGridLineColor = M_BORDER;
                var weightProp = hs.Font.GetType().GetProperty("Weight");
                weightProp.SetValue(hs.Font, Enum.Parse(weightProp.PropertyType, "Bold"), null);
                hs.Font.Size = 15;

                Console.WriteLine("  [Theme] Marine theme applied to Alarm Control (row height 40, font 15, header band).");
            } catch (Exception ex) {
                Console.WriteLine("  [Theme] WARNING: could not fully apply theme: " + Root(ex));
            }
        }

        static void BuildOverviewScreen(HmiScreen sc)
        {
            Console.WriteLine("  Building 1920x1080 Overview layout on Screen_1...");

            // Background canvas
            MakeRect(sc, "OV_BG", 0, 0, SCREEN_W, SCREEN_H, BG_DARK, BG_DARK, 0);

            // Header and Summary bars
            BuildHeaderBar(sc, "Valve Control System — 88 Valves Overview", true);
            BuildSummaryBar(sc);

            // Place 88 cards
            Console.WriteLine("  Placing 88 interactive valve buttons...");
            for (int v = 1; v <= VALVE_COUNT; v++) {
                if (v == 1 || v % 10 == 0) Console.WriteLine("    -> Valve " + v + " of 88...");
                int col  = (v - 1) % GRID_COLS;
                int row  = (v - 1) / GRID_COLS;
                int left = GRID_LEFT + col * (CARD_W + CARD_GAP_X);
                int top  = CONTENT_TOP + row * (CARD_H + CARD_GAP_Y);
                string vTag = string.Format("V{0:D3}", v);
                string name = "FPC_" + vTag;

                // Card Button
                var btn = sc.ScreenItems.Create<HmiButton>(name);
                btn.Left = SX(left); btn.Top = SY(top);
                btn.Width = (uint)SX(CARD_W); btn.Height = (uint)SY(CARD_H);
                btn.BackColor = BG_CARD; btn.BorderColor = BORDER; btn.BorderWidth = 1;
                btn.ForeColor = Color.White;
                SetMLText(btn, "Text", string.Format("VALVE V-{0:D3}", v));

                // Script to open popup on click
                AddPopupScript(btn, vTag);

                // BorderColor script dynamization
                try {
                    var borderDyn = btn.Dynamizations.Create<ScriptDynamization>("BorderColor");
                    borderDyn.ScriptCode = string.Format(
                        "function readTag(v) {{ return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }}\n" +
                        "let configured = readTag(Tags(\"{0}_Configured\").Read());\n" +
                        "let healthy = readTag(Tags(\"{0}_Healthy\").Read());\n" +
                        "let open = readTag(Tags(\"{0}_OpenFB\").Read());\n" +
                        "let closed = readTag(Tags(\"{0}_ClosedFB\").Read());\n" +
                        "let local = readTag(Tags(\"{0}_LocalMode\").Read());\n" +
                        "let flash = readTag(Tags(\"Valves_DB_Clock1Hz\").Read());\n\n" +
                        "if (!configured) return 0xFF353C4E;\n" +
                        "if (local) return 0xFFFF9F0A;\n" +
                        "if (!healthy || (open && closed)) return flash ? 0xFFFF0000 : 0xFF3A0000;\n" +
                        "if (open && !closed) return 0xFF32C785;\n" +
                        "if (!open && closed) return 0xFF353C4E;\n" +
                        "return 0xFF00A2FF;",
                        vTag
                    );
                    borderDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
                } catch (Exception ex) {
                    Console.WriteLine("  [DEBUG] Error adding BorderColor dynamization to " + name + ": " + ex.Message);
                }

                // Text script dynamization - SHORT text to fit 165px card width
                try {
                    var textDyn = btn.Dynamizations.Create<ScriptDynamization>("Text");
                    textDyn.ScriptCode = string.Format(
                        "function readTag(v) {{ return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }}\n" +
                        "let configured = readTag(Tags(\"{0}_Configured\").Read());\n" +
                        "let healthy = readTag(Tags(\"{0}_Healthy\").Read());\n" +
                        "let open = readTag(Tags(\"{0}_OpenFB\").Read());\n" +
                        "let closed = readTag(Tags(\"{0}_ClosedFB\").Read());\n" +
                        "let local = readTag(Tags(\"{0}_LocalMode\").Read());\n" +
                        "let flash = readTag(Tags(\"Valves_DB_Clock1Hz\").Read());\n\n" +
                        "let state = \"MOVING\";\n" +
                        "if (!configured) {{ state = \"UNCFGD\"; }}\n" +
                        "else if (local) {{ state = \"LOCAL\"; }}\n" +
                        "else if (!healthy || (open && closed)) {{ state = flash ? \"FAULT\" : \"FAULT\"; }}\n" +
                        "else if (open && !closed) {{ state = \"OPEN\"; }}\n" +
                        "else if (!open && closed) {{ state = \"CLOSED\"; }}\n\n" +
                        "return \"V-{1}\\n\" + state;",
                        vTag, string.Format("{0:D3}", v)
                    );
                    textDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
                } catch (Exception ex) {
                    Console.WriteLine("  [DEBUG] Error adding Text dynamization to " + name + ": " + ex.Message);
                }
            }

            Console.WriteLine("  Screen_1 (Overview) rebuilt successfully.");
        }

        static void BuildHeaderBar(HmiScreen sc, string title, bool isOverview)
        {
            MakeRect(sc, "Hdr_BG", 0, 0, SCREEN_W, HEADER_H, BG_HEADER, TEAL, 2);
            MakeRect(sc, "Hdr_Logo", 12, 10, 28, 28, TEAL, TEAL, 0);

            var titleIO = sc.ScreenItems.Create<HmiIOField>("Hdr_Title");
            titleIO.Left = SX(50); titleIO.Top = SY(12);
            titleIO.Width = (uint)SX(600); titleIO.Height = (uint)SY(24);
            titleIO.BackColor = BG_HEADER; titleIO.ForeColor = TEAL;
            titleIO.BorderColor = BG_HEADER; titleIO.BorderWidth = 0;
            SetPropEnum(titleIO, "IOFieldType", "Output");
            SetMLText(titleIO, "Text", title);

            var btnOv = sc.ScreenItems.Create<HmiButton>("Nav_Overview");
            btnOv.Left = SX(SCREEN_W - 240); btnOv.Top = SY(8);
            btnOv.Width = (uint)SX(110); btnOv.Height = (uint)SY(32);
            btnOv.BackColor = isOverview ? TEAL : BG_HEADER;
            btnOv.ForeColor = isOverview ? Color.Black : TEAL;
            btnOv.BorderColor = TEAL; btnOv.BorderWidth = 1;
            SetMLText(btnOv, "Text", "Overview");
            AddNavScript(btnOv, "Screen_Home");

            var btnAl = sc.ScreenItems.Create<HmiButton>("Nav_Alarms");
            btnAl.Left = SX(SCREEN_W - 122); btnAl.Top = SY(8);
            btnAl.Width = (uint)SX(110); btnAl.Height = (uint)SY(32);
            btnAl.BackColor = !isOverview ? TEAL : BG_HEADER;
            btnAl.ForeColor = !isOverview ? Color.Black : COLOR_FAIL;
            btnAl.BorderColor = COLOR_FAIL; btnAl.BorderWidth = 1;
            SetMLText(btnAl, "Text", "⚠ Alarms");
            AddNavScript(btnAl, "Screen_Alarms");
        }

        static void BuildSummaryBar(HmiScreen sc)
        {
            MakeRect(sc, "Sum_BG", 0, HEADER_H, SCREEN_W, SUMMARY_H, BG_SUMMARY, BORDER, 1);
            int tileW = 310;
            int tileH = 36;
            int tileX = 12;
            int tileY = HEADER_H + 4;

            var tiles = new string[]{ "OPEN VALVES", "CLOSED VALVES", "IN TRANSIT", "SYSTEM FAULTS", "LOCAL MODE", "UNCONFIGURED" };
            Color[] colors = { Color.FromArgb(255, 50, 199, 133), Color.FromArgb(255, 142, 142, 147), Color.FromArgb(255, 0, 162, 255), Color.FromArgb(255, 255, 69, 58), Color.FromArgb(255, 255, 159, 10), Color.FromArgb(255, 142, 142, 147) };
            string[] tags = { "Valves_DB_TotalOpen", "Valves_DB_TotalClosed", "Valves_DB_TotalTransit", "Valves_DB_TotalFault", "Valves_DB_TotalLocal", "" };

            for (int i = 0; i < tiles.Length; i++) {
                MakeRect(sc, "Sum_Tile_" + i, tileX, tileY, tileW, tileH, BG_HEADER, BORDER, 1);

                var dot = sc.ScreenItems.Create<HmiEllipse>("Sum_Dot_" + i);
                dot.CenterX = SX(tileX + 16); dot.CenterY = SY(tileY + 18);
                dot.RadiusX = (uint)SX(6); dot.RadiusY = (uint)SY(6);
                dot.BackColor = colors[i]; dot.BorderColor = colors[i];

                var cnt = sc.ScreenItems.Create<HmiIOField>("Sum_Cnt_" + i);
                cnt.Left = SX(tileX + 32); cnt.Top = SY(tileY + 9);
                cnt.Width = (uint)SX(50); cnt.Height = (uint)SY(18);
                cnt.BackColor = BG_HEADER; cnt.ForeColor = Color.White;
                cnt.BorderColor = BG_HEADER; cnt.BorderWidth = 0;
                SetPropEnum(cnt, "IOFieldType", "Output");
                
                if (!string.IsNullOrEmpty(tags[i])) {
                    try {
                        var dyn = cnt.Dynamizations.Create<TagDynamization>("ProcessValue");
                        dyn.Tag = tags[i];
                    } catch (Exception ex) {
                        Console.WriteLine("  [DEBUG] Error dynamizing Summary Cnt: " + ex);
                    }
                } else {
                    try {
                        var sd = cnt.Dynamizations.Create<ScriptDynamization>("ProcessValue");
                        sd.ScriptCode = 
                            "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                            "let o = readTag(Tags(\"Valves_DB_TotalOpen\").Read()) || 0;\n" +
                            "let c = readTag(Tags(\"Valves_DB_TotalClosed\").Read()) || 0;\n" +
                            "let t = readTag(Tags(\"Valves_DB_TotalTransit\").Read()) || 0;\n" +
                            "let f = readTag(Tags(\"Valves_DB_TotalFault\").Read()) || 0;\n" +
                            "let l = readTag(Tags(\"Valves_DB_TotalLocal\").Read()) || 0;\n" +
                            "return 88 - (o + c + t + f + l);";
                        sd.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
                    } catch (Exception ex) {
                        Console.WriteLine("  [DEBUG] Error scripting Summary Cnt: " + ex);
                    }
                }

                var lbl = sc.ScreenItems.Create<HmiButton>("Sum_Lbl_" + i);
                lbl.Left = SX(tileX + 85); lbl.Top = SY(tileY + 6);
                lbl.Width = (uint)SX(210); lbl.Height = (uint)SY(24);
                lbl.BackColor = BG_HEADER; lbl.ForeColor = colors[i];
                lbl.BorderColor = BG_HEADER; lbl.BorderWidth = 0;
                SetMLText(lbl, "Text", tiles[i]);

                tileX += tileW + 8;
            }
        }

        static void AddPopupCloseXScript(HmiButton btn)
        {
            try {
                PropertyInfo evProp = null;
                foreach (var p in btn.GetType().GetProperties())
                    if (p.Name == "EventHandlers") { evProp = p; if (p.DeclaringType == btn.GetType()) break; }
                if (evProp == null) return;
                object evObj = evProp.GetValue(btn, null);
                Type evEnum = null;
                foreach (var t in btn.GetType().Assembly.GetTypes())
                    if (t.Name == "HmiButtonEventType") { evEnum = t; break; }
                if (evEnum == null) return;
                object evVal = Enum.Parse(evEnum, "Tapped");
                var cm = evObj.GetType().GetMethod("Create", new Type[]{ evEnum });
                if (cm == null) return;
                object handler = cm.Invoke(evObj, new object[]{ evVal });
                if (handler == null) return;
                var sp = handler.GetType().GetProperty("Script");
                object script = sp.GetValue(handler, null);
                var scp = script.GetType().GetProperty("ScriptCode");
                if (scp == null || !scp.CanWrite) return;

                // Bracket notation bypasses compiler's static member check while working identically at runtime
                scp.SetValue(script, "HMIRuntime.UI.SysFct[\"CloseScreenInPopup\"](\"Popup_Valve\");", null);
            } catch {}
        }

        static void AddPopupActionButton(HmiButton btn, string action)
        {
            try {
                PropertyInfo evProp = null;
                foreach (var p in btn.GetType().GetProperties())
                    if (p.Name == "EventHandlers") { evProp = p; if (p.DeclaringType == btn.GetType()) break; }
                if (evProp == null) return;
                object evObj = evProp.GetValue(btn, null);
                Type evEnum = null;
                foreach (var t in btn.GetType().Assembly.GetTypes())
                    if (t.Name == "HmiButtonEventType") { evEnum = t; break; }
                if (evEnum == null) return;
                object evVal = Enum.Parse(evEnum, "Tapped");
                var cm = evObj.GetType().GetMethod("Create", new Type[]{ evEnum });
                if (cm == null) return;
                object handler = cm.Invoke(evObj, new object[]{ evVal });
                if (handler == null) return;
                var sp = handler.GetType().GetProperty("Script");
                object script = sp.GetValue(handler, null);
                var scp = script.GetType().GetProperty("ScriptCode");
                if (scp == null || !scp.CanWrite) return;

                string scriptBody = "";
                string helper = "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n";
                if (action == "OpenCmd") {
                    scriptBody =
                        helper +
                        "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                        "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                        "let cfg = readTag(Tags(vTag + \"_Configured\").Read());\n" +
                        "if (!cfg) return;\n" +
                        "Tags(vTag + \"_CloseCmd\").Write(false);\n" +
                        "Tags(vTag + \"_OpenCmd\").Write(true);\n" +
                        "HMIRuntime.Audit.SysFct.InsertElectronicRecord(vTag, \"ValveCommand\", \"Update\", false, true, \"None\");";
                } else if (action == "CloseCmd") {
                    scriptBody =
                        helper +
                        "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                        "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                        "let cfg = readTag(Tags(vTag + \"_Configured\").Read());\n" +
                        "if (!cfg) return;\n" +
                        "Tags(vTag + \"_OpenCmd\").Write(false);\n" +
                        "Tags(vTag + \"_CloseCmd\").Write(true);\n" +
                        "HMIRuntime.Audit.SysFct.InsertElectronicRecord(vTag, \"ValveCommand\", \"Update\", true, false, \"None\");";
                } else if (action == "ResetFault") {
                    // Just writing Healthy:=true isn't enough for a double-indication fault
                    // (OpenFB && ClosedFB both true) — FB_ValveLoop re-derives Healthy:=false from
                    // that same condition every scan, so the reset would be undone before the
                    // operator even saw it. Clearing both feedback bits actually resolves the root
                    // cause: the valve shows MOVING/unknown position until commanded again, which
                    // is the honest state after a sensor conflict.
                    scriptBody =
                        helper +
                        "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                        "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                        "Tags(vTag + \"_Healthy\").Write(true);\n" +
                        "Tags(vTag + \"_OpenFB\").Write(false);\n" +
                        "Tags(vTag + \"_ClosedFB\").Write(false);\n" +
                        "Tags(vTag + \"_TimeoutOpenAlarm\").Write(false);\n" +
                        "Tags(vTag + \"_TimeoutCloseAlarm\").Write(false);\n" +
                        "Tags(vTag + \"_UnexpMove\").Write(false);\n" +
                        "HMIRuntime.Audit.SysFct.InsertElectronicRecord(vTag, \"ValveCommand\", \"Update\", false, true, \"None\");";
                } else if (action == "ToggleService") {
                    scriptBody = 
                        helper +
                        "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                        "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                        "let cur = readTag(Tags(vTag + \"_Configured\").Read());\n" +
                        "let newVal = !cur;\n" +
                        "Tags(vTag + \"_Configured\").Write(newVal);\n" +
                        "if (newVal) {\n" +
                        "  Tags(vTag + \"_Healthy\").Write(true);\n" +
                        "}";
                } else if (action == "ToggleStuck") {
                    scriptBody = 
                        helper +
                        "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                        "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                        "let cur = readTag(Tags(vTag + \"_Stuck\").Read());\n" +
                        "Tags(vTag + \"_Stuck\").Write(!cur);";
                }

                scp.SetValue(script, scriptBody, null);
            } catch {}
        }

        static void AddPopupScript(HmiButton btn, string vTag)
        {
            try {
                PropertyInfo evProp = null;
                foreach (var p in btn.GetType().GetProperties())
                    if (p.Name == "EventHandlers") { evProp = p; if (p.DeclaringType == btn.GetType()) break; }
                if (evProp == null) { Console.WriteLine("  [PopupScript ERR] No EventHandlers property on " + btn.GetType().Name); return; }
                object evObj = evProp.GetValue(btn, null);
                object handler = CreateTappedHandler(evObj);
                if (handler == null) { Console.WriteLine("  [PopupScript ERR] Could not create Tapped handler for " + vTag); return; }
                var sp = handler.GetType().GetProperty("Script");
                object script = sp.GetValue(handler, null);
                var scp = script.GetType().GetProperty("ScriptCode");
                if (scp == null || !scp.CanWrite) return;

                int vIndex = int.Parse(vTag.Substring(1));
                string vNum = string.Format("{0:D3}", vIndex);
                // showHeader=false: we have a custom header with Btn_CloseX inside Screen_Popup.
                // The 730/360 position centers the popup on its parent screen — originally computed
                // for a 1920x1080 parent ((1920-460)/2=730, (1080-360)/2=360). Both the parent canvas
                // and the popup's own size now scale by SX()/SY() (1366x768 target), so the position
                // must scale the same way to stay centered instead of drifting toward one corner.
                string jsCode = string.Format(
                    "Tags(\"SelectedValve\").Write({0});\n" +
                    "HMIRuntime.UI.SysFct.OpenScreenInPopup(\"Popup_Valve\", \"Screen_Popup\", false, \" \", {1}, {2}, false);",
                    vIndex, SX(730), SY(360)
                );

                scp.SetValue(script, jsCode, null);
            } catch (Exception ex) {
                Console.WriteLine("  [DEBUG] Error in AddPopupScript: " + ex);
            }
        }

        static void AddMasterResetScript(HmiButton btn)
        {
            try {
                PropertyInfo evProp = null;
                foreach (var p in btn.GetType().GetProperties())
                    if (p.Name == "EventHandlers") { evProp = p; if (p.DeclaringType == btn.GetType()) break; }
                if (evProp == null) { Console.WriteLine("  [MasterReset ERR] No EventHandlers property on " + btn.GetType().Name); return; }
                object evObj = evProp.GetValue(btn, null);
                object handler = CreateTappedHandler(evObj);
                if (handler == null) { Console.WriteLine("  [MasterReset ERR] Could not create Tapped handler"); return; }
                var sp = handler.GetType().GetProperty("Script");
                object script = sp.GetValue(handler, null);
                var scp = script.GetType().GetProperty("ScriptCode");
                if (scp == null || !scp.CanWrite) return;

                string jsCode =
                    "for (let i = 1; i <= 88; i++) {\n" +
                    "  let tag = \"V\" + (\"000\" + i).slice(-3) + \"_Healthy\";\n" +
                    "  Tags(tag).Write(true);\n" +
                    "}\n";
                scp.SetValue(script, jsCode, null);
            } catch (Exception ex) {
                Console.WriteLine("  [DEBUG] Error in AddMasterResetScript: " + ex);
            }
        }

        static void CleanScreen(HmiScreen sc)
        {
            try {
                var del = new List<HmiScreenItemBase>();
                foreach (HmiScreenItemBase item in sc.ScreenItems) del.Add(item);
                foreach (var item in del) try { item.Delete(); } catch {}
            } catch {}
        }

        static HmiRectangle MakeRect(HmiScreen sc, string name, int left, int top, int w, int h, Color bg, Color border, int bw)
        {
            var r = sc.ScreenItems.Create<HmiRectangle>(name);
            r.Left = SX(left); r.Top = SY(top);
            r.Width = (uint)SX(w); r.Height = (uint)SY(h);
            r.BackColor = bg; r.BorderColor = border; r.BorderWidth = (byte)bw;
            return r;
        }

        static void SetMLText(object obj, string propName, string text)
        {
            try {
                var p = obj.GetType().GetProperty(propName);
                if (p == null) return;
                object ml = p.GetValue(obj, null);
                if (ml == null) return;

                string formattedText = text.Replace("\r", "").Replace("\n", "<br />");
                if (!formattedText.StartsWith("<body>")) {
                    formattedText = "<body><p>" + formattedText + "</p></body>";
                }

                var itemsProp = ml.GetType().GetProperty("Items");
                if (itemsProp != null) {
                    var items = itemsProp.GetValue(ml, null);
                    if (items != null) {
                        int count = (int)items.GetType().GetProperty("Count").GetValue(items, null);
                        if (count > 0) {
                            var iEnumerable = items as IEnumerable;
                            foreach (var it in iEnumerable) {
                                var tp = it.GetType().GetProperty("Text");
                                if (tp != null && tp.CanWrite) { tp.SetValue(it, formattedText, null); }
                            }
                        } else {
                            // Create text item if collection is empty
                            var createM = items.GetType().GetMethod("Create", new Type[] { typeof(string), typeof(string) });
                            if (createM != null) {
                                createM.Invoke(items, new object[] { "", formattedText });
                            }
                        }
                    }
                    return;
                }
                var directText = ml.GetType().GetProperty("Text");
                if (directText != null && directText.CanWrite) directText.SetValue(ml, formattedText, null);
            } catch (Exception ex) {
                Console.WriteLine("  [DEBUG] Error in SetMLText: " + ex);
            }
        }

        // Finds whatever enum type the item's own EventHandlers.Create() method actually takes,
        // instead of guessing a hardcoded type name (e.g. "HmiButtonEventType") that only exists
        // for some widget types — HmiEllipse's is HmiEllipseEventType, HmiRectangle's is
        // HmiRectangleEventType, etc. A wrong guess makes Create() lookup fail and the whole
        // method silently return with the click never wired — this is what that bug looked like.
        static object CreateTappedHandler(object evObj)
        {
            foreach (var m in evObj.GetType().GetMethods()) {
                if (m.Name != "Create") continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || !ps[0].ParameterType.IsEnum) continue;
                object evVal;
                try { evVal = Enum.Parse(ps[0].ParameterType, "Tapped"); }
                catch { continue; }
                return m.Invoke(evObj, new object[] { evVal });
            }
            return null;
        }

        static void AddNavScript(HmiButton btn, string targetScreen)
        {
            AddScriptEvent(btn, "HMIRuntime.UI.SysFct.ChangeScreen(\"" + targetScreen + "\", \"~\");");
        }

        static void AddScriptEvent(HmiButton btn, string scriptCode)
        {
            try {
                PropertyInfo evProp = null;
                foreach (var p in btn.GetType().GetProperties())
                    if (p.Name == "EventHandlers") { evProp = p; if (p.DeclaringType == btn.GetType()) break; }
                if (evProp == null) { Console.WriteLine("  [ScriptEvent ERR] No EventHandlers property on " + btn.GetType().Name); return; }
                object evObj = evProp.GetValue(btn, null);
                object handler = CreateTappedHandler(evObj);
                if (handler == null) { Console.WriteLine("  [ScriptEvent ERR] Could not create Tapped handler for " + btn.GetType().Name); return; }
                var sp = handler.GetType().GetProperty("Script");
                object script = sp.GetValue(handler, null);
                var scp = script.GetType().GetProperty("ScriptCode");
                if (scp != null && scp.CanWrite)
                    scp.SetValue(script, scriptCode, null);
            } catch (Exception ex) { Console.WriteLine("  [ScriptEvent ERR] " + ex.Message); }
        }

        static void SetPropEnum(object obj, string propName, string enumValueName)
        {
            try {
                var p = obj.GetType().GetProperty(propName);
                if (p == null || !p.CanWrite) return;
                object enumVal = Enum.Parse(p.PropertyType, enumValueName);
                p.SetValue(obj, enumVal, null);
            } catch {}
        }
        static void SetPropInt(object obj, string name, int val)
        { try { var p = obj.GetType().GetProperty(name); if (p != null && p.CanWrite) p.SetValue(obj, val, null); } catch {} }
        static void SetPropUInt(object obj, string name, uint val)
        { try { var p = obj.GetType().GetProperty(name); if (p != null && p.CanWrite) p.SetValue(obj, val, null); } catch {} }
        static void SetStr(object obj, string name, string val)
        { try { var p = obj.GetType().GetProperty(name); if (p != null && p.CanWrite) p.SetValue(obj, val, null); } catch {} }
        static string GetStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }

        static void ImportPlcBlocks(Project project)
        {
            try {
                Device plcDevice = null;
                PlcSoftware plc = null;
                
                // Robust search for any device containing PlcSoftware
                foreach (Device d in project.Devices) {
                    foreach (DeviceItem it in d.DeviceItems) {
                        var c = it.GetService<SoftwareContainer>();
                        if (c != null && c.Software is PlcSoftware) {
                            plcDevice = d;
                            plc = c.Software as PlcSoftware;
                            break;
                        }
                    }
                    if (plc != null) break;
                }
                
                if (plcDevice == null || plc == null) {
                    Console.WriteLine("  [PLC] PLC device or PlcSoftware not found in project.");
                    return;
                }
                Console.WriteLine("  [PLC] Found PLC device: " + plcDevice.Name);
                
                // Import Valves_DB
                try {
                    string dbPath = @"C:\Users\Admin\Documents\Automation\valveDemo2\temp_valves_db.xml";
                    Console.WriteLine("  [PLC] Importing Valves_DB from " + dbPath + "...");
                    var dbBlock = plc.BlockGroup.Blocks.Import(new FileInfo(dbPath), ImportOptions.Override);
                    if (dbBlock != null && dbBlock.Count > 0) 
                        Console.WriteLine("  [PLC] Import successful: " + dbBlock[0].Name);
                } catch (Exception ex) {
                    Console.WriteLine("  [PLC] (Skipping Valves_DB re-import - PLC is online or block exists)");
                }
                
                // Import FB_ValveLoop
                try {
                    string loopPath = @"C:\Users\Admin\Documents\Automation\valveDemo2\temp_fb_valveloop.xml";
                    Console.WriteLine("  [PLC] Importing FB_ValveLoop from " + loopPath + "...");
                    var loopBlock = plc.BlockGroup.Blocks.Import(new FileInfo(loopPath), ImportOptions.Override);
                    if (loopBlock != null && loopBlock.Count > 0)
                        Console.WriteLine("  [PLC] Import successful: " + loopBlock[0].Name);
                } catch (Exception ex) {
                    Console.WriteLine("  [PLC] (Skipping FB_ValveLoop re-import - PLC is online or block exists)");
                }

                // Import Valve_Meta_DB
                try {
                    string metaPath = @"C:\Users\Admin\Documents\Automation\valveDemo2\temp_valve_meta_db.xml";
                    Console.WriteLine("  [PLC] Importing Valve_Meta_DB from " + metaPath + "...");
                    var metaBlock = plc.BlockGroup.Blocks.Import(new FileInfo(metaPath), ImportOptions.Override);
                    if (metaBlock != null && metaBlock.Count > 0)
                        Console.WriteLine("  [PLC] Import successful: " + metaBlock[0].Name);
                } catch (Exception ex) {
                    Console.WriteLine("  [PLC] (Skipping Valve_Meta_DB re-import - PLC is online or block exists)");
                }
            } catch (Exception ex) {
                Console.WriteLine("  [PLC] Skipping PLC block import: " + ex.Message);
            }
        }

        static System.Collections.Generic.Dictionary<string, byte> _classPriorities;
        static System.Collections.Generic.Dictionary<string, string> _classAuditClasses;

        static void CreateAlarms(HmiSoftware hmi, string dbName = "Valves_DB")
        {
            Console.WriteLine("\n[STEP 3] Generating Discrete Alarms...");
            
            // Ensure required AlarmClasses exist, each with a distinct Priority so the PRIORITY
            // column actually means something (every alarm was showing Priority=0 before - the
            // classes were created but never assigned one). WinCC Unified's priority range is 0-16;
            // higher = more urgent (consistent with OPC UA's severity convention, which Unified
            // aligns with) - not exhaustively confirmed in Siemens' own docs, but this is the
            // best-supported reading; easy to flip later if sorting by Priority looks backwards
            // once alarms are visible.
            var classPriorities = new System.Collections.Generic.Dictionary<string, byte> {
                { "ValveFault",   14 }, // A/B - Unhealthy, Position Conflict: most severe
                { "System",       12 }, // hardware/infrastructure faults
                { "ValveWarning",  8 }, // C/D/E/F - timeouts, Loss of Position, Unexpected Movement
                { "ValveEvent",    3 }, // G - Local Mode: logged event, not a real fault
            };
            foreach (var kvp in classPriorities) {
                var cls = hmi.AlarmClasses.Find(kvp.Key);
                if (cls == null) { try { cls = hmi.AlarmClasses.Create(kvp.Key); } catch {} }
                if (cls != null) { try { cls.Priority = kvp.Value; } catch {} }
            }
            // Setting the class's Priority does NOT flow through to alarms already assigned to it -
            // confirmed live: after setting ValveFault.Priority = 14, V001_Unhealthy.Priority (its
            // own, separate property) still read 0. Each alarm instance carries its own Priority
            // that CreateDiscreteAlarm must set explicitly, matching its class's value.
            _classPriorities = classPriorities;

            // Audit Trail: 11 standard Alarm Audit Classes already exist once GMP mode is on
            // (confirmed live) - custom classes can't be created via Openness (HmiAlarmAuditClass.
            // Create() crashes TIA Portal outright, confirmed live, twice). Serious faults require a
            // comment on acknowledge so there's a record of why/what was done; lighter warnings and
            // events just need a plain logged acknowledge.
            _classAuditClasses = new System.Collections.Generic.Dictionary<string, string> {
                { "ValveFault",   "HMI_Audit_Alarm_Acknowledge_Comment" },
                { "System",       "HMI_Audit_Alarm_Acknowledge_Comment" },
                { "ValveWarning", "HMI_Audit_Alarm_Acknowledge" },
                { "ValveEvent",   "HMI_Audit_Alarm_Acknowledge" },
            };

            // Generate 9 System Alarms (HwWord bits 0..8)
            CreateDiscreteAlarm(hmi, "System_PLC_CPU_Fault", "System", "PLC CPU fault detected.", dbName + "_HwWord", 0, "PLC", "SYSTEM");
            CreateDiscreteAlarm(hmi, "System_Aft_RIO_Fault", "System", "Aft Ballast RIO station failure.", dbName + "_HwWord", 1, "RIO", "SYSTEM");
            CreateDiscreteAlarm(hmi, "System_Bilge_RIO_Fault", "System", "Bilge/ER RIO station failure.", dbName + "_HwWord", 2, "RIO", "SYSTEM");
            CreateDiscreteAlarm(hmi, "System_Fwd_RIO_Fault", "System", "Fwd Ballast RIO station failure.", dbName + "_HwWord", 3, "RIO", "SYSTEM");
            CreateDiscreteAlarm(hmi, "System_IO_Module_Fault", "System", "I/O Module fault detected.", dbName + "_HwWord", 4, "I/O", "SYSTEM");
            CreateDiscreteAlarm(hmi, "System_Power_UPS_Fault", "System", "Power/UPS fault detected.", dbName + "_HwWord", 5, "POWER", "SYSTEM");
            CreateDiscreteAlarm(hmi, "System_Network_Loss", "System", "Network loss detected.", dbName + "_HwWord", 6, "NETWORK", "SYSTEM");
            CreateDiscreteAlarm(hmi, "System_Heartbeat_Fault", "System", "HMI Heartbeat loss detected.", dbName + "_HwWord", 7, "HMI", "SYSTEM");
            CreateDiscreteAlarm(hmi, "System_General_Fault", "System", "System fault detected.", dbName + "_HwWord", 8, "SYSTEM", "SYSTEM");

            for (int i = 1; i <= 88; i++) {
                string vId = string.Format("V{0:D3}", i);
                // Position-based zone boundaries: AFT 1-28, BILGE/ER 29-56, FWD 57-88.
                string zoneArea = (i <= 28) ? "BALLAST AFT" : (i <= 56) ? "BILGE-ER" : "BALLAST FWD";

                // Pass 1: High priority alarms
                CreateDiscreteAlarm(hmi, vId + "_Unhealthy", "ValveFault", vId + " reported Unhealthy status.", dbName + "_W_Unhealthy_" + ((i-1)/16), (i-1)%16, vId, zoneArea);
                CreateDiscreteAlarm(hmi, vId + "_Conflict", "ValveFault", vId + " Command Conflict (Open and Close requested).", dbName + "_W_Conflict_" + ((i-1)/16), (i-1)%16, vId, zoneArea);
                CreateDiscreteAlarm(hmi, vId + "_FailOpen", "ValveWarning", vId + " Failed to Open in automatic mode.", dbName + "_W_FailOpen_" + ((i-1)/16), (i-1)%16, vId, zoneArea);
                CreateDiscreteAlarm(hmi, vId + "_FailClose", "ValveWarning", vId + " Failed to Close in automatic mode.", dbName + "_W_FailClose_" + ((i-1)/16), (i-1)%16, vId, zoneArea);

                // Pass 2: E (Loss of Position, MED), F (Unexpected Movement, MED), G (Local Mode, LOW).
                // Reuse ValveWarning for E/F (same tier as C/D's timeouts) and the new ValveEvent
                // class for G, since Local Mode is a logged event rather than a real fault.
                CreateDiscreteAlarm(hmi, vId + "_LossPos", "ValveWarning", vId + " Loss of Position Feedback (idle, no limit switch made).", dbName + "_W_LossPos_" + ((i-1)/16), (i-1)%16, vId, zoneArea);
                CreateDiscreteAlarm(hmi, vId + "_UnexpMove", "ValveWarning", vId + " Unexpected Movement detected (uncommanded limit switch change).", dbName + "_W_UnexpMove_" + ((i-1)/16), (i-1)%16, vId, zoneArea);
                CreateDiscreteAlarm(hmi, vId + "_Local", "ValveEvent", vId + " switched to Local Control.", dbName + "_W_Local_" + ((i-1)/16), (i-1)%16, vId, zoneArea);
            }
            Console.WriteLine("  Created " + (88 * 7 + 9) + " discrete alarms.");
        }

        static void CreateDiscreteAlarm(HmiSoftware hmi, string name, string className, string text, string triggerTag, int triggerBit, string origin, string area)
        {
            try {
                var al = hmi.DiscreteAlarms.Find(name);
                if (al == null) al = hmi.DiscreteAlarms.Create(name);
                al.AlarmClass = className;
                byte pri;
                if (_classPriorities != null && _classPriorities.TryGetValue(className, out pri)) {
                    try { al.Priority = pri; } catch {}
                }
                string auditClass;
                if (_classAuditClasses != null && _classAuditClasses.TryGetValue(className, out auditClass)) {
                    try { al.AuditClass = auditClass; } catch {}
                }
                // The real property is EventText, NOT AlarmText - confirmed live via reflection
                // (dumping an existing alarm's full property list showed no AlarmText property at
                // all; SetMLText(al, "AlarmText", ...) was silently no-oping every single call).
                SetMLText(al, "EventText", text);
                al.Origin = origin;
                al.Area = area;
                al.RaisedStateTag = triggerTag;
                al.RaisedStateTagBitNumber = (uint)triggerBit;

                Console.WriteLine("    -> Created " + name + " | Tag: " + triggerTag + " | Bit: " + triggerBit);
            } catch (Exception ex) {
                Console.WriteLine("  [ERROR] Alarm " + name + ": " + ex.Message);
            }
        }

        static void CreateSummaryHmiTags(HmiSoftware hmi, bool forceRefreshNewTags = false)
        {
            Console.WriteLine("\n[STEP 2] Checking and creating HMI tags for all 88 valves...");
            // SelectedValve is an INTERNAL HMI tag - no PLC address, just holds the selected index
            CreateInternalTag(hmi, "SelectedValve", "Int");
            // BilgePage tracks which page (0 or 1) of the Bilge valve table is currently shown —
            // internal only, no PLC binding, same pattern as SelectedValve.
            CreateInternalTag(hmi, "BilgePage", "Int");
            CreateInternalTag(hmi, "Internal_PrevFaultCount", "Int");
            CreateSummaryTag(hmi, "Valves_DB_TotalOpen",   "Valves_DB.TotalOpen",   "Int");
            CreateSummaryTag(hmi, "Valves_DB_TotalClosed", "Valves_DB.TotalClosed", "Int");
            CreateSummaryTag(hmi, "Valves_DB_TotalTransit","Valves_DB.TotalTransit","Int");
            CreateSummaryTag(hmi, "Valves_DB_TotalFault",  "Valves_DB.TotalFault",  "Int");
            CreateSummaryTag(hmi, "Valves_DB_TotalLocal",  "Valves_DB.TotalLocal",  "Int");
            // forceRefreshNewTags: these tags (plus the zone sub-totals and Name/Location below)
            // were originally created in the same run that imported the PLC blocks but before
            // the PLC was ever compiled, so their address binding never resolved and is stuck
            // broken ("PLC tag is invalid" in the HMI compiler) no matter how many times the PLC
            // is compiled afterward. Re-setting the address now that the PLC is properly
            // compiled fixes it. The 7 pre-existing tags above never had this problem, so they
            // stay on the normal skip-if-exists path.
            CreateSummaryTag(hmi, "Valves_DB_TotalConfigured", "Valves_DB.TotalConfigured", "Int", forceRefreshNewTags);
            CreateSummaryTag(hmi, "Valves_DB_Clock1Hz",    "Valves_DB.Clock_1Hz",   "Bool");

            // Per-zone sub-totals — FB_ValveLoop computes these in the same 1..88 pass that
            // already builds the plant-wide totals above, so each KPI/caption cell on
            // Screen_Home can read one tag instead of looping its zone's valves itself.
            string[] zonePfx = { "Er", "Fwd", "Aft" };
            string[] statSuf = { "Open", "Closed", "Transit", "Fault", "Local", "Configured" };
            foreach (var zp in zonePfx)
                foreach (var st in statSuf)
                    CreateSummaryTag(hmi, "Valves_DB_" + zp + st, "Valves_DB." + zp + st, "Int", forceRefreshNewTags);

            // Add the word arrays for discrete alarms
            string[] conditions = { "Unhealthy", "Conflict", "FailOpen", "FailClose", "LossPos", "UnexpMove", "Local" };
            for (int w = 0; w < 6; w++) {
                foreach (var cond in conditions) {
                    CreateSummaryTag(hmi, "Valves_DB_W_" + cond + "_" + w, "Valves_DB.W_" + cond + "[" + w + "]", "UInt");
                }
            }

            Console.WriteLine("  Creating HMI tags (Configured, OpenCmd, CloseCmd, OpenFB, ClosedFB, Healthy, LocalMode) for 88 valves...");
            for (int i = 1; i <= VALVE_COUNT; i++) {
                string vTag = string.Format("V{0:D3}", i);
                string plcPrefix = string.Format("Valves_DB.Valve[{0}]", i);
                CreateSummaryTag(hmi, vTag + "_Configured", plcPrefix + ".Configured", "Bool");
                CreateSummaryTag(hmi, vTag + "_OpenCmd",    plcPrefix + ".OpenCmd",    "Bool");
                CreateSummaryTag(hmi, vTag + "_CloseCmd",   plcPrefix + ".CloseCmd",   "Bool");
                CreateSummaryTag(hmi, vTag + "_OpenFB",     plcPrefix + ".OpenFB",     "Bool");
                CreateSummaryTag(hmi, vTag + "_ClosedFB",   plcPrefix + ".ClosedFB",   "Bool");
                CreateSummaryTag(hmi, vTag + "_Healthy",    plcPrefix + ".Healthy",    "Bool");
                CreateSummaryTag(hmi, vTag + "_LocalMode",  plcPrefix + ".LocalMode",  "Bool");
                CreateSummaryTag(hmi, vTag + "_Stuck",             "Valves_DB.Stuck[" + i + "]",             "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_TimeoutOpenAlarm",  "Valves_DB.TimeoutOpenAlarm[" + i + "]",  "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_TimeoutCloseAlarm", "Valves_DB.TimeoutCloseAlarm[" + i + "]", "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_UnexpMove",         "Valves_DB.UnexpMove[" + i + "]",         "Bool", forceRefreshNewTags);
                // Manually-maintained reference data (Valve_Meta_DB) — not written by any
                // script; an engineer fills these in by hand. Built for all 88 now so every
                // future zone screen (not just Bilge/ER) can reuse them without another pass.
                CreateSummaryTag(hmi, vTag + "_Name",     "Valve_Meta_DB.Name[" + i + "]",     "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_Location", "Valve_Meta_DB.Location[" + i + "]", "String", forceRefreshNewTags);
            }

            // Configuration screen's global (all-88) paged window - same naming convention as the
            // Aft/Er/Fwd_Tbl* tags the zone tables already use (Aft_TblNo_1..14 etc., confirmed live
            // in the project's ValveTags table - 1563 tags total).
            CreateSummaryTag(hmi, "Valves_DB_CfgPage", "Valves_DB.CfgPage", "Int", forceRefreshNewTags);
            for (int slot = 1; slot <= 14; slot++) {
                CreateSummaryTag(hmi, "Cfg_TblNo_" + slot,   "Valves_DB.CfgTblNo[" + slot + "]",   "Int",    forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblTag_" + slot,  "Valves_DB.CfgTblTag[" + slot + "]",  "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblZone_" + slot, "Valves_DB.CfgTblZone[" + slot + "]", "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblConfigured_" + slot, "Valves_DB.CfgTblConfigured[" + slot + "]", "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblName_" + slot, "Valve_Meta_DB.CfgTblName[" + slot + "]", "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblLoc_" + slot,  "Valve_Meta_DB.CfgTblLoc[" + slot + "]",  "String", forceRefreshNewTags);
            }
        }

        // Creates an HMI tag that IS connected to a PLC tag
        static void CreateSummaryTag(HmiSoftware hmi, string tagName, string plcAddress, string dataType = "Int", bool forceRefresh = false)
        {
            try {
                var table = hmi.TagTables.Find("ValveTags");
                if (table == null) { table = hmi.TagTables.Create("ValveTags"); Console.WriteLine("  Created tag table: ValveTags"); }

                var tag = table.Tags.Find(tagName);
                // Existing tags never need their address/connection/type touched again —
                // re-setting all three on all 616 valve tags every run was costing
                // ~1800 redundant Openness round-trips (the dominant cost per call,
                // since there's no bulk-write API) on every single rebuild.
                // forceRefresh bypasses this: tags created while the PLC block existed but
                // wasn't yet compiled get their address bound to nothing and stay broken
                // forever — re-setting the address after the PLC is properly compiled fixes it.
                if (tag != null && !forceRefresh) return;
                if (tag == null) tag = table.Tags.Create(tagName, "ValveTags");

                // Try all known property names for the PLC address field
                bool addressSet = false;
                foreach (var propName in new string[]{ "LogicalAddress", "PlcTag", "Address", "TagAddress" }) {
                    try {
                        var pp = tag.GetType().GetProperty(propName);
                        if (pp != null && pp.CanWrite) { pp.SetValue(tag, plcAddress, null); addressSet = true; break; }
                    } catch {}
                }
                if (!addressSet) Console.WriteLine("  [WARN] Could not set address for " + tagName);

                SetStr(tag, "Connection", HMI_CONNECTION);
                SetStr(tag, "PlcName", "PLC_1");

                // Set DataType by trying known enum values
                try {
                    var dtProp = tag.GetType().GetProperty("DataType");
                    if (dtProp != null && dtProp.CanWrite) {
                        // Try setting via string first, then via enum
                        try { dtProp.SetValue(tag, dataType, null); } catch {
                            var dtType = dtProp.PropertyType;
                            if (dtType.IsEnum) { try { dtProp.SetValue(tag, Enum.Parse(dtType, dataType, true), null); } catch {} }
                        }
                    }
                } catch {}
            } catch (Exception ex) {
                Console.WriteLine("  [ERROR] Error creating tag " + tagName + ": " + ex.Message);
            }
        }

        // Creates an INTERNAL HMI tag - no PLC connection (just stores values locally on HMI)
        static void CreateInternalTag(HmiSoftware hmi, string tagName, string dataType = "Int")
        {
            try {
                var table = hmi.TagTables.Find("ValveTags");
                if (table == null) { table = hmi.TagTables.Create("ValveTags"); }
                if (table.Tags.Find(tagName) != null) return; // already exists, nothing to do
                var tag = table.Tags.Create(tagName, "ValveTags");
                // Do NOT set Connection or address - internal tag has no PLC binding
                try {
                    var dtProp = tag.GetType().GetProperty("DataType");
                    if (dtProp != null && dtProp.CanWrite) { try { dtProp.SetValue(tag, dataType, null); } catch {} }
                } catch {}
                Console.WriteLine("  Created internal HMI tag: " + tagName);
            } catch (Exception ex) {
                Console.WriteLine("  [ERROR] Error creating internal tag " + tagName + ": " + ex.Message);
            }
        }

        // Small, targeted, low-risk fixup: (1) replace Screen_Login's still-unbuilt placeholder
        // content with the real BuildLoginScreen content, (2) set Authorization="Operate" on the
        // 84 per-slot table OPEN/CLOSE buttons across the 3 zone screens (Tr_Open_*/Tr_Close_*),
        // which bypass the popup entirely and so need the same protection. Deliberately does NOT
        // touch anything else — no screen deletion/recreation beyond Screen_Login's stale
        // placeholder items, no Alarms/column work. Added after the popup's 6 buttons already got
        // their Authorization set directly in the normal screen-build path.
        static void RunFinishLoginAuth()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
            TiaPortal portal = null; Project project = null;
            foreach (var p in procs) {
                try {
                    var att = p.Attach();
                    if (att != null && att.Projects.Count > 0) { portal = att; project = att.Projects[0]; break; }
                } catch (Exception ex) { Console.WriteLine("  [DEBUG] Attach() threw: " + ex.GetType().Name + ": " + ex.Message); }
            }
            if (portal == null || project == null) { Console.WriteLine("[ERROR] Could not attach to active TIA Portal project."); return; }
            Console.WriteLine("Attached to Project: " + project.Name);

            Device hmiDevice = FindDeviceByPartialName(project, "HMI");
            if (hmiDevice == null) { Console.WriteLine("[ERROR] HMI device not found."); return; }
            HmiSoftware hmi = FindHmiSoftware(hmiDevice);
            if (hmi == null) { Console.WriteLine("[ERROR] HMI software not found."); return; }

            var scLogin = FindScreen(hmi, "Screen_Login");
            if (scLogin == null) { Console.WriteLine("[ERROR] Screen_Login not found."); }
            else if (scLogin.ScreenItems.Any(i => i.Name == "Btn_Login")) {
                Console.WriteLine("  Screen_Login already has real content — skipping rebuild.");
            } else {
                foreach (var item in scLogin.ScreenItems) itemsToDelete.Add(item);
                foreach (var item in itemsToDelete) {
                    try { item.Delete(); } catch (Exception ex) { Console.WriteLine("  [WARN] could not delete " + item.Name + ": " + Root(ex)); }
                }
                itemsToDelete.Clear();
                try {
                    BuildLoginScreen(scLogin);
                    Console.WriteLine("  Screen_Login rebuilt with real LOGIN/LOGOUT content.");
                } catch (Exception ex) { Console.WriteLine("  [ERROR] BuildLoginScreen failed: " + Root(ex)); }
            }

            // Btn_AckAll lives on Screen_Alarms, which is deliberately excluded from every normal
            // rebuild (--only never includes "Alarms") to protect the manually-configured Alarm
            // Control columns. That means its Authorization was set in source but never actually
            // applied live — fix it directly here without touching anything else on that screen.
            var scAlarmsFix = FindScreen(hmi, "Screen_Alarms");
            if (scAlarmsFix == null) { Console.WriteLine("  [WARN] Screen_Alarms not found — cannot fix Btn_AckAll."); }
            else {
                var ackBtn = scAlarmsFix.ScreenItems.FirstOrDefault(i => i.Name == "Btn_AckAll");
                if (ackBtn == null) Console.WriteLine("  [WARN] Btn_AckAll not found on Screen_Alarms.");
                else { SetStr(ackBtn, "Authorization", "Operate"); Console.WriteLine("  Screen_Alarms Btn_AckAll: Authorization set to Operate."); }
            }

            string[] zoneScreens = { "Screen_Bilge", "Screen_AftBallast", "Screen_FwdBallast" };
            int totalSet = 0;
            foreach (var scName in zoneScreens) {
                var sc = FindScreen(hmi, scName);
                if (sc == null) { Console.WriteLine("  [SKIP] " + scName + " not found"); continue; }
                int count = 0;
                foreach (var item in sc.ScreenItems) {
                    if (item.Name.StartsWith("Tr_Open", StringComparison.OrdinalIgnoreCase) ||
                        item.Name.StartsWith("Tr_Close", StringComparison.OrdinalIgnoreCase)) {
                        SetStr(item, "Authorization", "Operate");
                        count++;
                    }
                }
                Console.WriteLine("  " + scName + ": set Authorization on " + count + " buttons");
                totalSet += count;
            }
            Console.WriteLine("\nTotal table buttons updated: " + totalSet + " (expected 84)");

            try { project.Save(); Console.WriteLine("\n[SAVE] Project saved."); }
            catch (Exception ex) { Console.WriteLine("\n[SAVE ERR] " + ex.Message); }
        }
        static List<HmiScreenItemBase> itemsToDelete = new List<HmiScreenItemBase>();

        static HmiScreen FindScreen(HmiSoftware hmi, string name)
        {
            foreach (HmiScreen s in hmi.Screens) if (s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return s;
            try {
                var pFolders = hmi.GetType().GetProperty("Folders");
                if (pFolders != null) {
                    var folders = pFolders.GetValue(hmi, null) as IEnumerable;
                    if (folders != null) return FindScreenInFolders(folders, name);
                }
            } catch {}
            return null;
        }

        static HmiScreen FindScreenInFolders(System.Collections.IEnumerable folders, string name)
        {
            foreach (dynamic f in folders) {
                foreach (HmiScreen s in f.Screens) if (s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return s;
                var res = FindScreenInFolders(f.Folders, name);
                if (res != null) return res;
            }
            return null;
        }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }

        static Device FindDeviceByPartialName(Project project, string partial)
        {
            foreach (var d in project.Devices) if (d.Name.IndexOf(partial, StringComparison.OrdinalIgnoreCase) >= 0) return d;
            foreach (var g in project.DeviceGroups) { var d = FindInGroup(g, partial); if (d != null) return d; }
            return null;
        }
        static Device FindInGroup(DeviceGroup g, string partial)
        {
            foreach (var d in g.Devices) if (d.Name.IndexOf(partial, StringComparison.OrdinalIgnoreCase) >= 0) return d;
            var ug = g as DeviceUserGroup;
            if (ug != null) foreach (var sub in ug.Groups) { var d = FindInGroup(sub, partial); if (d != null) return d; }
            return null;
        }
    }
}
