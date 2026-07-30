using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
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
        // ── Marine Screen Colors ────────────────────────────────────────
        static readonly Color M_BG       = Color.FromArgb(255, 176, 181, 190); // Main background steel
        static readonly Color M_BOX      = Color.FromArgb(255, 240, 242, 245); // Panel background
        static readonly Color M_HDR      = Color.FromArgb(255,  38,  50,  66); // Panel title strip (navy)
        static readonly Color M_HDRTXT   = Color.FromArgb(255, 245, 247, 250); // Text on navy
        static readonly Color M_BORDER   = Color.FromArgb(255,  46,  56,  70); // Dark border
        static readonly Color M_LINE     = Color.FromArgb(255, 198, 203, 212); // Hairline separator
        static readonly Color M_TEXT     = Color.FromArgb(255,  22,  28,  38); // Text
        static readonly Color M_MUTED    = Color.FromArgb(255,  96, 106, 122); // Secondary text
        static readonly Color M_GREEN    = Color.FromArgb(255,   0, 158,  74); // Open/OK
        static readonly Color M_RED      = Color.FromArgb(255, 205,  32,  38); // Closed/Fail
        static readonly Color M_YELLOW   = Color.FromArgb(255, 226, 168,   0); // Local mode / Warn
        static readonly Color M_BLUE     = Color.FromArgb(255,   0, 162, 255); // In transit / Moving (#00A2FF per spec)
        static readonly Color M_ACCENT   = Color.FromArgb(255,   0, 116, 186); // Selection / active nav
        static readonly Color M_TRANS    = Color.FromArgb(0,   0,   0,   0);   // Transparent

        // ── SetMLText (proven version from GenerateHmiLayout.cs) ────────
        static void SetText(object obj, string propName, string text)
        {
            try {
                var p = obj.GetType().GetProperty(propName);
                if (p == null) return;
                object ml = p.GetValue(obj, null);
                if (ml == null) return;

                string fmt = "<body><p>" + text + "</p></body>";

                var itemsProp = ml.GetType().GetProperty("Items");
                if (itemsProp != null) {
                    var items = itemsProp.GetValue(ml, null);
                    if (items != null) {
                        int count = (int)items.GetType().GetProperty("Count").GetValue(items, null);
                        if (count > 0) {
                            foreach (var it in (IEnumerable)items) {
                                var tp = it.GetType().GetProperty("Text");
                                if (tp != null && tp.CanWrite) tp.SetValue(it, fmt, null);
                            }
                        } else {
                            var createM = items.GetType().GetMethod("Create", new Type[]{ typeof(string), typeof(string) });
                            if (createM != null) createM.Invoke(items, new object[]{ "", fmt });
                        }
                    }
                    return;
                }
                var dt = ml.GetType().GetProperty("Text");
                if (dt != null && dt.CanWrite) dt.SetValue(ml, fmt, null);
            } catch (Exception ex) {
                Console.WriteLine("  [SetText ERR] " + ex.Message);
            }
        }

        static void SetProp(object obj, string name, string enumVal)
        {
            try {
                var p = obj.GetType().GetProperty(name);
                if (p == null || !p.CanWrite) return;
                p.SetValue(obj, Enum.Parse(p.PropertyType, enumVal), null);
            } catch {}
        }

        static void SetFont(object obj, int sizePx, bool bold)
        {
            try {
                var fp = obj.GetType().GetProperty("Font");
                if (fp == null) return;
                object font = fp.GetValue(obj, null);
                if (font == null) return;

                var sizeProp = font.GetType().GetProperty("Size");
                if (sizeProp != null && sizeProp.CanWrite) {
                    if (sizeProp.PropertyType == typeof(string))
                        sizeProp.SetValue(font, sizePx + "px", null);
                    else
                        sizeProp.SetValue(font, Convert.ChangeType(sizePx, sizeProp.PropertyType), null);
                }

                var weightProp = font.GetType().GetProperty("Weight");
                if (weightProp != null && weightProp.CanWrite) {
                    if (weightProp.PropertyType.IsEnum)
                        weightProp.SetValue(font, Enum.Parse(weightProp.PropertyType, bold ? "Bold" : "Normal"), null);
                    else if (weightProp.PropertyType == typeof(string))
                        weightProp.SetValue(font, bold ? "Bold" : "Normal", null);
                }
            } catch {}
        }

        static HmiButton MakeBtn(HmiScreen sc, string name, int x, int y, int w, int h,
                                  string label, Color bg, Color fg, Color border, int bw,
                                  int fontSize = 14, bool bold = false)
        {
            var b = sc.ScreenItems.Create<HmiButton>(name);
            b.Left = x; b.Top = y; b.Width = (uint)w; b.Height = (uint)h;
            b.BackColor = bg; b.ForeColor = fg; b.BorderColor = border; b.BorderWidth = (byte)bw;
            SetFont(b, fontSize, bold);
            SetText(b, "Text", label);
            return b;
        }

        static HmiTextBox MakeTb(HmiScreen sc, string name, int x, int y, int w, int h,
                                   string label, Color bg, Color fg, int bw = 0, string align = "Center",
                                   int fontSize = 14, bool bold = false)
        {
            var tb = sc.ScreenItems.Create<HmiTextBox>(name);
            tb.Left = x; tb.Top = y; tb.Width = (uint)w; tb.Height = (uint)h;
            tb.BackColor = bg; tb.ForeColor = fg; tb.BorderWidth = (byte)bw;
            SetProp(tb, "HorizontalTextAlignment", align);
            SetProp(tb, "VerticalTextAlignment", "Middle");
            SetFont(tb, fontSize, bold);
            SetText(tb, "Text", label);
            return tb;
        }

        // Flat, non-interactive-looking button — the only widget whose "Text" property
        // reliably accepts a ScriptDynamization, so it backs every live numeric readout.
        static HmiButton MakeLiveText(HmiScreen sc, string name, int x, int y, int w, int h,
                                       Color fg, string align, int fontSize, bool bold)
        {
            var b = sc.ScreenItems.Create<HmiButton>(name);
            b.Left = x; b.Top = y; b.Width = (uint)w; b.Height = (uint)h;
            b.BackColor = M_TRANS; b.ForeColor = fg;
            b.BorderColor = M_TRANS; b.BorderWidth = 0;
            SetProp(b, "HorizontalTextAlignment", align);
            SetFont(b, fontSize, bold);
            SetText(b, "Text", "--");
            return b;
        }

        static void Dyn(object item, string prop, string js, string trigger)
        {
            try {
                var dp = item.GetType().GetProperty("Dynamizations");
                if (dp == null) return;
                object dyns = dp.GetValue(item, null);
                if (dyns == null) return;

                MethodInfo create = null;
                foreach (var m in dyns.GetType().GetMethods()) {
                    if (m.Name != "Create" || !m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(string)) { create = m; break; }
                }
                if (create == null) return;

                object d = create.MakeGenericMethod(typeof(ScriptDynamization)).Invoke(dyns, new object[] { prop });
                var sd = (ScriptDynamization)d;
                sd.ScriptCode = js;
                sd.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), trigger);
            } catch (Exception ex) {
                Console.WriteLine("  [Dyn ERR ." + prop + "] " + ex.Message);
            }
        }

        const string JS_READ = "function r(v){return(v!==null&&typeof v===\"object\"&&\"Value\"in v)?v.Value:v;}\n";
        // A literal Tags(...) read is mandatory: with trigger AutomaticTags the TIA compiler
        // statically scans for literal tag names, and loop-built names alone fail compilation.
        const string JS_TICK = "Tags(\"Valves_DB_Clock1Hz\").Read();\n";

        static string CountScript(int vStart, int vEnd, string condition, string prefix)
        {
            return JS_READ + JS_TICK +
                "let n=0;\n" +
                "for(let i=" + vStart + ";i<=" + vEnd + ";i++){\n" +
                "  let t=\"V\"+(\"000\"+i).slice(-3);\n" +
                "  let cfg=r(Tags(t+\"_Configured\").Read());\n" +
                "  let hl=r(Tags(t+\"_Healthy\").Read());\n" +
                "  let op=r(Tags(t+\"_OpenFB\").Read());\n" +
                "  let cl=r(Tags(t+\"_ClosedFB\").Read());\n" +
                "  let lo=r(Tags(t+\"_LocalMode\").Read());\n" +
                "  let flt=(!hl)||(op&&cl);\n" +
                "  if(" + condition + "){n++;}\n" +
                "}\n" +
                "return \"" + prefix + "\"+n;";
        }

        // Display-only relabelling: operator sees CM-01..CM-88, but every PLC member, all 616
        // HMI tags, and every script keep V001..V088 — nothing underneath changes.
        static string Disp(int n) { return "CM-" + n.ToString("D2"); }

        static HmiEllipse MakeDot(HmiScreen sc, string name, int cx, int cy, int rx, int ry,
                                   Color fill, Color border, int bw = 2)
        {
            var e = sc.ScreenItems.Create<HmiEllipse>(name);
            e.CenterX = cx; e.CenterY = cy;
            e.RadiusX = (uint)rx; e.RadiusY = (uint)ry;
            e.BackColor = fill; e.BorderColor = border; e.BorderWidth = (byte)bw;
            return e;
        }

        static void SetRotation(object obj, short angleDeg)
        {
            try {
                var p = obj.GetType().GetProperty("RotationAngle");
                if (p != null && p.CanWrite) p.SetValue(obj, angleDeg, null);
            } catch {}
        }

        // Draws a straight segment as a thin rotated rectangle — used to build
        // the ship hull's curved bow (no native line-to-point primitive available).
        static void MakeDiagLine(HmiScreen sc, string name, double x1, double y1, double x2, double y2,
                                   int thickness, Color color)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            double midX = (x1 + x2) / 2.0, midY = (y1 + y2) / 2.0;

            var r = sc.ScreenItems.Create<HmiRectangle>(name);
            r.Left = (int)Math.Round(midX - length / 2.0);
            r.Top  = (int)Math.Round(midY - thickness / 2.0);
            r.Width  = (uint)Math.Round(length);
            r.Height = (uint)thickness;
            r.BackColor = color; r.BorderColor = color; r.BorderWidth = 0;
            SetRotation(r, (short)Math.Round(angleDeg));
        }

        // Quadratic Bezier approximated by straight chords — used for the bow taper.
        // Segments overlap by 1px of length so no hairline gaps show at the joints.
        static void DrawBowCurve(HmiScreen sc, string name, double x0, double y0,
                                  double cx, double cy, double x1, double y1, int thickness, Color color)
        {
            const int SEG = 7;
            double px = x0, py = y0;
            for (int i = 1; i <= SEG; i++) {
                double t = (double)i / SEG, u = 1.0 - t;
                double qx = u * u * x0 + 2 * u * t * cx + t * t * x1;
                double qy = u * u * y0 + 2 * u * t * cy + t * t * y1;
                MakeDiagLine(sc, name + i, px, py, qx, qy, thickness, color);
                px = qx; py = qy;
            }
        }

        // Panel with a subtle drop-shadow for visual depth (used for all major boxes).
        static void MakePanel(HmiScreen sc, string name, int x, int y, int w, int h, Color bg, Color border, int bw)
        {
            MakeRect(sc, name + "_Shd", x + 5, y + 5, w, h, Color.FromArgb(50, 0, 0, 0), Color.FromArgb(50, 0, 0, 0), 0);
            MakeRect(sc, name, x, y, w, h, bg, border, bw);
        }

        static void AddNavClick(HmiButton btn, string targetScreen)
        {
            try {
                PropertyInfo evProp = null;
                foreach (var p in btn.GetType().GetProperties())
                    if (p.Name == "EventHandlers") { evProp = p; break; }
                if (evProp == null) { Console.WriteLine("  [NavClick ERR] No EventHandlers property on " + btn.GetType().Name); return; }
                object evObj = evProp.GetValue(btn, null);
                // CreateTappedHandler lives in GenerateHmiLayout.cs (same partial class) — it derives
                // the event enum from Create()'s own parameter instead of a hardcoded type name, so
                // this keeps working if this helper is ever pointed at a non-button widget.
                object handler = CreateTappedHandler(evObj);
                if (handler == null) { Console.WriteLine("  [NavClick ERR] Could not create Tapped handler for " + targetScreen); return; }
                var sp = handler.GetType().GetProperty("Script");
                object script = sp.GetValue(handler, null);
                var scp = script.GetType().GetProperty("ScriptCode");
                if (scp != null && scp.CanWrite)
                    scp.SetValue(script, "HMIRuntime.UI.SysFct.ChangeScreen(\"" + targetScreen + "\", \"~\");", null);
            } catch (Exception ex) { Console.WriteLine("  [NavClick ERR] " + ex.Message); }
        }

        // Writes OpenCmd/CloseCmd directly for this row's own valve — unlike the SBO popup
        // (which writes whichever valve "SelectedValve" points at, since it's one shared screen
        // reused for any valve), a table row already knows exactly which valve it is, so no
        // indirection is needed. Mirrors the popup's Open/Close scripts in GenerateHmiLayout.cs
        // (~line 856-869): guarded by Configured, and PLC-side interlocks (LocalMode/Healthy in
        // FB_ValveLoop) still apply regardless of what gets written here.
        static void AddCmdScript(HmiButton btn, string vTag, bool isOpen)
        {
            try {
                PropertyInfo evProp = null;
                foreach (var p in btn.GetType().GetProperties())
                    if (p.Name == "EventHandlers") { evProp = p; break; }
                if (evProp == null) { Console.WriteLine("  [CmdScript ERR] No EventHandlers property on " + btn.GetType().Name); return; }
                object evObj = evProp.GetValue(btn, null);
                object handler = CreateTappedHandler(evObj);
                if (handler == null) { Console.WriteLine("  [CmdScript ERR] Could not create Tapped handler for " + vTag); return; }
                var sp = handler.GetType().GetProperty("Script");
                object script = sp.GetValue(handler, null);
                var scp = script.GetType().GetProperty("ScriptCode");
                if (scp == null || !scp.CanWrite) return;

                string setTag = isOpen ? vTag + "_OpenCmd" : vTag + "_CloseCmd";
                string clrTag = isOpen ? vTag + "_CloseCmd" : vTag + "_OpenCmd";
                string jsCode = JS_READ +
                    "var cfg=r(Tags(\"" + vTag + "_Configured\").Read());\n" +
                    "if(!cfg) return;\n" +
                    "Tags(\"" + clrTag + "\").Write(false);\n" +
                    "Tags(\"" + setTag + "\").Write(true);";
                scp.SetValue(script, jsCode, null);
            } catch (Exception ex) { Console.WriteLine("  [CmdScript ERR] " + ex.Message); }
        }

        // ── Valve symbol: filled status badge + bowtie glyph, seated on the pipe ──
        // At 88-on-screen density the previous actuator/stem decoration and separate
        // hit-target were dropped (per the "circular badge, simplified" call) to keep
        // this at 3 Openness items/valve — 88 valves stays close to the item budget of
        // the old 12-valve decorative version. Click wiring stays on HmiButton (the
        // only widget with click proven in this project) rather than the ellipse badge,
        // by having the label button's transparent body double as the hit-target.
        // Shared by the badge (BackColor), and the valve-table's status text + dot (Screen_Bilge)
        // — one 6-state definition instead of three copies drifting apart over time.
        static string ValveStateReads(string vTag)
        {
            return JS_READ +
                "var cfg=r(Tags(\"" + vTag + "_Configured\").Read());\n" +
                "var op=r(Tags(\"" + vTag + "_OpenFB\").Read());\n" +
                "var cl=r(Tags(\"" + vTag + "_ClosedFB\").Read());\n" +
                "var hl=r(Tags(\"" + vTag + "_Healthy\").Read());\n" +
                "var lo=r(Tags(\"" + vTag + "_LocalMode\").Read());\n";
        }
        static string ValveStateColorScript(string vTag)
        {
            return ValveStateReads(vTag) +
                "if(!cfg) return 0xFF9AA3B0;\n" +
                "if(!hl||(op&&cl)) return 0xFFCD2026;\n" +
                "if(lo) return 0xFFE2A800;\n" +          // LOCAL — amber
                "if(op&&!cl) return 0xFF009E4A;\n" +
                "if(cl&&!op) return 0xFF606A7A;\n" +
                "return 0xFF00A2FF;";                     // MOVING/IN TRANSIT — blue
        }
        static string ValveStateTextScript(string vTag)
        {
            return ValveStateReads(vTag) +
                "if(!cfg) return \"UNCONFIGURED\";\n" +
                "if(!hl||(op&&cl)) return \"FAULT\";\n" +
                "if(lo) return \"LOCAL\";\n" +
                "if(op&&!cl) return \"OPEN\";\n" +
                "if(cl&&!op) return \"CLOSED\";\n" +
                "return \"MOVING\";";
        }

        static void DrawValveSym(HmiScreen sc, string name, int cx, int cy, int tagNum)
        {
            string vTag = string.Format("V{0:D3}", tagNum);
            const int R = 21; // 42px-diameter badge

            // 1. Status badge — a filled disc whose colour IS the valve state.
            var badge = MakeDot(sc, name + "_badge", cx, cy, R, R, M_MUTED, M_BORDER, 2);
            Dyn(badge, "BackColor", ValveStateColorScript(vTag), "T500ms");

            // 2. Bowtie glyph on top of the badge — fixed white, legible against all 6 states.
            var sym = sc.ScreenItems.Create<HmiTextBox>(name + "_sym");
            sym.Left = cx - R; sym.Top = cy - R; sym.Width = (uint)(R * 2); sym.Height = (uint)(R * 2);
            sym.BackColor = M_TRANS; sym.ForeColor = Color.White; sym.BorderWidth = 0;
            SetProp(sym, "HorizontalTextAlignment", "Center");
            SetProp(sym, "VerticalTextAlignment", "Middle");
            SetFont(sym, 20, true);
            SetText(sym, "Text", "&#x22C8;");

            // 3. Transparent button spanning badge + label area: its own Text (bottom-aligned)
            // IS the CM-nn label, and its native click opens the SBO popup — one item doing
            // both jobs instead of a separate label textbox plus a separate hit-target button.
            var hit = sc.ScreenItems.Create<HmiButton>(name + "_hit");
            hit.Left = cx - 30; hit.Top = cy - R; hit.Width = 60; hit.Height = (uint)(R * 2 + 18);
            hit.BackColor = M_TRANS; hit.ForeColor = M_TEXT;
            hit.BorderColor = M_TRANS; hit.BorderWidth = 0;
            SetProp(hit, "HorizontalTextAlignment", "Center");
            SetProp(hit, "VerticalTextAlignment", "Bottom");
            SetFont(hit, 13, true);
            SetText(hit, "Text", Disp(tagNum));
            try { hit.GetType().GetProperty("ShowFocusVisual").SetValue(hit, false, null); } catch {}
            AddPopupScript(hit, vTag);
        }

        // ── HOME SCREEN ─────────────────────────────────────────────────
        static void BuildScreenHome(HmiScreen sc)
        {
            Console.WriteLine("  Drawing Home Screen (v4 - 88-valve mimic, tables in one row below)...");

            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);

            BuildHomeHeader(sc);
            BuildNav(sc, "Screen_Home");

            // Illustration gets max width and max height per the client's request; the 6
            // table panels move below it into a single row, sized to their own minimum
            // legible width rather than the previous 3-column stack.
            // 198 + 560 + 16 + 288 = 1062 (18px bottom margin, matching the previous layout).
            // Mimic panel height was trimmed from 678 to make room for the taller KPI row below
            // (bigger text) and the taller shared header above — the badge grid itself is the
            // same size, only its surrounding margins got tighter.
            BuildVesselMimic(sc, 16, 198, 1888, 560);

            int tY = 774, tH = 288, tW = 304, tStep = 316, tX0 = 16;
            // Left-to-right order matches the mimic's corrected zone order above it (AFT-ER-FWD).
            BuildKpiBox(sc, "AFT BALLAST",     61, 88, "Aft", tX0 + 0 * tStep, tY, tW, tH);
            BuildKpiBox(sc, "BILGE / ER",       1, 28, "Er",  tX0 + 1 * tStep, tY, tW, tH);
            BuildKpiBox(sc, "FORWARD BALLAST", 29, 60, "Fwd", tX0 + 2 * tStep, tY, tW, tH);
            BuildSysStatus(sc,                       tX0 + 3 * tStep, tY, tW, tH);
            BuildPlantSummary(sc,                    tX0 + 4 * tStep, tY, tW, tH);
            BuildAlarmPanel(sc,                      tX0 + 5 * tStep, tY, tW, tH);
        }

        static void BuildHomeHeader(HmiScreen sc)
        {
            // Top utility strip — navy, live date/time/user.
            MakeRect(sc, "Hdr_Strip", 0, 0, 1920, 46, M_HDR, M_HDR, 0);

            var date = MakeLiveText(sc, "Hdr_Date", 20, 10, 370, 30, M_HDRTXT, "Left", 19, false);
            Dyn(date, "Text",
                "function p(n){return (\"0\"+n).slice(-2);}\n" +
                "var d=new Date();\n" +
                "return \"DATE:  \"+p(d.getDate())+\"/\"+p(d.getMonth()+1)+\"/\"+d.getFullYear();", "T1s");

            MakeRect(sc, "Hdr_Sep1", 408, 10, 1, 26, M_MUTED, M_MUTED, 0);

            var time = MakeLiveText(sc, "Hdr_Time", 428, 10, 300, 30, M_HDRTXT, "Left", 19, false);
            Dyn(time, "Text",
                "function p(n){return (\"0\"+n).slice(-2);}\n" +
                "var d=new Date();\n" +
                "return \"TIME:  \"+p(d.getHours())+\":\"+p(d.getMinutes())+\":\"+p(d.getSeconds());", "T1s");

            MakeRect(sc, "Hdr_Sep2", 760, 10, 1, 26, M_MUTED, M_MUTED, 0);
            MakeTb(sc, "Hdr_Vessel", 780, 8, 560, 30, "IMO 9834217  &#xB7;  MV WESTERLY", M_TRANS, M_HDRTXT, 0, "Left", 19, false);
            MakeTb(sc, "Hdr_User", 1500, 8, 400, 30, "&#x1F464;  USER: ENGINEER", M_TRANS, M_HDRTXT, 0, "Right", 19, false);

            // Title band.
            MakeRect(sc, "Title_Rule", 0, 46, 1920, 4, M_ACCENT, M_ACCENT, 0);
            MakeTb(sc, "Title_Main", 0, 50, 1920, 54, "MV WESTERLY  &#xB7;  VALVE REMOTE CONTROL SYSTEM",
                   M_TRANS, M_TEXT, 0, "Center", 42, true);
            MakeTb(sc, "Title_Sub", 0, 104, 1920, 24, "Bilge &amp; Ballast Distribution  &#x2014;  88 Motorised Valves",
                   M_TRANS, M_MUTED, 0, "Center", 19, false);
        }

        // Minimal screen for a nav target that has no dedicated design yet. Keeps the header
        // and nav bar fully functional (so nothing is a dead end) with a plain centred panel
        // standing in for the real content. Cheap to build (~10 items) — swap for a real
        // screen builder later without touching Run()'s screen-creation list or any nav wiring.
        static void BuildPlaceholderScreen(HmiScreen sc, string activeTarget, string title, string subtitle)
        {
            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);
            BuildHomeHeader(sc);
            BuildNav(sc, activeTarget);

            int px = 560, py = 300, pw = 800, ph = 300;
            MakePanel(sc, "Ph_BG", px, py, pw, ph, M_BOX, M_BORDER, 1);
            MakeRect(sc, "Ph_Hdr", px, py, pw, 38, M_HDR, M_HDR, 0);
            MakeTb(sc, "Ph_Ttl", px, py + 60, pw, 40, title, M_TRANS, M_TEXT, 0, "Center", 24, true);
            MakeTb(sc, "Ph_Sub", px, py + 110, pw, 26, subtitle, M_TRANS, M_MUTED, 0, "Center", 14, false);
            MakeTb(sc, "Ph_Note1", px, py + 160, pw, 24,
                   "This screen is not yet designed &#x2014; navigation is wired and working.",
                   M_TRANS, M_MUTED, 0, "Center", 13, false);
            MakeTb(sc, "Ph_Note2", px, py + 186, pw, 24,
                   "Use HOME for the full overview.", M_TRANS, M_MUTED, 0, "Center", 13, false);
        }

        // Shared by every screen — all 7 targets now exist (Screen_Home, Screen_Bilge,
        // Screen_FwdBallast, Screen_AftBallast, Screen_Alarms, Screen_Diagnostics,
        // Screen_Login are all created by Run()), so every button is live. `activeTarget`
        // is whichever of these the calling screen IS, so its own button highlights.
        static void BuildNav(HmiScreen sc, string activeTarget)
        {
            MakeRect(sc, "Nav_BG", 0, 128, 1920, 58, M_BOX, M_LINE, 1);

            string[] labels  = { "&#x2302;  HOME", "&#x1F4A7;  BILGE / ER", "&#x2693;  BALLAST FWD",
                                 "&#x2693;  BALLAST AFT", "&#x1F514;  ALARMS", "&#x1F4C8;  DIAGNOSTICS", "&#x1F464;  LOGIN" };
            string[] targets = { "Screen_Home", "Screen_Bilge", "Screen_FwdBallast", "Screen_AftBallast",
                                 "Screen_Alarms", "Screen_Diagnostics", "Screen_Login" };

            int w = 258, h = 46, y = 134, x0 = 20, gap = 8;
            for (int i = 0; i < labels.Length; i++) {
                bool active = (targets[i] == activeTarget);
                Color bg = active ? M_ACCENT : M_BOX;
                Color fg = active ? Color.White : M_TEXT;
                Color bd = active ? M_ACCENT : M_LINE;
                var btn = MakeBtn(sc, "Nav_" + i, x0 + i * (w + gap), y, w, h, labels[i], bg, fg, bd, 1, 18, active);
                if (!active) AddNavClick(btn, targets[i]); // no self-navigation needed on the active screen
            }
        }

        // ── VESSEL MIMIC — all 88 valves, full width/height ─────────────
        static void BuildVesselMimic(HmiScreen sc, int px, int py, int pw, int ph)
        {
            MakePanel(sc, "Mim_BG", px, py, pw, ph, M_BOX, M_BORDER, 1);
            MakeRect(sc, "Mim_Hdr", px, py, pw, 42, M_HDR, M_HDR, 0);
            MakeTb(sc, "Mim_Ttl", px + 16, py + 8, pw - 32, 30, "VESSEL MIMIC  &#x2014;  ALL 88 VALVES",
                   M_TRANS, M_HDRTXT, 0, "Left", 20, true);

            // Hull geometry — derived from (px,py,pw,ph) so this stays correct if the panel
            // is ever resized again. Zone order is AFT (stern, left) -> ER (mid) -> FWD (bow,
            // right) — real vessel geography; the previous ER-FWD-AFT order put "AFT BALLAST"
            // beside a "BOW" label, which was backwards.
            // Badges/pitch below are unchanged size — topY/botY margins were tightened (from a
            // 480px badge-row span with ~200px of top+bottom margin, to the same 312px span
            // with less surrounding whitespace) to make room for the taller shared header and
            // taller KPI row this panel now shares the screen with.
            int topY = py + 56, botY = py + 368, midY = (topY + botY) / 2;
            int sternX = px + 54;
            const int bowMargin = 34, bowLen = 220, hullT = 4;
            int bowTipX = px + pw - bowMargin;
            int straightX = bowTipX - bowLen;

            int zoneL = px + 94, zoneR = straightX;
            int zoneW = (zoneR - zoneL) / 3;
            int[] zoneX = { zoneL, zoneL + zoneW, zoneL + zoneW * 2 };
            int[] div = { zoneX[1], zoneX[2] };

            string[] zoneNames  = { "AFT BALLAST", "BILGE / ER", "FORWARD BALLAST" };
            int[] zoneVStart    = { 61, 1, 29 };
            int[] zoneVEnd      = { 88, 28, 60 };
            // Exact fit: 7+7+8 columns x 4 rows = 28+28+32 = 88, no leftover.
            int[] zoneCols      = { 7, 7, 8 };
            string[] zonePfx    = { "Aft", "Er", "Fwd" }; // matches Valves_DB_<prefix>Configured etc.

            // Alternating zone tint for visual separation.
            MakeRect(sc, "Zone_T0", zoneX[0], topY, zoneW, botY - topY, Color.FromArgb(16, 0, 116, 186), M_TRANS, 0);
            MakeRect(sc, "Zone_T2", zoneX[2], topY, zoneR - zoneX[2], botY - topY, Color.FromArgb(16, 0, 116, 186), M_TRANS, 0);

            // Hull outline: rounded stern (left), straight midbody, tapered bow (right).
            MakeDiagLine(sc, "Hull_StnT", sternX, topY + 46, sternX + 40, topY, hullT, M_BORDER);
            MakeRect(sc, "Hull_Stn", sternX, topY + 46, hullT, (botY - 46) - (topY + 46), M_BORDER, M_BORDER, 0);
            MakeDiagLine(sc, "Hull_StnB", sternX, botY - 46, sternX + 40, botY, hullT, M_BORDER);

            MakeRect(sc, "Hull_Top", sternX + 40, topY, straightX - (sternX + 40), hullT, M_BORDER, M_BORDER, 0);
            MakeRect(sc, "Hull_Bot", sternX + 40, botY, straightX - (sternX + 40), hullT, M_BORDER, M_BORDER, 0);

            DrawBowCurve(sc, "Hull_BowT", straightX, topY, bowTipX - 10, topY + 40, bowTipX, midY, hullT, M_BORDER);
            DrawBowCurve(sc, "Hull_BowB", straightX, botY, bowTipX - 10, botY - 40, bowTipX, midY, hullT, M_BORDER);

            // Dashed watertight bulkheads between zones — dash size unchanged, just fewer of
            // them to fit the shorter topY..botY span (312px, was 480px).
            int bhdSegs = (botY - topY - 16) / 29;
            for (int d = 0; d < 2; d++)
                for (int seg = 0; seg < bhdSegs; seg++)
                    MakeRect(sc, "Bhd" + d + "_" + seg, div[d], topY + 8 + seg * 29, 3, 17, M_MUTED, M_MUTED, 0);

            // Engine block — the ER zone is now full of valves, so this is a compact label
            // in its top margin rather than the earlier full-size decorative box.
            int erX = zoneX[1];
            MakeRect(sc, "Eng_Box", erX + 20, topY + 4, 120, 40, M_BG, M_BORDER, 2);
            MakeTb(sc, "Eng_Lbl", erX + 20, topY + 5, 120, 24, "M/E", M_TRANS, M_TEXT, 0, "Center", 18, true);
            MakeTb(sc, "Eng_Sub", erX + 20, topY + 28, 120, 14, "MAIN ENGINE", M_TRANS, M_MUTED, 0, "Center", 12, false);

            // 4-row valve grid — badges/pitch (cellW, 66px row pitch) are unchanged from before;
            // only the clearance above (M/E block) and below (captions/legend) got tighter.
            const int cellW = 60;
            int[] rowY = { topY + 78, topY + 144, topY + 210, topY + 276 };

            for (int z = 0; z < 3; z++) {
                int cols = zoneCols[z];
                int usedW = cols * cellW;
                int leftPad = (zoneW - usedW) / 2;
                int rowSpanL = zoneX[z] + leftPad;

                // One horizontal manifold per row + one vertical trunk per zone — a full
                // per-valve P&ID routing would clutter badly at this density and cost far
                // more Openness items for no real gain in clarity.
                int trunkX = zoneX[z] + zoneW / 2;
                MakeRect(sc, "Pipe_Trunk" + z, trunkX - 2, rowY[0], 4, rowY[3] - rowY[0], M_BORDER, M_BORDER, 0);
                for (int r = 0; r < 4; r++)
                    MakeRect(sc, "Pipe_Row" + z + "_" + r, rowSpanL, rowY[r] - 2, usedW, 4, M_BORDER, M_BORDER, 0);

                int vNum = zoneVStart[z];
                for (int r = 0; r < 4; r++) {
                    for (int c = 0; c < cols; c++) {
                        int cx = rowSpanL + (int)((c + 0.5) * cellW);
                        DrawValveSym(sc, "Vlv_" + z + "_" + r + "_" + c, cx, rowY[r], vNum);
                        vNum++;
                    }
                }

                // Zone caption below the hull outline, with a live configured count — reads
                // Valves_DB_<prefix>Configured directly instead of looping the zone's valves.
                int capX = zoneX[z] + zoneW / 2 - 100;
                MakeTb(sc, "Zone_Lbl" + z, capX, botY + 10, 200, 26, zoneNames[z], M_TRANS, M_TEXT, 0, "Center", 18, true);
                var cnt = MakeLiveText(sc, "Zone_Cnt" + z, capX, botY + 38, 200, 22, M_MUTED, "Center", 15, false);
                Dyn(cnt, "Text", JS_READ + "return \"\"+r(Tags(\"Valves_DB_" + zonePfx[z] + "Configured\").Read())+\" / " +
                        (zoneVEnd[z] - zoneVStart[z] + 1) + " CONFIGURED\";", "T1s");
            }

            // Bow / stern orientation markers — now correctly aligned: AFT zone sits at the
            // stern end, FWD zone sits at the bow end.
            MakeTb(sc, "Mim_Bow",   bowTipX - 80, midY - 14, 100, 26, "BOW", M_TRANS, M_MUTED, 0, "Center", 16, true);
            MakeTb(sc, "Mim_Stern", sternX - 54, midY - 14, 80, 26, "AFT", M_TRANS, M_MUTED, 0, "Center", 16, true);

            // Legend strip.
            int ly = py + ph - 60;
            MakeRect(sc, "Lgd_Sep", px + 20, ly - 14, pw - 40, 1, M_LINE, M_LINE, 0);
            string[] lgLabels = { "OPEN", "CLOSED", "MOVING", "LOCAL", "FAULT", "UNCONFIGURED" };
            Color[]  lgColors = { M_GREEN, M_MUTED, M_BLUE, M_YELLOW, M_RED, Color.FromArgb(255, 154, 163, 176) };
            int lx = px + 28;
            for (int i = 0; i < lgLabels.Length; i++) {
                MakeTb(sc, "Lgd_Sym" + i, lx, ly, 30, 30, "&#x22C8;", M_TRANS, lgColors[i], 0, "Center", 20, true);
                MakeTb(sc, "Lgd_Txt" + i, lx + 32, ly, 148, 30, lgLabels[i], M_TRANS, M_TEXT, 0, "Left", 15, false);
                lx += 182;
            }
        }

        // ── Table row: 6 panels, single row below the mimic (304px each) ─
        // zonePrefix matches the PLC tag family Valves_DB_<prefix><Stat> (Er/Fwd/Aft) that
        // FB_ValveLoop now computes directly — every row here is one tag read, not a loop
        // over the zone's 28-32 valves. That loop-per-cell was what made this panel slow to
        // repopulate every time an operator navigated back to Screen_Home.
        static void BuildKpiBox(HmiScreen sc, string title, int vStart, int vEnd, string zonePrefix, int x, int y, int w, int h)
        {
            MakePanel(sc, "KPI_BG_" + vStart, x, y, w, h, M_BOX, M_BORDER, 1);
            MakeRect(sc, "KPI_Hdr_" + vStart, x, y, w, 36, M_HDR, M_HDR, 0);
            MakeTb(sc, "KPI_Ttl_" + vStart, x + 10, y + 5, w - 108, 26, title, M_TRANS, M_HDRTXT, 0, "Left", 17, true);
            MakeTb(sc, "KPI_Rng_" + vStart, x + w - 96, y + 7, 86, 22,
                   string.Format("V{0:D3}&#x2013;V{1:D3}", vStart, vEnd), M_TRANS, M_HDRTXT, 0, "Right", 13, false);

            // TOTAL is a fixed constant (the zone's tag-range size). Every other row maps
            // directly to one of the six stats FB_ValveLoop precomputes per zone.
            string[] labels = { "TOTAL", "OPEN", "CLOSED", "MOVING", "LOCAL", "FAULTS" };
            string[] tagSuf = { null,    "Open", "Closed", "Transit", "Local", "Fault" };
            Color[]  cols   = { M_TEXT, M_GREEN, M_MUTED, M_BLUE, M_YELLOW, M_RED };
            int zoneTotal   = vEnd - vStart + 1;
            int rowH = (h - 36) / labels.Length;

            for (int i = 0; i < labels.Length; i++) {
                int rY = y + 36 + i * rowH;
                MakeDot(sc, "KPI_Icn_" + vStart + "_" + i, x + 20, rY + rowH / 2, 7, 7, M_TRANS, cols[i], 2);
                MakeTb(sc, "KPI_Lbl_" + vStart + "_" + i, x + 36, rY, w - 116, rowH, labels[i], M_TRANS,
                       i == 0 ? M_TEXT : M_MUTED, 0, "Left", 15, i == 0);

                if (i == 0) {
                    // Zone size is fixed by the tag range — a constant, not a live count.
                    MakeTb(sc, "KPI_Val_" + vStart + "_" + i, x + w - 78, rY, 68, rowH,
                           zoneTotal.ToString(), M_TRANS, M_TEXT, 0, "Right", 22, true);
                } else {
                    var val = MakeLiveText(sc, "KPI_Val_" + vStart + "_" + i, x + w - 78, rY, 68, rowH, cols[i], "Right", 22, true);
                    Dyn(val, "Text", JS_READ + "return \"\"+r(Tags(\"Valves_DB_" + zonePrefix + tagSuf[i] + "\").Read());", "T1s");
                }
                if (i < labels.Length - 1) MakeRect(sc, "KPI_Sep_" + vStart + "_" + i, x + 10, rY + rowH - 1, w - 20, 1, M_LINE, M_LINE, 0);
            }
        }

        static void BuildSysStatus(HmiScreen sc, int x, int y, int w, int h)
        {
            MakePanel(sc, "Sys_BG", x, y, w, h, M_BOX, M_BORDER, 1);
            MakeRect(sc, "Sys_Hdr", x, y, w, 36, M_HDR, M_HDR, 0);
            MakeTb(sc, "Sys_Ttl", x + 10, y + 5, w - 20, 26, "SYSTEM STATUS", M_TRANS, M_HDRTXT, 0, "Left", 17, true);

            string[] items = { "PLC S7-1200", "ER RIO", "FWD RIO", "AFT RIO", "UPS", "PROFINET" };
            int rowH = (h - 36) / items.Length;
            for (int i = 0; i < items.Length; i++) {
                int rY = y + 36 + i * rowH;
                MakeDot(sc, "SysDot_" + i, x + 20, rY + rowH / 2, 7, 7, M_GREEN, M_GREEN, 0);
                MakeTb(sc, "SysLbl_" + i, x + 36, rY, w - 116, rowH, items[i], M_TRANS, M_TEXT, 0, "Left", 15, false);
                MakeTb(sc, "SysVal_" + i, x + w - 88, rY, 74, rowH, "OK", M_TRANS, M_GREEN, 0, "Right", 15, true);
                if (i < items.Length - 1)
                    MakeRect(sc, "SysSep_" + i, x + 10, rY + rowH - 1, w - 20, 1, M_LINE, M_LINE, 0);
            }
        }

        static void BuildPlantSummary(HmiScreen sc, int x, int y, int w, int h)
        {
            MakePanel(sc, "Pls_BG", x, y, w, h, M_BOX, M_BORDER, 1);
            MakeRect(sc, "Pls_Hdr", x, y, w, 36, M_HDR, M_HDR, 0);
            MakeTb(sc, "Pls_Ttl", x + 10, y + 5, w - 20, 26, "PLANT SUMMARY", M_TRANS, M_HDRTXT, 0, "Left", 17, true);

            int rowH = (h - 36) / 3;
            int rY0 = y + 36;

            // TOTAL CHANNELS is a fixed constant — never worth a dynamization.
            MakeTb(sc, "Pls_Lbl0", x + 14, rY0, w - 116, rowH, "TOTAL CHANNELS", M_TRANS, M_MUTED, 0, "Left", 15, false);
            MakeTb(sc, "Pls_Val0", x + w - 78, rY0, 68, rowH, "88", M_TRANS, M_TEXT, 0, "Right", 22, true);
            MakeRect(sc, "Pls_Sep0", x + 10, rY0 + rowH - 1, w - 20, 1, M_LINE, M_LINE, 0);

            // CONFIGURED now reads Valves_DB_TotalConfigured directly — FB_ValveLoop computes
            // it in the same pass as the other totals, so this no longer loops all 88 tags.
            MakeTb(sc, "Pls_Lbl1", x + 14, rY0 + rowH, w - 116, rowH, "CONFIGURED", M_TRANS, M_MUTED, 0, "Left", 15, false);
            var cfgVal = MakeLiveText(sc, "Pls_Val1", x + w - 78, rY0 + rowH, 68, rowH, M_ACCENT, "Right", 22, true);
            Dyn(cfgVal, "Text", JS_READ + "return \"\"+r(Tags(\"Valves_DB_TotalConfigured\").Read());", "T1s");
            MakeRect(sc, "Pls_Sep1", x + 10, rY0 + rowH * 2 - 1, w - 20, 1, M_LINE, M_LINE, 0);

            // LOCAL MODE reads Valves_DB_TotalLocal directly instead of looping — FB_ValveLoop
            // already computes this every scan (temp_fb_valveloop.xml) and the HMI tag for it
            // (CreateSummaryHmiTags) existed but was unused. One tag read replaces 88.
            MakeTb(sc, "Pls_Lbl2", x + 14, rY0 + rowH * 2, w - 116, rowH, "LOCAL MODE", M_TRANS, M_MUTED, 0, "Left", 15, false);
            var locVal = MakeLiveText(sc, "Pls_Val2", x + w - 78, rY0 + rowH * 2, 68, rowH, M_YELLOW, "Right", 22, true);
            Dyn(locVal, "Text", JS_READ + "return \"\"+r(Tags(\"Valves_DB_TotalLocal\").Read());", "T1s");
        }

        static void BuildAlarmPanel(HmiScreen sc, int x, int y, int w, int h)
        {
            MakePanel(sc, "Alm_BG", x, y, w, h, M_BOX, M_BORDER, 1);
            MakeRect(sc, "Alm_Hdr", x, y, w, 36, M_HDR, M_HDR, 0);
            MakeTb(sc, "Alm_Ttl", x + 10, y + 5, w - 20, 26, "ACTIVE ALARMS", M_TRANS, M_HDRTXT, 0, "Left", 17, true);

            // Reads Valves_DB_TotalFault directly — same pre-computed-total optimization as
            // Plant Summary's LOCAL MODE row, removing another 88-valve loop.
            string readFaultTotal = JS_READ + "var n=r(Tags(\"Valves_DB_TotalFault\").Read());\n";

            var big = MakeLiveText(sc, "Alm_Count", x + 14, y + 42, 90, h - 92, M_RED, "Center", 50, true);
            Dyn(big, "Text", readFaultTotal + "return \"\"+n;", "T1s");

            var state = MakeLiveText(sc, "Alm_State", x + 112, y + 48, w - 126, 26, M_GREEN, "Left", 18, true);
            Dyn(state, "Text", readFaultTotal + "return n>0?\"ALARM ACTIVE\":\"ALL NORMAL\";", "T1s");
            Dyn(state, "ForeColor", readFaultTotal + "return n>0?0xFFCD2026:0xFF009E4A;", "T1s");

            MakeTb(sc, "Alm_Hint", x + 112, y + 78, w - 126, 22, "Tap to open annunciator", M_TRANS, M_MUTED, 0, "Left", 13, false);

            var goAlarms = MakeBtn(sc, "Alm_Btn", x + 14, y + h - 42, w - 28, 34, "&#x1F514;  OPEN ALARMS",
                                    M_HDR, M_HDRTXT, M_BORDER, 1, 15, true);
            AddNavClick(goAlarms, "Screen_Alarms");
        }

        // ── SCREEN_BILGE — ER zone (V001-028): illustration + paginated table + summary ──
        static void BuildScreenBilge(HmiScreen sc)
        {
            Console.WriteLine("  Drawing Screen_Bilge (ER zone: 28 valves, illustration + paginated table + summary)...");

            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);
            BuildHomeHeader(sc);
            BuildNav(sc, "Screen_Bilge");

            // Illustration gets max width/height (like Home's mimic) — the table below only needs
            // to fit 8 rows per page now (not all 28 at once), so it doesn't need much height.
            // 198 + 410 + 14 + 380 + 14 + 46 = 1062, matching Home's same bottom boundary.
            BuildZoneMimic(sc, 16, 198, 1888, 410, 1, 28, 14, 2, "BILGE / ER");
            BuildValveTable(sc, 16, 622, 1510, 380, 1, 28);

            // Summary now sits beside the table (same row) instead of stretching the full column
            // height — right-sized instead of the previous 6-rows-in-864px stretch.
            BuildKpiBox(sc, "BILGE / ER SUMMARY", 1, 28, "Er", 1540, 622, 364, 380);

            // Page UP/DOWN — below the summary specifically, not spanning the table's width too.
            int pbY = 622 + 380 + 14;
            var upBtn   = MakeBtn(sc, "Bilge_PageUp",   1540, pbY, 174, 46, "&#x25B2;  UP",   M_HDR, M_HDRTXT, M_BORDER, 1, 16, true);
            var downBtn = MakeBtn(sc, "Bilge_PageDown", 1730, pbY, 174, 46, "&#x25BC;  DOWN", M_HDR, M_HDRTXT, M_BORDER, 1, 16, true);
            AddPageNavScript(upBtn, "BilgePage", -1, 1);
            AddPageNavScript(downBtn, "BilgePage", 1, 1);
            // Dim each button when it's already at its boundary (page 0 for UP, page 1 for DOWN) —
            // purely visual feedback; the click script's own clamp is what actually prevents
            // going out of range.
            Dyn(upBtn, "BackColor", JS_READ + "var p=r(Tags(\"BilgePage\").Read()); return p<=0?0xFF3A4356:0xFF263242;", "T1s");
            Dyn(upBtn, "ForeColor", JS_READ + "var p=r(Tags(\"BilgePage\").Read()); return p<=0?0xFF7A8494:0xFFF5F7FA;", "T1s");
            Dyn(downBtn, "BackColor", JS_READ + "var p=r(Tags(\"BilgePage\").Read()); return p>=1?0xFF3A4356:0xFF263242;", "T1s");
            Dyn(downBtn, "ForeColor", JS_READ + "var p=r(Tags(\"BilgePage\").Read()); return p>=1?0xFF7A8494:0xFFF5F7FA;", "T1s");
        }

        // Same CreateTappedHandler pattern as AddCmdScript/AddNavClick — writes a clamped page
        // index to an internal (no-PLC-binding) HMI tag like BilgePage.
        static void AddPageNavScript(HmiButton btn, string tagName, int delta, int maxPage)
        {
            try {
                PropertyInfo evProp = null;
                foreach (var p in btn.GetType().GetProperties())
                    if (p.Name == "EventHandlers") { evProp = p; break; }
                if (evProp == null) { Console.WriteLine("  [PageNav ERR] No EventHandlers property on " + btn.GetType().Name); return; }
                object evObj = evProp.GetValue(btn, null);
                object handler = CreateTappedHandler(evObj);
                if (handler == null) { Console.WriteLine("  [PageNav ERR] Could not create Tapped handler for " + tagName); return; }
                var sp = handler.GetType().GetProperty("Script");
                object script = sp.GetValue(handler, null);
                var scp = script.GetType().GetProperty("ScriptCode");
                if (scp == null || !scp.CanWrite) return;

                string jsCode = JS_READ +
                    "var cur=r(Tags(\"" + tagName + "\").Read());\n" +
                    "var next=(cur||0)+(" + delta + ");\n" +
                    "if(next<0) next=0;\n" +
                    "if(next>" + maxPage + ") next=" + maxPage + ";\n" +
                    "Tags(\"" + tagName + "\").Write(next);";
                scp.SetValue(script, jsCode, null);
            } catch (Exception ex) { Console.WriteLine("  [PageNav ERR] " + ex.Message); }
        }

        // Single-zone illustration: a plain rectangular compartment (this is one midship section
        // of the vessel, not the whole ship, so no bow/stern taper like BuildVesselMimic on Home).
        // Generalised over (vStart,vEnd,cols,rows) so FwdBallast/AftBallast screens can reuse it.
        static void BuildZoneMimic(HmiScreen sc, int px, int py, int pw, int ph,
                                    int vStart, int vEnd, int cols, int rows, string zoneLabel)
        {
            MakePanel(sc, "ZMim_BG", px, py, pw, ph, M_BOX, M_BORDER, 1);
            MakeRect(sc, "ZMim_Hdr", px, py, pw, 40, M_HDR, M_HDR, 0);
            MakeTb(sc, "ZMim_Ttl", px + 16, py + 7, pw - 32, 28,
                   zoneLabel + " &#x2014; " + (vEnd - vStart + 1) + " VALVES", M_TRANS, M_HDRTXT, 0, "Left", 20, true);

            int hullL = px + 24, hullT = py + 44, hullR = px + pw - 24, hullB = py + ph - 16;
            MakeRect(sc, "ZHull_Top",   hullL,     hullT,     hullR - hullL, 4,             M_BORDER, M_BORDER, 0);
            MakeRect(sc, "ZHull_Bot",   hullL,     hullB - 4, hullR - hullL, 4,             M_BORDER, M_BORDER, 0);
            MakeRect(sc, "ZHull_Left",  hullL,     hullT,     4,             hullB - hullT, M_BORDER, M_BORDER, 0);
            MakeRect(sc, "ZHull_Right", hullR - 4, hullT,     4,             hullB - hullT, M_BORDER, M_BORDER, 0);

            int gridT = hullT + 20, gridB = hullB - 20, gridL = hullL + 20, gridR = hullR - 20;
            int rowPitch = (gridB - gridT) / rows;
            int colPitch = (gridR - gridL) / cols;

            // One horizontal manifold per row, spanning the grid.
            for (int r = 0; r < rows; r++) {
                int ry = gridT + rowPitch * r + rowPitch / 2;
                MakeRect(sc, "ZPipe_Row" + r, gridL, ry - 2, gridR - gridL, 4, M_BORDER, M_BORDER, 0);
            }

            int vNum = vStart;
            for (int r = 0; r < rows; r++) {
                int ry = gridT + rowPitch * r + rowPitch / 2;
                for (int c = 0; c < cols; c++) {
                    if (vNum > vEnd) break;
                    int cx = gridL + colPitch * c + colPitch / 2;
                    DrawValveSym(sc, "ZVlv_" + r + "_" + c, cx, ry, vNum);
                    vNum++;
                }
            }
        }

        // Paginated valve table: NO. | TAG | NAME | LOCATION | STATUS | OPEN | CLOSE, split into 2
        // columns of 8 rows each (16 valves per page). All 28 rows are built once; each row's
        // Visible property is dynamized against the internal BilgePage tag, so page 0's and page
        // 1's rows occupy the exact same screen slot and only one is ever shown at a time — the
        // UP/DOWN buttons (built in BuildScreenBilge) just change which page is visible. Name/
        // Location read the manually-maintained Valve_Meta_DB tags (Vnnn_Name/Vnnn_Location) —
        // empty until an engineer fills them in.
        static void BuildValveTable(HmiScreen sc, int px, int py, int pw, int ph, int vStart, int vEnd)
        {
            MakePanel(sc, "Tbl_BG", px, py, pw, ph, M_BOX, M_BORDER, 1);
            MakeRect(sc, "Tbl_Hdr", px, py, pw, 38, M_HDR, M_HDR, 0);
            MakeTb(sc, "Tbl_Ttl", px + 14, py + 6, pw - 28, 26, "VALVE LIST", M_TRANS, M_HDRTXT, 0, "Left", 18, true);

            const int cols = 2;
            const int rowsPerCol = 8; // per page
            int vCount = vEnd - vStart + 1;
            int pages = (vCount + (cols * rowsPerCol) - 1) / (cols * rowsPerCol); // ceil(28/16)=2
            int colGutter = 8;
            int colW = (pw - 32 - colGutter) / cols;     // 16px side margins inside the panel
            const int colHdrH = 30;
            int rowH = (ph - 38 - colHdrH - 10) / rowsPerCol;

            // Field widths within one ~colW-wide column — unchanged from the non-paginated version,
            // since colW here (~731px) is roomy enough to not need any shrinking.
            const int noW = 36, tagW = 58, nameW = 190, locW = 140, statusW = 98, btnW = 94, btnGap = 6;

            // Column headers — page-independent, built once.
            for (int col = 0; col < cols; col++) {
                int cx0 = px + 16 + col * (colW + colGutter);
                int hy = py + 38 + 2;
                int hxTag  = cx0 + noW;
                int hxName = hxTag + tagW;
                int hxLoc  = hxName + nameW;
                int hxSt   = hxLoc + locW;
                int hxCmd  = hxSt + statusW;

                MakeTb(sc, "Tbl_H_No"   + col, cx0,    hy, noW,               colHdrH, "NO.",      M_TRANS, M_MUTED, 0, "Center", 14, true);
                MakeTb(sc, "Tbl_H_Tag"  + col, hxTag,  hy, tagW,              colHdrH, "TAG",      M_TRANS, M_MUTED, 0, "Center", 14, true);
                MakeTb(sc, "Tbl_H_Name" + col, hxName, hy, nameW - 4,         colHdrH, "NAME",     M_TRANS, M_MUTED, 0, "Left",   14, true);
                MakeTb(sc, "Tbl_H_Loc"  + col, hxLoc,  hy, locW - 4,          colHdrH, "LOCATION", M_TRANS, M_MUTED, 0, "Left",   14, true);
                MakeTb(sc, "Tbl_H_St"   + col, hxSt,   hy, statusW,           colHdrH, "STATUS",   M_TRANS, M_MUTED, 0, "Center", 14, true);
                MakeTb(sc, "Tbl_H_Cmd"  + col, hxCmd,  hy, btnW * 2 + btnGap, colHdrH, "COMMAND",  M_TRANS, M_MUTED, 0, "Center", 14, true);
            }

            int bodyTop = py + 38 + 2 + colHdrH + 4;
            for (int page = 0; page < pages; page++) {
                string visScript = JS_READ + "var p=r(Tags(\"BilgePage\").Read()); return p==" + page + ";";
                for (int col = 0; col < cols; col++) {
                    int cx0 = px + 16 + col * (colW + colGutter);
                    int hxTag  = cx0 + noW;
                    int hxName = hxTag + tagW;
                    int hxLoc  = hxName + nameW;
                    int hxSt   = hxLoc + locW;
                    int hxCmd  = hxSt + statusW;

                    for (int r = 0; r < rowsPerCol; r++) {
                        int vNum = vStart + page * (cols * rowsPerCol) + col * rowsPerCol + r;
                        if (vNum > vEnd) continue;
                        int rY = bodyTop + r * rowH;
                        string vTag = string.Format("V{0:D3}", vNum);

                        var noTb = MakeTb(sc, "Tr_No_"  + vNum, cx0,          rY, noW,       rowH, (vNum - vStart + 1).ToString(), M_TRANS, M_MUTED, 0, "Center", 15, false);
                        Dyn(noTb, "Visible", visScript, "T1s");
                        var tagTb = MakeTb(sc, "Tr_Tag_" + vNum, cx0 + noW,    rY, tagW,      rowH, Disp(vNum),                    M_TRANS, M_TEXT,  0, "Center", 15, true);
                        Dyn(tagTb, "Visible", visScript, "T1s");

                        var nameVal = MakeLiveText(sc, "Tr_Name_" + vNum, hxName, rY, nameW - 4, rowH, M_TEXT, "Left", 13, false);
                        Dyn(nameVal, "Text", JS_READ + "var v=r(Tags(\"" + vTag + "_Name\").Read()); return (v&&v.length)?v:\"&#x2014;\";", "T1s");
                        Dyn(nameVal, "Visible", visScript, "T1s");

                        var locVal = MakeLiveText(sc, "Tr_Loc_" + vNum, hxLoc, rY, locW - 4, rowH, M_MUTED, "Left", 13, false);
                        Dyn(locVal, "Text", JS_READ + "var v=r(Tags(\"" + vTag + "_Location\").Read()); return (v&&v.length)?v:\"&#x2014;\";", "T1s");
                        Dyn(locVal, "Visible", visScript, "T1s");

                        var dot = MakeDot(sc, "Tr_Dot_" + vNum, hxSt + 12, rY + rowH / 2, 6, 6, M_MUTED, M_MUTED, 0);
                        Dyn(dot, "BackColor", ValveStateColorScript(vTag), "T500ms");
                        Dyn(dot, "Visible", visScript, "T1s");
                        var stVal = MakeLiveText(sc, "Tr_St_" + vNum, hxSt + 24, rY, statusW - 28, rowH, M_MUTED, "Left", 14, true);
                        Dyn(stVal, "Text", ValveStateTextScript(vTag), "T500ms");
                        Dyn(stVal, "ForeColor", ValveStateColorScript(vTag), "T500ms");
                        Dyn(stVal, "Visible", visScript, "T1s");

                        int btnX = hxCmd;
                        var openBtn  = MakeBtn(sc, "Tr_Open_"  + vNum, btnX,                    rY + 2, btnW, rowH - 4, "OPEN",  M_BOX, M_GREEN, M_GREEN, 1, 15, true);
                        var closeBtn = MakeBtn(sc, "Tr_Close_" + vNum, btnX + btnW + btnGap,    rY + 2, btnW, rowH - 4, "CLOSE", M_BOX, M_RED,   M_RED,   1, 15, true);
                        AddCmdScript(openBtn, vTag, true);
                        AddCmdScript(closeBtn, vTag, false);
                        // Fill with the state color when active; white/idle otherwise.
                        Dyn(openBtn, "BackColor", ValveStateReads(vTag) + "if(op&&!cl) return 0xFF009E4A; return 0xFFFFFFFF;", "T500ms");
                        Dyn(closeBtn, "BackColor", ValveStateReads(vTag) + "if(cl&&!op) return 0xFFCD2026; return 0xFFFFFFFF;", "T500ms");
                        Dyn(openBtn, "Visible", visScript, "T1s");
                        Dyn(closeBtn, "Visible", visScript, "T1s");

                        bool nextExistsInSlot = r < rowsPerCol - 1 && (vNum + 1) <= vEnd;
                        if (nextExistsInSlot) {
                            var sep = MakeRect(sc, "Tr_Sep_" + vNum, cx0, rY + rowH - 1, colW, 1, M_LINE, M_LINE, 0);
                            Dyn(sep, "Visible", visScript, "T1s");
                        }
                    }
                }
            }
        }
    }
}
