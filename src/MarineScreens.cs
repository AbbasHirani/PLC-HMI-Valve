using System;
using System.Drawing;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.Base;

namespace ValveDemoHmiBuilder
{
    partial class Program
    {
        static readonly Color BG_LIGHT = Color.FromArgb(255, 220, 220, 220); // Box background
        static readonly Color HDR_GRAY = Color.FromArgb(255, 200, 200, 200); // Inner headers
        static readonly Color BORDER_DARK = Color.FromArgb(255, 80, 80, 80);
        static readonly Color TEXT_DARK = Color.FromArgb(255, 30, 30, 30);
        static readonly Color COLOR_WARN = Color.FromArgb(255, 220, 220, 0);

        static void SetPropEnum(HmiScreenItemBase item, string propName, string enumValueStr)
        {
            try {
                var p = item.GetType().GetProperty(propName);
                if (p != null) {
                    var val = Enum.Parse(p.PropertyType, enumValueStr);
                    p.SetValue(item, val, null);
                }
            } catch {}
        }

        static void SetMLText(HmiScreenItemBase item, string propName, string text)
        {
            try {
                var mlProp = item.GetType().GetProperty(propName);
                var mlObj = mlProp.GetValue(item, null);
                var defProp = mlObj.GetType().GetProperty("DefaultText");
                defProp.SetValue(mlObj, text, null);
            } catch {}
        }

        static void DrawStaticText(HmiScreen sc, string name, string text, int x, int y, int w, int h, Color bg, Color fg, string align = "Left")
        {
            var tb = sc.ScreenItems.Create<HmiTextBox>(name);
            tb.Left = x; tb.Top = y; tb.Width = (uint)w; tb.Height = (uint)h;
            tb.BackColor = bg; tb.ForeColor = fg; tb.BorderWidth = 0;
            
            // Force alignment via Openness Property
            try { 
                object enumVal = Enum.Parse(tb.GetType().Assembly.GetType("Siemens.Engineering.HmiUnified.UI.Widgets.HmiHorizontalAlignment"), align);
                tb.SetAttribute("HorizontalTextAlignment", enumVal);
            } catch {}
            try { SetPropEnum(tb, "HorizontalTextAlignment", align); } catch {}
            try { SetPropEnum(tb, "TextHorizontalAlignment", align); } catch {}
            
            SetMLText(tb, "Text", text);
        }



        static void BuildMarineHeader(HmiScreen sc, string activePage)
        {
            MakeRect(sc, "Hdr_BG", 0, 0, SCREEN_W, 160, BG_LIGHT, BORDER_DARK, 1);
            
            // Huge bold Title
            DrawStaticText(sc, "Hdr_Title1", "MV WESTERLY - VALVE REMOTE CONTROL SYSTEM", 30, 20, 1000, 35, Color.Transparent, TEXT_DARK);
            DrawStaticText(sc, "Hdr_Title2", "Bilge and Ballast System", 30, 60, 1000, 25, Color.Transparent, TEXT_DARK);

            // Top Right Info Box
            int rX = 1600;
            DrawStaticText(sc, "Hdr_Date", "\uD83D\uDCC5 DATE: 21/06/2026", rX, 20, 300, 24, Color.Transparent, TEXT_DARK);
            DrawStaticText(sc, "Hdr_Time", "\uD83D\uDD53 TIME: 14:32:18", rX, 50, 300, 24, Color.Transparent, TEXT_DARK);
            DrawStaticText(sc, "Hdr_User", "\uD83D\uDC64 USER: ENGINEER", rX, 80, 300, 24, Color.Transparent, TEXT_DARK);

            // Clean Horizontal Navigation Bar
            string[] navLabels = { "\u2302 HOME", "\uD83D\uDCA7 BILGE / ER", "\uD83D\uDEA2 BALLAST FWD", "\uD83D\uDEA2 BALLAST AFT", "\uD83D\uDD14 ALARMS", "\uD83D\uDCC8 DIAGNOSTICS", "\uD83D\uDC64 LOGIN" };
            string[] navScreens = { "Screen_Home", "Screen_Bilge", "Screen_FwdBallast", "Screen_AftBallast", "Screen_Alarms", "", "" };
            
            int btnW = 180;
            int btnH = 45;
            int startX = 30;

            for (int i = 0; i < navLabels.Length; i++)
            {
                var btn = sc.ScreenItems.Create<HmiButton>("Nav_" + i);
                btn.Left = startX + (i * (btnW + 20)); 
                btn.Top = 100;
                btn.Width = (uint)btnW; btn.Height = (uint)btnH;
                
                bool isActive = (navScreens[i] == activePage) && (i < 5);
                btn.BackColor = isActive ? Color.Black : BG_DARK;
                btn.ForeColor = isActive ? Color.White : TEXT_DARK;
                btn.BorderColor = TEXT_DARK; btn.BorderWidth = 2;
                
                SetMLText(btn, "Text", navLabels[i]);
                if (navScreens[i] != "") AddNavScript(btn, navScreens[i]);
            }
        }

        static void DrawValve(HmiScreen sc, string name, int x, int y, int vNum)
        {
            // Draw the \u22C8 (Bowtie) symbol with a transparent background
            DrawStaticText(sc, name + "_Sym", "\u22C8", x - 20, y - 12, 40, 24, Color.Transparent, TEXT_DARK, "Center");
            
            // Draw the dynamic status circle in the exact center
            var dot = sc.ScreenItems.Create<HmiEllipse>(name + "_Dot");
            dot.CenterX = x; dot.CenterY = y; dot.RadiusX = 7u; dot.RadiusY = 7u;
            dot.BackColor = Color.Gray; dot.BorderColor = BORDER_DARK; dot.BorderWidth = 1;

            // Dynamize the circle color based on V001, V002, etc.
            string vTag = string.Format("V{0:D3}", vNum);
            try {
                var dyn = dot.Dynamizations.Create<ScriptDynamization>("BackColor");
                dyn.ScriptCode = 
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let cfg = readTag(Tags(\"" + vTag + "_Configured\").Read());\n" +
                    "if (!cfg) return 0xFF808080;\n" +
                    "let healthy = readTag(Tags(\"" + vTag + "_Healthy\").Read());\n" +
                    "let open = readTag(Tags(\"" + vTag + "_OpenFB\").Read());\n" +
                    "let closed = readTag(Tags(\"" + vTag + "_ClosedFB\").Read());\n" +
                    "if (!healthy || (open && closed)) return 0xFFFF0000;\n" +
                    "if (open && !closed) return 0xFF00C800;\n" +
                    "if (!open && closed) return 0xFFFF0000;\n" +
                    "return 0xFFDCDC00;";
                dyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}
        }

        static void BuildScreenHome(HmiScreen sc)
        {
            Console.WriteLine("  Building 1920x1080 Marine Home Screen (Fresh Start)...");
            MakeRect(sc, "Home_BG", 0, 0, SCREEN_W, SCREEN_H, BG_DARK, BG_DARK, 0);
            BuildMarineHeader(sc, "Screen_Home");

            // LEFT SIDE: BOAT MIMIC
            int bX = 30; int bY = 180;
            MakeRect(sc, "Home_BoatBG", bX, bY, 950, 850, BG_LIGHT, BORDER_DARK, 2);
            
            int hullX = bX + 50; int hullY = bY + 50; 
            int hullW = 700; int hullH = 750;
            MakeRect(sc, "Hull_Body", hullX, hullY, hullW, hullH, BG_LIGHT, BORDER_DARK, 2);
            
            var bow = sc.ScreenItems.Create<HmiEllipse>("Hull_Bow");
            bow.CenterX = hullX + hullW; bow.CenterY = hullY + (hullH / 2);
            bow.RadiusX = 120u; bow.RadiusY = (uint)(hullH / 2);
            bow.BackColor = BG_LIGHT; bow.BorderColor = BORDER_DARK; bow.BorderWidth = 2;

            int zoneW = hullW / 3;
            
            // Draw dotted dividers (simulated with 5 small vertical rectangles per line)
            for (int i=0; i<5; i++) {
                MakeRect(sc, "Div1_" + i, hullX + zoneW, hullY + 20 + (i * 150), 2, 80, BORDER_DARK, BORDER_DARK, 0);
                MakeRect(sc, "Div2_" + i, hullX + (zoneW * 2), hullY + 20 + (i * 150), 2, 80, BORDER_DARK, BORDER_DARK, 0);
            }

            // Zone Labels
            DrawStaticText(sc, "Lbl_ER", "ER\n(BILGE)", hullX + 10, hullY + (hullH/2), 120, 50, Color.Transparent, TEXT_DARK, "Center");
            DrawStaticText(sc, "Lbl_FWD", "FORWARD\nBALLAST", hullX + zoneW + 10, hullY + (hullH/2), 160, 50, Color.Transparent, TEXT_DARK, "Center");
            DrawStaticText(sc, "Lbl_AFT", "AFT", hullX + (zoneW*2) + 40, hullY + (hullH/2), 80, 50, Color.Transparent, TEXT_DARK, "Center");

            MakeRect(sc, "Engine_Box", hullX + 30, hullY + 60, 100, 80, BG_LIGHT, TEXT_DARK, 2);
            DrawStaticText(sc, "Engine_Lbl", "ENGINE", hullX + 30, hullY + 85, 100, 24, Color.Transparent, TEXT_DARK, "Center");

            // PIPING & VALVES
            int pTop = hullY + 200;
            int pBot = hullY + 550;
            
            // Horizontal main pipes connecting the zones
            MakeRect(sc, "Pipe_Top", hullX + 30, pTop, hullW - 60, 4, TEXT_DARK, TEXT_DARK, 0);
            MakeRect(sc, "Pipe_Bot", hullX + 30, pBot, hullW - 60, 4, TEXT_DARK, TEXT_DARK, 0);

            int[] vNums = { 1, 2, 3, 4, 30, 31, 32, 33, 60, 61, 62, 63 }; // Example representative valves
            int vIdx = 0;

            for(int z = 0; z < 3; z++) {
                int zCenterX = hullX + (zoneW * z) + (zoneW / 2);
                // Vertical crossover loop for this zone
                MakeRect(sc, "Pipe_Cross_" + z, zCenterX, pTop, 4, (pBot - pTop), TEXT_DARK, TEXT_DARK, 0);

                // 4 Valves per zone
                DrawValve(sc, "Vlv_"+z+"_1", zCenterX - 60, pTop, vNums[vIdx++]);
                DrawValve(sc, "Vlv_"+z+"_2", zCenterX + 60, pTop, vNums[vIdx++]);
                DrawValve(sc, "Vlv_"+z+"_3", zCenterX - 60, pBot, vNums[vIdx++]);
                DrawValve(sc, "Vlv_"+z+"_4", zCenterX + 60, pBot, vNums[vIdx++]);
            }

            // RIGHT SIDE: KPI BOXES
            int kpiX = 1010;
            BuildZoneKpiBox(sc, "BILGE VALVES", 1, 28, kpiX, 180);
            BuildZoneKpiBox(sc, "FORWARD BALLAST VALVES", 29, 60, kpiX, 420);
            BuildZoneKpiBox(sc, "AFT BALLAST VALVES", 61, 88, kpiX, 660);

            BuildSystemStatusBox(sc, kpiX, 900);
        }

        static void BuildZoneKpiBox(HmiScreen sc, string title, int start, int end, int x, int y)
        {
            int w = 880; int h = 220;
            MakeRect(sc, "KPI_BG_" + title.Replace(" ", ""), x, y, w, h, BG_LIGHT, BORDER_DARK, 2);
            MakeRect(sc, "KPI_TitleBG_" + title.Replace(" ", ""), x, y, w, 40, HDR_GRAY, BORDER_DARK, 1);
            DrawStaticText(sc, "KPI_TitleLbl_" + title.Replace(" ", ""), "\u26F1 " + title, x + 20, y + 8, w - 40, 24, Color.Transparent, TEXT_DARK, "Left");

            string[] labels = { "TOTAL VALVES", "OPEN VALVES", "CLOSED VALVES", "MOVING VALVES", "FAULTS" };
            
            for (int i = 0; i < 5; i++)
            {
                int rowY = y + 50 + (i * 32);
                DrawStaticText(sc, "KPI_Lbl_" + title.Replace(" ", "") + "_" + i, labels[i], x + 90, rowY, 300, 24, Color.Transparent, TEXT_DARK, "Left");

                var val = sc.ScreenItems.Create<HmiTextBox>("KPI_Val_" + title.Replace(" ", "") + "_" + i);
                val.Left = x + w - 160; val.Top = rowY; val.Width = 100; val.Height = 24;
                val.BackColor = Color.Transparent; val.ForeColor = TEXT_DARK; val.BorderWidth = 0;
                try { val.SetAttribute("HorizontalTextAlignment", Enum.Parse(val.GetType().Assembly.GetType("Siemens.Engineering.HmiUnified.UI.Widgets.HmiHorizontalAlignment"), "Right")); } catch {}
                try { SetPropEnum(val, "HorizontalTextAlignment", "Right"); } catch {}
                SetMLText(val, "Text", "0");

                // Dynamic logic (same as before)
                try {
                    var dyn = val.Dynamizations.Create<ScriptDynamization>("ProcessValue");
                    string calcLogic = "";
                    if (i == 0) calcLogic = "count++;";
                    else if (i == 1) calcLogic = "if (open && !closed) count++;";
                    else if (i == 2) calcLogic = "if (!open && closed) count++;";
                    else if (i == 3) calcLogic = "if (!open && !closed) count++;";
                    else if (i == 4) calcLogic = "if (!healthy || (open && closed)) count++;";

                    dyn.ScriptCode = 
                        "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                        "let count = 0;\n" +
                        "for (let i = " + start + "; i <= " + end + "; i++) {\n" +
                        "  let vTag = \"V\" + (\"000\" + i).slice(-3);\n" +
                        "  let cfg = readTag(Tags(vTag + \"_Configured\").Read());\n" +
                        "  if (!cfg) continue;\n" +
                        "  let healthy = readTag(Tags(vTag + \"_Healthy\").Read());\n" +
                        "  let open = readTag(Tags(vTag + \"_OpenFB\").Read());\n" +
                        "  let closed = readTag(Tags(vTag + \"_ClosedFB\").Read());\n" +
                        "  " + calcLogic + "\n" +
                        "}\n" +
                        "return count;";
                    dyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "T1s");
                } catch {}
            }

            // Draw exact geometric icons next to labels
            MakeRect(sc, "KPI_Icn0_" + title.Replace(" ", ""), x + 40, y + 50 + (0*32) + 6, 12, 12, TEXT_DARK, TEXT_DARK, 1);
            
            var ic1 = sc.ScreenItems.Create<HmiEllipse>("KPI_Icn1_" + title.Replace(" ", ""));
            ic1.CenterX = x + 46; ic1.CenterY = y + 50 + (1*32) + 12; ic1.RadiusX = 6u; ic1.RadiusY = 6u; ic1.BackColor = COLOR_OK; ic1.BorderColor = COLOR_OK;
            
            var ic2 = sc.ScreenItems.Create<HmiEllipse>("KPI_Icn2_" + title.Replace(" ", ""));
            ic2.CenterX = x + 46; ic2.CenterY = y + 50 + (2*32) + 12; ic2.RadiusX = 6u; ic2.RadiusY = 6u; ic2.BackColor = Color.Transparent; ic2.BorderColor = COLOR_FAIL; ic2.BorderWidth = 2;

            var ic3 = sc.ScreenItems.Create<HmiEllipse>("KPI_Icn3_" + title.Replace(" ", ""));
            ic3.CenterX = x + 46; ic3.CenterY = y + 50 + (3*32) + 12; ic3.RadiusX = 6u; ic3.RadiusY = 6u; ic3.BackColor = COLOR_WARN; ic3.BorderColor = COLOR_WARN;

            DrawStaticText(sc, "KPI_Icn4_" + title.Replace(" ", ""), "\u26A0", x + 35, y + 50 + (4*32) - 2, 24, 24, Color.Transparent, COLOR_FAIL, "Center");
        }

        static void BuildSystemStatusBox(HmiScreen sc, int x, int y)
        {
            MakeRect(sc, "Sys_BG", x, y, 880, 120, BG_LIGHT, BORDER_DARK, 2);
            MakeRect(sc, "Sys_TitleBG", x, y, 880, 30, HDR_GRAY, BORDER_DARK, 1);
            DrawStaticText(sc, "Sys_Title", "SYSTEM STATUS", x, y + 4, 880, 20, Color.Transparent, TEXT_DARK, "Center");

            string[] lbls = { "PLC", "AFT RIO", "ER RIO", "UPS", "FWD RIO", "NETWORK" };
            for(int i=0; i<6; i++) {
                int col = i % 3;
                int row = i / 3;
                int dotX = x + 40 + (col * 200);
                int dotY = y + 50 + (row * 35);
                
                var dot = sc.ScreenItems.Create<HmiEllipse>("SysDot_" + i);
                dot.CenterX = dotX; dot.CenterY = dotY + 12; dot.RadiusX = 6u; dot.RadiusY = 6u;
                dot.BackColor = COLOR_OK; dot.BorderColor = COLOR_OK;
                
                DrawStaticText(sc, "SysLbl_" + i, lbls[i] + "    HEALTHY", dotX + 20, dotY, 150, 24, Color.Transparent, TEXT_DARK, "Left");
            }

            MakeRect(sc, "Sys_AlmBG", x + 650, y + 40, 200, 60, BG_DARK, BORDER_DARK, 1);
            DrawStaticText(sc, "Sys_AlmLbl", "ACTIVE ALARMS", x + 660, y + 60, 120, 24, Color.Transparent, TEXT_DARK, "Left");
            DrawStaticText(sc, "Sys_AlmVal", "0", x + 790, y + 60, 50, 24, Color.Transparent, TEXT_DARK, "Right");
        }
    }
}
