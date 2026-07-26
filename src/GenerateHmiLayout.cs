using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Drawing;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using Siemens.Engineering.HmiUnified.UI.Shapes;
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
    class Program
    {
        private const int    VALVE_COUNT    = 88;
        private const string FACEPLATE_TYPE = "Valve_Faceplate_V_0_0_4";
        private const string HMI_CONNECTION = "HMI_Connection_1";

        private const int SCREEN_W    = 1920;
        private const int SCREEN_H    = 1080;
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
            try { Run(); }
            catch (Exception ex) { Console.WriteLine("\n[ERROR] " + ex); }
            Console.WriteLine("\nPress Enter to exit..."); try { Console.ReadLine(); } catch {}
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
            Console.WriteLine("Searching for active TIA Portal project...");
            TiaPortal portal = null;
            Project project = null;
            foreach (var p in procs) {
                try {
                    var att = p.Attach();
                    if (att != null && att.Projects.Count > 0) {
                        portal = att;
                        project = att.Projects[0];
                        break;
                    }
                } catch {}
            }
            if (portal == null || project == null) {
                Console.WriteLine("[ERROR] Could not attach to active TIA Portal project.");
                return;
            }
            Console.WriteLine("Attached to Project: " + project.Name);

            // Import updated PLC blocks (FB_ValveLoop with Configured headroom logic)
            ImportPlcBlocks(project);

            Device hmiDevice = FindDeviceByPartialName(project, "HMI");
            if (hmiDevice == null) { Console.WriteLine("[ERROR] HMI device not found."); return; }
            HmiSoftware hmi = FindHmiSoftware(hmiDevice);
            if (hmi == null) { Console.WriteLine("[ERROR] HMI software not found."); return; }

            // Create HMI Summary Tags with valid PLC Tag references
            CreateSummaryHmiTags(hmi);

            // STEP 1 – Rebuild screens
            Console.WriteLine("\n[STEP 1] Rebuilding screens for 1920x1080 resolution...");
            EnsureAlarmScreen(hmi);
            EnsurePopupScreen(hmi);

            HmiScreen overview = RecreateScreen(hmi, "Screen_1");
            if (overview == null) { Console.WriteLine("[ERROR] Could not create Screen_1."); return; }
            BuildOverviewScreen(overview);

            Console.WriteLine("\n=== Complete! ===");
            Console.WriteLine("Screens: Screen_1 (Overview 1920x1080), Screen_Popup (600x500), Screen_Alarms");
        }

        static HmiScreen RecreateScreen(HmiSoftware hmi, string screenName)
        {
            HmiScreen existing = FindScreen(hmi, screenName);
            if (existing != null) {
                Console.WriteLine("  Deleting existing " + screenName + "...");
                try {
                    CleanScreen(existing);
                    existing.Delete();
                } catch {
                    CleanScreen(existing);
                    return existing;
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
                SetPropUInt(newScreen, "Width",  (uint)SCREEN_W);
                SetPropUInt(newScreen, "Height", (uint)SCREEN_H);
            }
            return newScreen;
        }

        static void EnsureAlarmScreen(HmiSoftware hmi)
        {
            HmiScreen sc = RecreateScreen(hmi, "Screen_Alarms");
            if (sc != null) BuildAlarmScreen(sc);
        }

        static void EnsurePopupScreen(HmiSoftware hmi)
        {
            HmiScreen sc = RecreateScreen(hmi, "Screen_Popup");
            if (sc == null) return;
            SetPropUInt(sc, "Width", 600);
            SetPropUInt(sc, "Height", 500);
            sc.BackColor = BG_DARK;

            // Outer canvas
            MakeRect(sc, "Pop_BG", 0, 0, 600, 500, BG_DARK, BORDER, 2);

            // ─── HEADER (Y=0..48): Dark bar + Centered Valve Name ─────────
            MakeRect(sc, "Pop_Header", 0, 0, 600, 48, BG_HEADER, BORDER, 1);

            // Valve name (centered horizontally across 600px width)
            var titleIO = sc.ScreenItems.Create<HmiIOField>("Pop_Title");
            titleIO.Left = 0; titleIO.Top = 10; titleIO.Width = 600; titleIO.Height = 28;
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

            // ─── STATUS CARD (Y=55..108): Text only, NO small circle ─────────────
            MakeRect(sc, "Pop_StatusCard", 15, 55, 570, 53, BG_CARD, BORDER, 1);

            // Reads SelectedValve dynamically and reads corresponding Vxxx PLC tags every 1s (Cyclic)
            var statusIO = sc.ScreenItems.Create<HmiIOField>("Pop_StatusText");
            statusIO.Left = 20; statusIO.Top = 60; statusIO.Width = 560; statusIO.Height = 43;
            statusIO.BackColor = BG_CARD; statusIO.ForeColor = Color.White;
            statusIO.BorderColor = BG_CARD; statusIO.BorderWidth = 0;
            SetPropEnum(statusIO, "IOFieldType", "Output");
            SetMLText(statusIO, "Text", "V-001  |  INITIALIZING  |  N/A  |  AUTO");
            try {
                var sDyn = statusIO.Dynamizations.Create<ScriptDynamization>("ProcessValue");
                sDyn.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                    "let vNum = (\"000\" + (idx || 1)).slice(-3);\n" +
                    "let vTag = \"V\" + vNum;\n" +
                    "let configured = readTag(Tags(vTag + \"_Configured\").Read());\n" +
                    "let healthy    = readTag(Tags(vTag + \"_Healthy\").Read());\n" +
                    "let open       = readTag(Tags(vTag + \"_OpenFB\").Read());\n" +
                    "let closed     = readTag(Tags(vTag + \"_ClosedFB\").Read());\n" +
                    "let local      = readTag(Tags(vTag + \"_LocalMode\").Read());\n\n" +
                    "let st = \"MOVING\";\n" +
                    "if (!configured) st = \"UNCONFIGURED\";\n" +
                    "else if (local) st = \"LOCAL MODE\";\n" +
                    "else if (!healthy || (open && closed)) st = \"FAULT\";\n" +
                    "else if (open && !closed) st = \"OPEN\";\n" +
                    "else if (!open && closed) st = \"CLOSED\";\n\n" +
                    "let hl = (!configured) ? \"N/A\" : (healthy ? \"HEALTHY\" : \"FAULT\");\n" +
                    "let md = local ? \"LOCAL\" : \"AUTO\";\n" +
                    "return \"V-\" + vNum + \"  |  \" + st + \"  |  \" + hl + \"  |  MODE: \" + md;";
                sDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "Cyclic");
                try { var cp = sDyn.Trigger.GetType().GetProperty("CyclicTime"); if (cp != null) cp.SetValue(sDyn.Trigger, 1000, null); } catch {}
            } catch {}

            // ─── OPEN / CLOSE Buttons (Y=118..183) ─────────────────────────────────
            var btnOpen = sc.ScreenItems.Create<HmiButton>("Btn_Open");
            btnOpen.Left = 20; btnOpen.Top = 118; btnOpen.Width = 270; btnOpen.Height = 62;
            btnOpen.BackColor = Color.FromArgb(255, 16, 185, 129); btnOpen.ForeColor = Color.White;
            btnOpen.BorderColor = Color.FromArgb(255, 52, 211, 153); btnOpen.BorderWidth = 2;
            SetMLText(btnOpen, "Text", "▲ OPEN VALVE");
            AddPopupActionButton(btnOpen, "OpenCmd");

            var btnClose = sc.ScreenItems.Create<HmiButton>("Btn_Close");
            btnClose.Left = 310; btnClose.Top = 118; btnClose.Width = 270; btnClose.Height = 62;
            btnClose.BackColor = Color.FromArgb(255, 55, 65, 81); btnClose.ForeColor = Color.White;
            btnClose.BorderColor = Color.FromArgb(255, 107, 114, 128); btnClose.BorderWidth = 2;
            SetMLText(btnClose, "Text", "▼ CLOSE VALVE");
            AddPopupActionButton(btnClose, "CloseCmd");

            // ─── Large Status Circle — centered between OPEN/CLOSE (ends Y=180) and RESET (starts Y=410) ──
            var dot = sc.ScreenItems.Create<HmiEllipse>("Pop_Dot");
            dot.CenterX = 300; dot.CenterY = 295; dot.RadiusX = 70; dot.RadiusY = 70;
            dot.BackColor = TEAL; dot.BorderColor = Color.White;
            try {
                var dotDyn = dot.Dynamizations.Create<ScriptDynamization>("BackColor");
                dotDyn.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                    "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                    "let configured = readTag(Tags(vTag + \"_Configured\").Read());\n" +
                    "let healthy    = readTag(Tags(vTag + \"_Healthy\").Read());\n" +
                    "let open       = readTag(Tags(vTag + \"_OpenFB\").Read());\n" +
                    "let closed     = readTag(Tags(vTag + \"_ClosedFB\").Read());\n" +
                    "let local      = readTag(Tags(vTag + \"_LocalMode\").Read());\n" +
                    "let flash      = readTag(Tags(\"Valves_DB_Clock1Hz\").Read());\n\n" +
                    "if (!configured) return 0xFF8E8E93;\n" +
                    "if (local) return 0xFFFF9F0A;\n" +
                    "if (!healthy || (open && closed)) return flash ? 0xFFFF0000 : 0xFF3A0000;\n" +
                    "if (open && !closed) return 0xFF32C785;\n" +
                    "if (!open && closed) return 0xFF4B5563;\n" +
                    "return 0xFF00A2FF;\n";
                dotDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "Cyclic");
                try { var cp = dotDyn.Trigger.GetType().GetProperty("CyclicTime"); if (cp != null) cp.SetValue(dotDyn.Trigger, 1000, null); } catch {}
            } catch {}

            // State label below big circle (Y=375..405)
            var stateLabel = sc.ScreenItems.Create<HmiIOField>("Pop_StateLabel");
            stateLabel.Left = 100; stateLabel.Top = 373; stateLabel.Width = 400; stateLabel.Height = 28;
            stateLabel.BackColor = BG_DARK; stateLabel.ForeColor = Color.White;
            stateLabel.BorderColor = BG_DARK; stateLabel.BorderWidth = 0;
            SetPropEnum(stateLabel, "IOFieldType", "Output");
            SetMLText(stateLabel, "Text", "STATE: INITIALIZING");
            try {
                var slDyn = stateLabel.Dynamizations.Create<ScriptDynamization>("ProcessValue");
                slDyn.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                    "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                    "let configured = readTag(Tags(vTag + \"_Configured\").Read());\n" +
                    "let healthy    = readTag(Tags(vTag + \"_Healthy\").Read());\n" +
                    "let open       = readTag(Tags(vTag + \"_OpenFB\").Read());\n" +
                    "let closed     = readTag(Tags(vTag + \"_ClosedFB\").Read());\n" +
                    "let local      = readTag(Tags(vTag + \"_LocalMode\").Read());\n\n" +
                    "if (!configured) return \"⬤  UNCONFIGURED\";\n" +
                    "if (local) return \"⬤  LOCAL MODE\";\n" +
                    "if (!healthy || (open && closed)) return \"⬤  FAULT\";\n" +
                    "if (open && !closed) return \"⬤  FULLY OPEN\";\n" +
                    "if (!open && closed) return \"⬤  FULLY CLOSED\";\n" +
                    "return \"⬤  MOVING\";\n";
                slDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "Cyclic");
                try { var cp = slDyn.Trigger.GetType().GetProperty("CyclicTime"); if (cp != null) cp.SetValue(slDyn.Trigger, 1000, null); } catch {}
            } catch {}

            // ─── RESET FAULT Button (Y=412..462) ─────────────────────────────────
            var btnReset = sc.ScreenItems.Create<HmiButton>("Btn_Reset");
            btnReset.Left = 150; btnReset.Top = 412; btnReset.Width = 300; btnReset.Height = 52;
            btnReset.BackColor = Color.FromArgb(255, 194, 65, 12); btnReset.ForeColor = Color.White;
            btnReset.BorderColor = Color.FromArgb(255, 249, 115, 22); btnReset.BorderWidth = 2;
            SetMLText(btnReset, "Text", "⚡ RESET FAULT");
            AddPopupActionButton(btnReset, "ResetFault");

            Console.WriteLine("  Screen_Popup built: Pop_* literal tags for live status, bracket-notation close button.");
        }

        static void BuildAlarmScreen(HmiScreen sc)
        {
            MakeRect(sc, "Al_BG", 0, 0, SCREEN_W, SCREEN_H, BG_DARK, BG_DARK, 0);
            BuildHeaderBar(sc, "Alarm Summary — Valve System", false);
            Console.WriteLine("  Screen_Alarms built.");
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
                btn.Left = left; btn.Top = top;
                btn.Width = (uint)CARD_W; btn.Height = (uint)CARD_H;
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
            titleIO.Left = 50; titleIO.Top = 12;
            titleIO.Width = 600; titleIO.Height = 24;
            titleIO.BackColor = BG_HEADER; titleIO.ForeColor = TEAL;
            titleIO.BorderColor = BG_HEADER; titleIO.BorderWidth = 0;
            SetPropEnum(titleIO, "IOFieldType", "Output");
            SetMLText(titleIO, "Text", title);

            var btnOv = sc.ScreenItems.Create<HmiButton>("Nav_Overview");
            btnOv.Left = SCREEN_W - 240; btnOv.Top = 8;
            btnOv.Width = 110; btnOv.Height = 32;
            btnOv.BackColor = isOverview ? TEAL : BG_HEADER;
            btnOv.ForeColor = isOverview ? Color.Black : TEAL;
            btnOv.BorderColor = TEAL; btnOv.BorderWidth = 1;
            SetMLText(btnOv, "Text", "Overview");
            AddNavScript(btnOv, "Screen_1");

            var btnAl = sc.ScreenItems.Create<HmiButton>("Nav_Alarms");
            btnAl.Left = SCREEN_W - 122; btnAl.Top = 8;
            btnAl.Width = 110; btnAl.Height = 32;
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
                dot.CenterX = tileX + 16; dot.CenterY = tileY + 18;
                dot.RadiusX = 6; dot.RadiusY = 6;
                dot.BackColor = colors[i]; dot.BorderColor = colors[i];

                var cnt = sc.ScreenItems.Create<HmiIOField>("Sum_Cnt_" + i);
                cnt.Left = tileX + 32; cnt.Top = tileY + 9;
                cnt.Width = 50; cnt.Height = 18;
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
                    SetMLText(cnt, "Text", "0");
                }

                var lbl = sc.ScreenItems.Create<HmiButton>("Sum_Lbl_" + i);
                lbl.Left = tileX + 85; lbl.Top = tileY + 6;
                lbl.Width = 210; lbl.Height = 24;
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
                        "Tags(vTag + \"_OpenCmd\").Write(true);";
                } else if (action == "CloseCmd") {
                    scriptBody = 
                        helper +
                        "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                        "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                        "Tags(vTag + \"_CloseCmd\").Write(true);";
                } else if (action == "ResetFault") {
                    scriptBody = 
                        helper +
                        "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                        "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                        "Tags(vTag + \"_Healthy\").Write(true);";
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

                int vIndex = int.Parse(vTag.Substring(1));
                string vNum = string.Format("{0:D3}", vIndex);
                // Write Pop_* tags with LITERAL valve tag names so compiler subscribes them.
                // Pop_* are then read by the popup dynamizations as literal names → status works.
                // showHeader=false: we have a custom header with Btn_CloseX inside Screen_Popup.
                string jsCode = string.Format(
                    "function readTag(v) {{ return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }}\n" +
                    "Tags(\"SelectedValve\").Write({0});\n" +
                    "Tags(\"Pop_Configured\").Write(readTag(Tags(\"{1}_Configured\").Read()));\n" +
                    "Tags(\"Pop_OpenFB\").Write(readTag(Tags(\"{1}_OpenFB\").Read()));\n" +
                    "Tags(\"Pop_ClosedFB\").Write(readTag(Tags(\"{1}_ClosedFB\").Read()));\n" +
                    "Tags(\"Pop_Healthy\").Write(readTag(Tags(\"{1}_Healthy\").Read()));\n" +
                    "Tags(\"Pop_LocalMode\").Write(readTag(Tags(\"{1}_LocalMode\").Read()));\n" +
                    "HMIRuntime.UI.SysFct.OpenScreenInPopup(\"Popup_Valve\", \"Screen_Popup\", \"\", 200, 150, false, false);",
                    vIndex, vTag
                );

                scp.SetValue(script, jsCode, null);
            } catch (Exception ex) {
                Console.WriteLine("  [DEBUG] Error in AddPopupScript: " + ex);
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
            r.Left = left; r.Top = top;
            r.Width = (uint)w; r.Height = (uint)h;
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

        static void AddNavScript(HmiButton btn, string targetScreen)
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
                if (scp != null && scp.CanWrite)
                    scp.SetValue(script, "HMIRuntime.UI.SysFct.ChangeScreen(\"" + targetScreen + "\", \"~\");", null);
            } catch {}
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
            } catch (Exception ex) {
                Console.WriteLine("  [PLC] Skipping PLC block import: " + ex.Message);
            }
        }

        static void CreateSummaryHmiTags(HmiSoftware hmi)
        {
            Console.WriteLine("\n[STEP 2] Checking and creating HMI tags for all 88 valves...");
            // SelectedValve is an INTERNAL HMI tag - no PLC address, just holds the selected index
            CreateInternalTag(hmi, "SelectedValve", "Int");
            // Popup display intermediate tags - written by card click, read by popup as LITERAL names
            CreateInternalTag(hmi, "Pop_Configured",  "Bool");
            CreateInternalTag(hmi, "Pop_OpenFB",       "Bool");
            CreateInternalTag(hmi, "Pop_ClosedFB",     "Bool");
            CreateInternalTag(hmi, "Pop_Healthy",      "Bool");
            CreateInternalTag(hmi, "Pop_LocalMode",    "Bool");
            CreateSummaryTag(hmi, "Valves_DB_TotalOpen",   "Valves_DB.TotalOpen",   "Int");
            CreateSummaryTag(hmi, "Valves_DB_TotalClosed", "Valves_DB.TotalClosed", "Int");
            CreateSummaryTag(hmi, "Valves_DB_TotalTransit","Valves_DB.TotalTransit","Int");
            CreateSummaryTag(hmi, "Valves_DB_TotalFault",  "Valves_DB.TotalFault",  "Int");
            CreateSummaryTag(hmi, "Valves_DB_TotalLocal",  "Valves_DB.TotalLocal",  "Int");
            CreateSummaryTag(hmi, "Valves_DB_Clock1Hz",    "Valves_DB.Clock_1Hz",   "Bool");

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
            }
        }

        // Creates an HMI tag that IS connected to a PLC tag
        static void CreateSummaryTag(HmiSoftware hmi, string tagName, string plcAddress, string dataType = "Int")
        {
            try {
                var table = hmi.TagTables.Find("ValveTags");
                if (table == null) { table = hmi.TagTables.Create("ValveTags"); Console.WriteLine("  Created tag table: ValveTags"); }
                
                var tag = table.Tags.Find(tagName);
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
                var tag = table.Tags.Find(tagName);
                if (tag == null) tag = table.Tags.Create(tagName, "ValveTags");
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

        static HmiScreen FindScreen(HmiSoftware hmi, string name)
        { foreach (HmiScreen s in hmi.Screens) if (s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return s; return null; }

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
