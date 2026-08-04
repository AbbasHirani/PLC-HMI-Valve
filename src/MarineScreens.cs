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
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;
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
        // Table dressing. Alternating row tint carries the eye across a wide row far better than a
        // hairline rule, and the header band separates labels from data without another border.
        static readonly Color M_ZEBRA    = Color.FromArgb(255, 231, 235, 241); // Alternate row tint
        static readonly Color M_HDRBAND  = Color.FromArgb(255, 219, 224, 233); // Column-header band

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
            b.Left = SX(x); b.Top = SY(y); b.Width = (uint)SX(w); b.Height = (uint)SY(h);
            b.BackColor = bg; b.ForeColor = fg; b.BorderColor = border; b.BorderWidth = (byte)bw;
            SetFont(b, SFont(fontSize), bold);
            SetText(b, "Text", label);
            return b;
        }

        static HmiTextBox MakeTb(HmiScreen sc, string name, int x, int y, int w, int h,
                                   string label, Color bg, Color fg, int bw = 0, string align = "Center",
                                   int fontSize = 14, bool bold = false)
        {
            var tb = sc.ScreenItems.Create<HmiTextBox>(name);
            tb.Left = SX(x); tb.Top = SY(y); tb.Width = (uint)SX(w); tb.Height = (uint)SY(h);
            tb.BackColor = bg; tb.ForeColor = fg; tb.BorderWidth = (byte)bw;
            SetProp(tb, "HorizontalTextAlignment", align);
            SetProp(tb, "VerticalTextAlignment", "Middle");
            SetFont(tb, SFont(fontSize), bold);
            SetText(tb, "Text", label);
            return tb;
        }

        // Flat, non-interactive-looking button — the only widget whose "Text" property
        // reliably accepts a ScriptDynamization, so it backs every live numeric readout.
        static HmiButton MakeLiveText(HmiScreen sc, string name, int x, int y, int w, int h,
                                       Color fg, string align, int fontSize, bool bold)
        {
            var b = sc.ScreenItems.Create<HmiButton>(name);
            b.Left = SX(x); b.Top = SY(y); b.Width = (uint)SX(w); b.Height = (uint)SY(h);
            b.BackColor = M_TRANS; b.ForeColor = fg;
            b.BorderColor = M_TRANS; b.BorderWidth = 0;
            SetProp(b, "HorizontalTextAlignment", align);
            SetFont(b, SFont(fontSize), bold);
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

        // ── Native tag binding (no JavaScript) ──────────────────────────
        // Every dynamic property in this project used to be a ScriptDynamization: a JS block
        // re-run on a cyclic trigger that calls Tags(...).Read(). Screen_Home carried ~111 of
        // them, all instantiated and evaluated when the screen activates — the most plausible
        // remaining cause of the slow re-populate on screen switch. A TagDynamization binds the
        // property straight to a tag and updates push-based when the value changes, with no
        // script engine involved. Use Dyn() only where a script is genuinely required (string
        // composition, or no tag at all, e.g. the header clock).
        static object DynTag(object item, string prop, string tagName)
        {
            try {
                var dp = item.GetType().GetProperty("Dynamizations");
                if (dp == null) { Console.WriteLine("  [DynTag ERR ." + prop + "] no Dynamizations property"); return null; }
                object dyns = dp.GetValue(item, null);
                if (dyns == null) return null;

                MethodInfo create = null;
                foreach (var m in dyns.GetType().GetMethods()) {
                    if (m.Name != "Create" || !m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(string)) { create = m; break; }
                }
                if (create == null) { Console.WriteLine("  [DynTag ERR ." + prop + "] no Create<T>(string)"); return null; }

                object d = create.MakeGenericMethod(typeof(TagDynamization)).Invoke(dyns, new object[] { prop });
                var td = (TagDynamization)d;
                td.Tag = tagName;
                return td;
            } catch (Exception ex) {
                Console.WriteLine("  [DynTag ERR ." + prop + "] " + ex.Message);
                return null;
            }
        }

        // Attaches a value->result mapping (state code 0..5 -> colour or string) to a
        // TagDynamization, so the mapping happens natively instead of in a JS if-chain.
        // The exact entry shape TIA wants isn't documented in the API surface — Simple takes a
        // Condition string, Range takes From/To (its RangeType is read-only) — so try Simple
        // first and fall back to Range, reporting which one took so the build log records it.
        static bool s_mapShapeLogged = false;

        // Reflection wraps every failure in TargetInvocationException, whose own Message is the
        // useless "Exception has been thrown by the target of an invocation." Always report the
        // innermost message or there is nothing to diagnose from.
        static string Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.GetType().Name + ": " + ex.Message;
        }

        // codes[] and values[] are parallel: codes[k] is the tag value to match, values[k] the
        // colour/string to show. Explicit codes rather than array positions because the table needs
        // to map -1 (empty slot on a short last page), which an index-based map can't express.
        static bool AddValueMap(object tagDyn, int[] codes, object[] values)
        {
            if (tagDyn == null) return false;
            try {
                var vcProp = tagDyn.GetType().GetProperty("ValueConverter");
                object vc = vcProp.GetValue(tagDyn, null);
                var mtProp = vc.GetType().GetProperty("MappingTable");
                object mt = mtProp.GetValue(vc, null);
                object entries = mt.GetType().GetProperty("Entries").GetValue(mt, null);

                MethodInfo createGeneric = null;
                foreach (var m in entries.GetType().GetMethods()) {
                    if (m.Name != "Create" || !m.IsGenericMethodDefinition) continue;
                    if (m.GetParameters().Length == 0) { createGeneric = m; break; }
                }
                if (createGeneric == null) { Console.WriteLine("  [AddValueMap ERR] no Entries.Create<T>()"); return false; }

                // The mapping table must be told what kind of condition its entries use BEFORE any
                // entry is created — creating an entry that doesn't match ConditionType throws.
                // Omitting this is what made the first attempt fail on every entry type.
                bool ctSet = false;
                try {
                    var ctProp = mt.GetType().GetProperty("ConditionType");
                    if (ctProp != null && ctProp.CanWrite) {
                        ctProp.SetValue(mt, Enum.Parse(ctProp.PropertyType, "Range"), null);
                        ctSet = true;
                    }
                } catch (Exception exCt) { Console.WriteLine("  [AddValueMap ERR] ConditionType=Range -> " + Root(exCt)); }

                string shapeUsed = null;
                for (int k = 0; k < codes.Length; k++) {
                    int code = codes[k];
                    if (values[k] == null) continue;
                    bool ok = false;
                    // Range first now: ConditionType is set to Range above, so a degenerate
                    // From==To range is the entry type that should match it.
                    try {
                        object e = createGeneric.MakeGenericMethod(typeof(MappingTableEntryRange)).Invoke(entries, null);
                        var re = (MappingTableEntryRange)e;
                        re.From = code; re.To = code;
                        re.Value = values[k];
                        ok = true; shapeUsed = shapeUsed ?? "Range";
                    } catch (Exception exRange) {
                        try {
                            object e = createGeneric.MakeGenericMethod(typeof(MappingTableEntrySimple)).Invoke(entries, null);
                            var se = (MappingTableEntrySimple)e;
                            se.Condition = code.ToString();
                            se.Value = values[k];
                            ok = true; shapeUsed = shapeUsed ?? "Simple";
                        } catch (Exception exSimple) {
                            Console.WriteLine("  [AddValueMap ERR] code " + code + " (ConditionType set=" + ctSet + ")");
                            Console.WriteLine("      Range  -> " + Root(exRange));
                            Console.WriteLine("      Simple -> " + Root(exSimple));
                        }
                    }
                    if (!ok) return false;
                }
                if (!s_mapShapeLogged && shapeUsed != null) {
                    Console.WriteLine("  [AddValueMap] mapping entries created using: " + shapeUsed);
                    s_mapShapeLogged = true;
                }
                return true;
            } catch (Exception ex) {
                Console.WriteLine("  [AddValueMap ERR] " + Root(ex));
                return false;
            }
        }

        // Without a mapping table a TagDynamization would bind BackColor straight to the raw
        // state number (0..5), which is not a colour — so a failed mapping must not be left in
        // place. Remove it and let the caller fall back to the proven script path.
        static void RemoveDyn(object item, string prop)
        {
            try {
                var dp = item.GetType().GetProperty("Dynamizations");
                if (dp == null) return;
                object dyns = dp.GetValue(item, null);
                if (dyns == null) return;
                var find = dyns.GetType().GetMethod("Find", new Type[] { typeof(string) });
                if (find == null) return;
                object d = find.Invoke(dyns, new object[] { prop });
                if (d == null) return;
                var del = d.GetType().GetMethod("Delete", Type.EmptyTypes);
                if (del != null) del.Invoke(d, null);
            } catch {}
        }

        // Set false the first time a native mapping fails, so the whole run falls back to scripts
        // consistently instead of producing a screen with a mix of working and broken badges.
        static bool s_nativeBadgeOk = true;

        // State-code palette/labels, shared by every native mapping (same order and meaning as
        // STATE_COLOR_LOGIC / STATE_TEXT_LOGIC, which the Bilge table's script slots still use).
        static readonly int[] STATE_CODES = { 0, 1, 2, 3, 4, 5 };
        static readonly object[] STATE_COLORS = {
            Color.FromArgb(255, 154, 163, 176), // 0 UNCONFIGURED
            Color.FromArgb(255, 205,  32,  38), // 1 FAULT
            Color.FromArgb(255, 226, 168,   0), // 2 LOCAL
            Color.FromArgb(255,   0, 158,  74), // 3 OPEN
            Color.FromArgb(255,  96, 106, 122), // 4 CLOSED
            Color.FromArgb(255,   0, 162, 255)  // 5 MOVING
        };

        // Table variant adds code 6 = "no valve in this slot on this page" (a zone whose valve
        // count isn't a multiple of 14 leaves the last page short), drawn in transparent ink so the
        // row reads as blank. 6 rather than -1: negative codes are avoided since the mapping table
        // is fussy about what it accepts, and there was no reason to risk it.
        // There is deliberately no TBL_WORDS — a mapping table is rejected on a Text property
        // ("Creation of Tag dynamization entries is not allowed for this property"), so the status
        // word is supplied ready-made by the PLC in <Zone>TblStateTxt and bound directly.
        static readonly int[] TBL_CODES = { 0, 1, 2, 3, 4, 5, 6 };
        static readonly object[] TBL_COLORS = {
            Color.FromArgb(255, 154, 163, 176),
            Color.FromArgb(255, 205,  32,  38),
            Color.FromArgb(255, 226, 168,   0),
            Color.FromArgb(255,   0, 158,  74),
            Color.FromArgb(255,  96, 106, 122),
            Color.FromArgb(255,   0, 162, 255),
            M_TRANS
        };

        // Command buttons fill only in their own state; every other state has no entry, so the
        // button keeps its own (white) background. Verify this fallback holds in runtime — if an
        // unmatched value renders wrong instead, these need all seven codes spelled out.
        static readonly int[] FILL_OPEN_CODES  = { 3 };
        static readonly object[] FILL_OPEN     = { Color.FromArgb(255, 0, 158, 74) };
        static readonly int[] FILL_CLOSE_CODES = { 4 };
        static readonly object[] FILL_CLOSE    = { Color.FromArgb(255, 205, 32, 38) };

        // Click handler for a table slot. The slot doesn't know its valve at build time — that
        // depends on the page — so it resolves it at click time from the slot's live NO. tag
        // (position within the zone) plus the zone's first valve number. This is an event, so it
        // only ever runs on tap and costs nothing during screen activation.
        static void AddSlotCmdScript(HmiButton btn, string zonePrefix, int slot, int zoneStart, bool isOpen)
        {
            try {
                PropertyInfo evProp = null;
                foreach (var p in btn.GetType().GetProperties())
                    if (p.Name == "EventHandlers") { evProp = p; break; }
                if (evProp == null) { Console.WriteLine("  [SlotCmd ERR] No EventHandlers on " + btn.GetType().Name); return; }
                object evObj = evProp.GetValue(btn, null);
                object handler = CreateTappedHandler(evObj);
                if (handler == null) { Console.WriteLine("  [SlotCmd ERR] No Tapped handler for slot " + slot); return; }
                var sp = handler.GetType().GetProperty("Script");
                object script = sp.GetValue(handler, null);
                var scp = script.GetType().GetProperty("ScriptCode");
                if (scp == null || !scp.CanWrite) return;

                string setSuf = isOpen ? "_OpenCmd" : "_CloseCmd";
                string clrSuf = isOpen ? "_CloseCmd" : "_OpenCmd";
                string js = JS_READ +
                    "var no=r(Tags(\"" + zonePrefix + "_TblNo_" + slot + "\").Read());\n" +
                    "if(!no) return;\n" +                       // empty slot on a short last page
                    "var v=" + (zoneStart - 1) + "+no;\n" +
                    "var vTag=\"V\"+(\"000\"+v).slice(-3);\n" +
                    "var cfg=r(Tags(vTag+\"_Configured\").Read());\n" +
                    "if(!cfg) return;\n" +
                    "Tags(vTag+\"" + clrSuf + "\").Write(false);\n" +
                    "Tags(vTag+\"" + setSuf + "\").Write(true);";
                scp.SetValue(script, js, null);
            } catch (Exception ex) { Console.WriteLine("  [SlotCmd ERR] " + ex.Message); }
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

        // Build-time throttle on illustration badges (each costs 3 Openness items + a dynamization
        // + 6 mapping entries, and every one is a cross-process round-trip). The valve tables, KPI
        // panels and summaries are never capped — they read PLC-precomputed totals, so they always
        // report the full section counts.
        //
        // Kept at 4 while iterating: badges dominate build time, and layout changes need fast
        // turnaround more than they need a full illustration. Raise to 99 for a final build once
        // the layout is signed off — that is a deliberate, costly choice (Screen_Home alone is 88
        // badges), so it should be an explicit decision, not a default.
        const int ILLUSTRATION_VALVES_PER_ZONE = 4;

        static HmiEllipse MakeDot(HmiScreen sc, string name, int cx, int cy, int rx, int ry,
                                   Color fill, Color border, int bw = 2)
        {
            var e = sc.ScreenItems.Create<HmiEllipse>(name);
            e.CenterX = SX(cx); e.CenterY = SY(cy);
            e.RadiusX = (uint)SX(rx); e.RadiusY = (uint)SY(ry);
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
            // Scale endpoints to real-panel space first so length/angle are computed from the
            // final on-screen geometry, not the 1920x1080 design-space one.
            x1 = SX((int)Math.Round(x1)); y1 = SY((int)Math.Round(y1));
            x2 = SX((int)Math.Round(x2)); y2 = SY((int)Math.Round(y2));
            int scaledThickness = Math.Max(1, SY(thickness));

            double dx = x2 - x1, dy = y2 - y1;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            double midX = (x1 + x2) / 2.0, midY = (y1 + y2) / 2.0;

            var r = sc.ScreenItems.Create<HmiRectangle>(name);
            r.Left = (int)Math.Round(midX - length / 2.0);
            r.Top  = (int)Math.Round(midY - scaledThickness / 2.0);
            r.Width  = (uint)Math.Round(length);
            r.Height = (uint)scaledThickness;
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
        // One read instead of five. FB_ValveLoop precomputes StateCode per valve using the exact
        // same priority chain this used to evaluate in JavaScript (Fault > Local > Open > Closed >
        // Moving), so behaviour is unchanged but Screen_Home drops from 440 tag reads per refresh
        // to 88 — and _State is a CyclicContinuous tag, so it's served from a warm cache instead
        // of a cold PLC round-trip on every screen switch.
        static string ValveStateReads(string vTag)
        {
            return JS_READ + "var st=r(Tags(\"" + vTag + "_State\").Read());\n";
        }

        // Shared state-code if-chains — factored out so both the fixed-vTag callers below (badges,
        // etc.) and the slot-based ones (BuildValveTable's virtualized rows, which compute vTag
        // from a live page tag rather than a build-time constant) use identical logic.
        // 0=UNCONFIGURED 1=FAULT 2=LOCAL 3=OPEN 4=CLOSED 5=MOVING
        const string STATE_COLOR_LOGIC =
                "if(st==0) return 0xFF9AA3B0;\n" +
                "if(st==1) return 0xFFCD2026;\n" +
                "if(st==2) return 0xFFE2A800;\n" +        // LOCAL — amber
                "if(st==3) return 0xFF009E4A;\n" +
                "if(st==4) return 0xFF606A7A;\n" +
                "return 0xFF00A2FF;";                     // MOVING/IN TRANSIT — blue
        const string STATE_TEXT_LOGIC =
                "if(st==0) return \"UNCONFIGURED\";\n" +
                "if(st==1) return \"FAULT\";\n" +
                "if(st==2) return \"LOCAL\";\n" +
                "if(st==3) return \"OPEN\";\n" +
                "if(st==4) return \"CLOSED\";\n" +
                "return \"MOVING\";";

        static string ValveStateColorScript(string vTag) { return ValveStateReads(vTag) + STATE_COLOR_LOGIC; }
        static string ValveStateTextScript(string vTag) { return ValveStateReads(vTag) + STATE_TEXT_LOGIC; }

        // (The Slot* script helpers and AddCmdScriptSlot that used to live here are gone: the table
        // now binds natively to the PLC's per-zone page window, so no HMI-side page arithmetic —
        // and therefore no runtime-built tag names — remain. See BuildValveTable.)

        static void DrawValveSym(HmiScreen sc, string name, int cx, int cy, int tagNum)
        {
            string vTag = string.Format("V{0:D3}", tagNum);
            const int R = 21; // 42px-diameter badge

            // 1. Status badge — a filled disc whose colour IS the valve state. Bound natively to
            // the precomputed _State tag with a value->colour map (no script, push-based), since
            // 88 of these on Screen_Home was the single biggest block of polled JavaScript.
            var badge = MakeDot(sc, name + "_badge", cx, cy, R, R, M_MUTED, M_BORDER, 2);
            if (s_nativeBadgeOk) {
                if (!AddValueMap(DynTag(badge, "BackColor", vTag + "_State"), STATE_CODES, STATE_COLORS)) {
                    RemoveDyn(badge, "BackColor");
                    s_nativeBadgeOk = false;
                    Console.WriteLine("  [DrawValveSym] native colour mapping unavailable — falling back to script for all badges.");
                }
            }
            if (!s_nativeBadgeOk) Dyn(badge, "BackColor", ValveStateColorScript(vTag), "AutomaticTags");

            // 2. Bowtie glyph on top of the badge — fixed white, legible against all 6 states.
            var sym = sc.ScreenItems.Create<HmiTextBox>(name + "_sym");
            sym.Left = SX(cx - R); sym.Top = SY(cy - R); sym.Width = (uint)SX(R * 2); sym.Height = (uint)SY(R * 2);
            sym.BackColor = M_TRANS; sym.ForeColor = Color.White; sym.BorderWidth = 0;
            SetProp(sym, "HorizontalTextAlignment", "Center");
            SetProp(sym, "VerticalTextAlignment", "Middle");
            SetFont(sym, SFont(20), true);
            SetText(sym, "Text", "&#x22C8;");

            // 3. Transparent button spanning badge + label area: its own Text (bottom-aligned)
            // IS the CM-nn label, and its native click opens the SBO popup — one item doing
            // both jobs instead of a separate label textbox plus a separate hit-target button.
            var hit = sc.ScreenItems.Create<HmiButton>(name + "_hit");
            hit.Left = SX(cx - 30); hit.Top = SY(cy - R); hit.Width = (uint)SX(60); hit.Height = (uint)SY(R * 2 + 18);
            hit.BackColor = M_TRANS; hit.ForeColor = M_TEXT;
            hit.BorderColor = M_TRANS; hit.BorderWidth = 0;
            SetProp(hit, "HorizontalTextAlignment", "Center");
            SetProp(hit, "VerticalTextAlignment", "Bottom");
            SetFont(hit, SFont(13), true);
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
            BuildKpiBox(sc, "AFT BALLAST",      1, 28, "Aft", tX0 + 0 * tStep, tY, tW, tH);
            BuildKpiBox(sc, "BILGE / ER",      29, 56, "Er",  tX0 + 1 * tStep, tY, tW, tH);
            BuildKpiBox(sc, "FORWARD BALLAST", 57, 88, "Fwd", tX0 + 2 * tStep, tY, tW, tH);
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
            
            // @UserName is a real WinCC Unified system tag, confirmed live in TIA Portal's own
            // tag browser (Show all + search "user") — not guessed. No obfuscation needed since
            // it's a genuine tag the compiler's static Tags() analyzer will happily validate.
            var userText = MakeLiveText(sc, "Hdr_User", 1500, 8, 400, 30, M_HDRTXT, "Right", 19, false);
            Dyn(userText, "Text",
                "var u = \"\"; try { u = Tags(\"@UserName\").Read(); } catch(e){}\n" +
                "if (!u) u = \"GUEST\";\n" +
                "return \"\\uD83D\\uDC64  USER: \" + u.toUpperCase();", "AutomaticTags");

            // Title band.
            MakeRect(sc, "Title_Rule", 0, 46, 1920, 4, M_ACCENT, M_ACCENT, 0);
            MakeTb(sc, "Title_Main", 0, 50, 1920, 54, "MV WESTERLY  &#xB7;  VALVE REMOTE CONTROL SYSTEM",
                   M_TRANS, M_TEXT, 0, "Center", 32, true);
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

        public static void BuildLoginScreen(HmiScreen sc)
        {
            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);
            BuildHomeHeader(sc);
            BuildNav(sc, "Screen_Login");

            int px = 760, py = 340, pw = 400;
            MakeRect(sc, "Panel_BG", px, py, pw, 400, M_BOX, M_BORDER, 1);
            MakeTb(sc, "Panel_Title", px, py + 50, pw, 50, "USER ACCESS CONTROL", M_TRANS, M_TEXT, 0, "Center", 24, true);
            MakeTb(sc, "Panel_Sub", px, py + 100, pw, 40, "Login to unlock restricted functions", M_TRANS, M_MUTED, 0, "Center", 16, false);

            var btnLogin = MakeBtn(sc, "Btn_Login", px + 50, py + 180, 300, 60, "LOGIN", M_ACCENT, M_BG, M_BORDER, 1, 20, true);
            AddScriptEvent(btnLogin, "HMIRuntime.UI.UserManagement.SysFct.ShowLoginDialog();\n");

            var btnLogout = MakeBtn(sc, "Btn_Logout", px + 50, py + 260, 300, 60, "LOGOUT", M_MUTED, M_BG, M_BORDER, 1, 20, true);
            AddScriptEvent(btnLogout, "HMIRuntime.UI.SysFct.LogOff();\n");
        }

        // Shared by every screen — all 7 targets now exist (Screen_Home, Screen_Bilge,
        // Screen_FwdBallast, Screen_AftBallast, Screen_Alarms, Screen_Diagnostics,
        // Screen_Login are all created by Run()), so every button is live. `activeTarget`
        // is whichever of these the calling screen IS, so its own button highlights.
        static void BuildNav(HmiScreen sc, string activeTarget)
        {
            MakeRect(sc, "Nav_BG", 0, 128, 1920, 58, M_BOX, M_LINE, 1);

            // Zone buttons run in valve-number order (AFT CM-01-28, BILGE/ER CM-29-56,
            // FWD CM-57-88), which is also stern->bow, matching the mimic's zone order.
            string[] labels  = { "&#x2302;  HOME", "&#x2693;  BALLAST AFT", "&#x1F4A7;  BILGE / ER",
                                 "&#x2693;  BALLAST FWD", "&#x1F514;  ALARMS", "&#x1F4C8;  CONFIG", "&#x1F464;  LOGIN" };
            string[] targets = { "Screen_Home", "Screen_AftBallast", "Screen_Bilge", "Screen_FwdBallast",
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
            // Valve numbers follow physical position stern->bow, so the mimic reads CM-01..CM-88
            // straight across instead of jumping 61-88, 1-28, 29-60. FB_ValveLoop uses the exact
            // same boundaries (i<=28 Aft, i<=56 Er, else Fwd) for its per-zone counters.
            int[] zoneVStart    = { 1, 29, 57 };
            int[] zoneVEnd      = { 28, 56, 88 };
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
                // Pipework follows however many valves are actually drawn, so a capped
                // illustration still looks deliberate (no manifolds running off past empty rows).
                // Uncapped this is identical to before: full rows, all 4 of them.
                int zoneCount = Math.Min(ILLUSTRATION_VALVES_PER_ZONE, zoneVEnd[z] - zoneVStart[z] + 1);
                int rowsNeeded = Math.Min(4, (zoneCount + cols - 1) / cols);

                // One horizontal manifold per row + one vertical trunk per zone — a full
                // per-valve P&ID routing would clutter badly at this density and cost far
                // more Openness items for no real gain in clarity.
                int trunkX = zoneX[z] + zoneW / 2;
                if (rowsNeeded > 1)
                    MakeRect(sc, "Pipe_Trunk" + z, trunkX - 2, rowY[0], 4, rowY[rowsNeeded - 1] - rowY[0], M_BORDER, M_BORDER, 0);

                int vNum = zoneVStart[z];
                for (int r = 0; r < rowsNeeded; r++) {
                    int inThisRow = Math.Min(cols, zoneCount - r * cols);
                    int usedW = inThisRow * cellW;
                    int rowSpanL = zoneX[z] + (zoneW - usedW) / 2;
                    MakeRect(sc, "Pipe_Row" + z + "_" + r, rowSpanL, rowY[r] - 2, usedW, 4, M_BORDER, M_BORDER, 0);

                    for (int c = 0; c < inThisRow; c++) {
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
                        (zoneVEnd[z] - zoneVStart[z] + 1) + " CONFIGURED\";", "AutomaticTags");
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
                    // Pure passthrough of an Int — bind straight to the tag, no script needed.
                    var val = MakeLiveText(sc, "KPI_Val_" + vStart + "_" + i, x + w - 78, rY, 68, rowH, cols[i], "Right", 22, true);
                    DynTag(val, "Text", "Valves_DB_" + zonePrefix + tagSuf[i]);
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
            DynTag(cfgVal, "Text", "Valves_DB_TotalConfigured");
            MakeRect(sc, "Pls_Sep1", x + 10, rY0 + rowH * 2 - 1, w - 20, 1, M_LINE, M_LINE, 0);

            // LOCAL MODE reads Valves_DB_TotalLocal directly instead of looping — FB_ValveLoop
            // already computes this every scan (temp_fb_valveloop.xml) and the HMI tag for it
            // (CreateSummaryHmiTags) existed but was unused. One tag read replaces 88.
            MakeTb(sc, "Pls_Lbl2", x + 14, rY0 + rowH * 2, w - 116, rowH, "LOCAL MODE", M_TRANS, M_MUTED, 0, "Left", 15, false);
            var locVal = MakeLiveText(sc, "Pls_Val2", x + w - 78, rY0 + rowH * 2, 68, rowH, M_YELLOW, "Right", 22, true);
            DynTag(locVal, "Text", "Valves_DB_TotalLocal");
        }

        static void BuildAlarmPanel(HmiScreen sc, int x, int y, int w, int h)
        {
            MakePanel(sc, "Alm_BG", x, y, w, h, M_BOX, M_BORDER, 1);
            MakeRect(sc, "Alm_Hdr", x, y, w, 36, M_HDR, M_HDR, 0);
            MakeTb(sc, "Alm_Ttl", x + 10, y + 5, w - 20, 26, "ACTIVE ALARMS", M_TRANS, M_HDRTXT, 0, "Left", 17, true);

            // The count is a straight passthrough, so it binds natively.
            var big = MakeLiveText(sc, "Alm_Count", x + 14, y + 42, 90, h - 92, M_RED, "Center", 50, true);
            DynTag(big, "Text", "Valves_DB_TotalFault");

            // The state word/colour stay on scripts. A fault total is 0..88, so expressing
            // "zero vs non-zero" as a discrete value map would need ~89 entries per property —
            // i.e. ~178 extra Openness round-trips at build time to remove just two scripts.
            // Not worth it; the win from native binding is in the 88 badges and 15 KPI cells.
            string readFaultTotal = JS_READ + "var n=r(Tags(\"Valves_DB_TotalFault\").Read());\n";
            var state = MakeLiveText(sc, "Alm_State", x + 112, y + 48, w - 126, 26, M_GREEN, "Left", 18, true);
            Dyn(state, "Text", readFaultTotal + "return n>0?\"ALARM ACTIVE\":\"ALL NORMAL\";", "AutomaticTags");
            Dyn(state, "ForeColor", readFaultTotal + "return n>0?0xFFCD2026:0xFF009E4A;", "AutomaticTags");

            MakeTb(sc, "Alm_Hint", x + 112, y + 78, w - 126, 22, "Tap to open annunciator", M_TRANS, M_MUTED, 0, "Left", 13, false);

            var goAlarms = MakeBtn(sc, "Alm_Btn", x + 14, y + h - 42, w - 28, 34, "&#x1F514;  OPEN ALARMS",
                                    M_HDR, M_HDRTXT, M_BORDER, 1, 15, true);
            AddNavClick(goAlarms, "Screen_Alarms");
        }

        // ── Zone screen (AFT / BILGE-ER / FWD): illustration + paged table + summary ──
        // One builder for all three zones — they differ only in valve range, page count and which
        // PLC window prefix (Aft/Er/Fwd) their table reads.
        static void BuildZoneScreen(HmiScreen sc, string screenTarget, string zoneLabel,
                                     int vStart, int vEnd, string zonePrefix, int mimicCols)
        {
            int count = vEnd - vStart + 1;
            int maxPage = (count + 13) / 14 - 1;   // 0-based index of the last page
            Console.WriteLine("  Drawing " + screenTarget + " (" + zoneLabel + ": " + count +
                              " valves, " + (maxPage + 1) + " page(s))...");

            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);
            BuildHomeHeader(sc);
            BuildNav(sc, screenTarget);

            // Illustration stops 24px short of the table/summary row so the two panels read as
            // separate boxes — at 484 they shared an edge and looked fused together.
            // 198 + 460 = 658, then a 24px gap, then the table row at 682 .. 1062 (Home's bottom).
            BuildZoneMimic(sc, 16, 198, 1888, 460, vStart, vEnd, mimicCols, 2, zoneLabel);
            // Table takes width back off the summary (1650 -> 1674): the summary only has to fit
            // six short label/number rows, whereas the table has six columns fighting for room.
            BuildValveTable(sc, 16, 682, 1674, 380, zonePrefix, vStart, maxPage);

            // Summary sits beside the table, sharing its top edge, and now runs the full width left
            // over to the right margin (1680..1904) instead of stopping at 1880 and leaving a dead
            // strip. Its height reaches down to just above the page buttons for the same reason —
            // 260 left an odd blank gap in the middle of the column.
            // Short title: BuildKpiBox gives the title only (w-108)px, so the full zone name would
            // clip; the zone is already named on the illustration header directly above.
            // 200 wide, not narrower: BuildKpiBox gives its title only (w-108)px, and "SUMMARY" at
            // font 17 needs ~75 of the 92 that leaves — any narrower and the title starts clipping.
            const int sumX = 1704, sumW = 200;      // 1704 + 200 = 1904 = 1920 - 16px margin
            BuildKpiBox(sc, "SUMMARY", vStart, vEnd, zonePrefix, sumX, 682, sumW, 320);

            // Page UP/DOWN — below the summary, matching its width so the column lines up.
            // These write the PLC's per-zone page tag; the PLC then reloads the table window.
            string pageTag = "Valves_DB_" + zonePrefix + "Page";
            int pbY = 1016;                          // 682 + 320 = 1002, 14px gap, 1016 + 46 = 1062
            const int pbW = 95;                      // 95 + 10 gap + 95 = 200, same as the summary
            var upBtn   = MakeBtn(sc, "Zn_PageUp",   sumX, pbY, pbW, 46, "&#x25B2; UP",   M_HDR, M_HDRTXT, M_BORDER, 1, 14, true);
            var downBtn = MakeBtn(sc, "Zn_PageDown", sumX + pbW + 10, pbY, pbW, 46, "&#x25BC; DN", M_HDR, M_HDRTXT, M_BORDER, 1, 14, true);
            AddPageNavScript(upBtn, pageTag, -1, maxPage);
            AddPageNavScript(downBtn, pageTag, 1, maxPage);
            // Dim each button at its boundary (first page for UP, last for DOWN) — purely visual;
            // the click script's own clamp is what actually prevents going out of range. Native
            // bindings: one entry marks the boundary page, every other page falls back to the
            // button's own colours.
            AddValueMap(DynTag(upBtn, "BackColor", pageTag), new int[] { 0 }, new object[] { Color.FromArgb(255, 58, 67, 86) });
            AddValueMap(DynTag(upBtn, "ForeColor", pageTag), new int[] { 0 }, new object[] { Color.FromArgb(255, 122, 132, 148) });
            AddValueMap(DynTag(downBtn, "BackColor", pageTag), new int[] { maxPage }, new object[] { Color.FromArgb(255, 58, 67, 86) });
            AddValueMap(DynTag(downBtn, "ForeColor", pageTag), new int[] { maxPage }, new object[] { Color.FromArgb(255, 122, 132, 148) });
        }

        // Same CreateTappedHandler pattern as AddCmdScript/AddNavClick — writes a clamped page
        // index to the zone's PLC page tag (Valves_DB_<Zone>Page), which the PLC then uses to
        // reload that zone's 14-slot table window.
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
                   zoneLabel + " &#x2014; " + (vEnd - vStart + 1) + " VALVES", M_TRANS, M_HDRTXT, 0, "Left", 15, true);

            int hullL = px + 24, hullT = py + 44, hullR = px + pw - 24, hullB = py + ph - 16;
            MakeRect(sc, "ZHull_Top",   hullL,     hullT,     hullR - hullL, 4,             M_BORDER, M_BORDER, 0);
            MakeRect(sc, "ZHull_Bot",   hullL,     hullB - 4, hullR - hullL, 4,             M_BORDER, M_BORDER, 0);
            MakeRect(sc, "ZHull_Left",  hullL,     hullT,     4,             hullB - hullT, M_BORDER, M_BORDER, 0);
            MakeRect(sc, "ZHull_Right", hullR - 4, hullT,     4,             hullB - hullT, M_BORDER, M_BORDER, 0);

            int gridT = hullT + 20, gridB = hullB - 20, gridL = hullL + 20, gridR = hullR - 20;
            int rowPitch = (gridB - gridT) / rows;
            int colPitch = (gridR - gridL) / cols;

            // Manifolds follow the valves actually drawn (see ILLUSTRATION_VALVES_PER_ZONE) and the
            // group is centred, so a capped illustration still looks deliberate rather than like a
            // half-empty grid. Uncapped this is identical to before: full-width rows.
            int zoneCount = Math.Min(ILLUSTRATION_VALVES_PER_ZONE, vEnd - vStart + 1);
            int rowsNeeded = Math.Min(rows, (zoneCount + cols - 1) / cols);

            int vNum = vStart;
            for (int r = 0; r < rowsNeeded; r++) {
                int ry = gridT + rowPitch * r + rowPitch / 2;
                int inThisRow = Math.Min(cols, zoneCount - r * cols);
                int usedW = inThisRow * colPitch;
                int rowL = gridL + ((gridR - gridL) - usedW) / 2;
                MakeRect(sc, "ZPipe_Row" + r, rowL, ry - 2, usedW, 4, M_BORDER, M_BORDER, 0);

                for (int c = 0; c < inThisRow; c++) {
                    int cx = rowL + colPitch * c + colPitch / 2;
                    DrawValveSym(sc, "ZVlv_" + r + "_" + c, cx, ry, vNum);
                    vNum++;
                }
            }
        }

        // Paged valve table: NO. | TAG | NAME | LOCATION | STATUS | OPEN | CLOSE, in 2 columns of
        // 7 = 14 fixed slots. Every cell binds NATIVELY (TagDynamization) to the PLC's per-zone
        // page window — slot 3 always reads Er_TblState_3, a constant name, while the PLC shifts
        // what that slot contains as the page changes (see FB_ValveLoop's window loops).
        //
        // The previous version computed each slot's tag name in JavaScript from a page counter,
        // which meant ~126 polled scripts per screen; that is what made Screen_Bilge slow to load,
        // and a TagDynamization cannot express a name that is only known at runtime. Moving the
        // paging arithmetic into the PLC removes the indirection entirely.
        //
        // TblState == -1 means "no valve in this slot on this page" (the last page of a zone whose
        // valve count isn't a multiple of 14), rendered blank. 0 still means UNCONFIGURED.
        // zonePrefix is Aft/Er/Fwd; maxPage is the last 0-based page index for this zone.
        static void BuildValveTable(HmiScreen sc, int px, int py, int pw, int ph,
                                     string zonePrefix, int zoneStart, int maxPage)
        {
            MakePanel(sc, "Tbl_BG", px, py, pw, ph, M_BOX, M_BORDER, 1);
            MakeRect(sc, "Tbl_Hdr", px, py, pw, 38, M_HDR, M_HDR, 0);
            MakeTb(sc, "Tbl_Ttl", px + 14, py + 6, pw - 28, 26, "VALVE LIST", M_TRANS, M_HDRTXT, 0, "Left", 18, true);

            const int cols = 2;
            const int rowsPerCol = 7;
            int colGutter = 24;                         // room for the divider between the halves
            int colW = (pw - 32 - colGutter) / cols;     // 16px side margins inside the panel
            const int colHdrH = 30;
            int rowH = (ph - 38 - colHdrH - 10) / rowsPerCol;

            // Field widths within one ~colW-wide column, with `pad` breathing room between each so
            // STATUS doesn't run into the command buttons. The status dot was removed (the status
            // word is already colour-coded by the same state, so the dot only repeated it).
            // Budget at pw=1674: colW=809; fields 34+72+182+156+112+200 = 756, plus 5 pads = 806,
            // leaving 3px slack. Widths are set against worst-case content so nothing clips:
            //   NO.      34  "28"                    ~20px
            //   TAG      72  "CM-29" @15 bold        ~48px   (was 64 and clipped the last digit)
            //   NAME    182  20 chars @13            ~143px  (Valve_Meta_DB is String[20])
            //   LOCATION156  20 chars @13            ~143px
            //   STATUS  112  "UNCONFIGURED" @14 bold ~101px  (longest state word)
            //   COMMAND 200  two 96px buttons + 8 gap
            const int noW = 34, tagW = 72, nameW = 182, locW = 156, statusW = 112,
                      btnW = 96, btnGap = 8, pad = 10;

            int bodyTop = py + 38 + 2 + colHdrH + 4;

            // Header band behind the column labels, so they read as a header rather than as
            // another data row. Drawn before the divider and labels so it sits underneath both.
            MakeRect(sc, "Tbl_HdrBand", px + 1, py + 38, pw - 2, colHdrH + 6, M_HDRBAND, M_HDRBAND, 0);

            // Divider between the two halves — the left column's COMMAND buttons otherwise sit
            // hard against the right column's NO., making them read as one run of cells.
            int divX = px + 16 + colW + colGutter / 2;
            MakeRect(sc, "Tbl_Div", divX, py + 42, 2, ph - 48, M_ACCENT, M_ACCENT, 0);
            for (int col = 0; col < cols; col++) {
                int cx0 = px + 16 + col * (colW + colGutter);
                int hy = py + 38 + 2;
                int hxTag  = cx0 + noW + pad;
                int hxName = hxTag + tagW + pad;
                int hxLoc  = hxName + nameW + pad;
                int hxSt   = hxLoc + locW + pad;
                int hxCmd  = hxSt + statusW + pad;

                MakeTb(sc, "Tbl_H_No"   + col, cx0,    hy, noW,               colHdrH, "NO.",      M_TRANS, M_MUTED, 0, "Center", 14, true);
                MakeTb(sc, "Tbl_H_Tag"  + col, hxTag,  hy, tagW,              colHdrH, "TAG",      M_TRANS, M_MUTED, 0, "Center", 14, true);
                MakeTb(sc, "Tbl_H_Name" + col, hxName, hy, nameW,             colHdrH, "NAME",     M_TRANS, M_MUTED, 0, "Center", 14, true);
                MakeTb(sc, "Tbl_H_Loc"  + col, hxLoc,  hy, locW,              colHdrH, "LOCATION", M_TRANS, M_MUTED, 0, "Center", 14, true);
                MakeTb(sc, "Tbl_H_St"   + col, hxSt,   hy, statusW,           colHdrH, "STATUS",   M_TRANS, M_MUTED, 0, "Center", 14, true);
                MakeTb(sc, "Tbl_H_Cmd"  + col, hxCmd,  hy, btnW * 2 + btnGap, colHdrH, "COMMAND",  M_TRANS, M_MUTED, 0, "Center", 14, true);

                for (int r = 0; r < rowsPerCol; r++) {
                    // 1-based slot index into the PLC's 14-entry window for this zone.
                    int slot = col * rowsPerCol + r + 1;
                    int rY = bodyTop + r * rowH;
                    string sfx = "_" + col + "_" + r;
                    string stateTag = zonePrefix + "_TblState_" + slot;

                    // Alternating row tint, drawn first so every cell in the row sits on top of it.
                    // This replaces the old hairline rule between rows — across a row this wide a
                    // tint band tracks far better than a 1px line, and it's a plain rect with no
                    // dynamization, so it costs nothing at runtime.
                    if (r % 2 == 1)
                        MakeRect(sc, "Tr_Zeb" + sfx, cx0 - 6, rY, colW + 6, rowH, M_ZEBRA, M_ZEBRA, 0);

                    // NO. is the row's position within the zone (1..28); TAG is the CM-nn label,
                    // preformatted by the PLC so no string work is needed here.
                    var noTb = MakeTb(sc, "Tr_No" + sfx, cx0, rY, noW, rowH, "", M_TRANS, M_MUTED, 0, "Center", 15, false);
                    DynTag(noTb, "Text", zonePrefix + "_TblNo_" + slot);
                    var tagTb = MakeTb(sc, "Tr_Tag" + sfx, cx0 + noW, rY, tagW, rowH, "", M_TRANS, M_TEXT, 0, "Center", 15, true);
                    DynTag(tagTb, "Text", zonePrefix + "_TblTag_" + slot);

                    // NAME/LOCATION centred so the cells line up under their headers.
                    var nameVal = MakeLiveText(sc, "Tr_Name" + sfx, hxName, rY, nameW, rowH, M_TEXT, "Center", 13, false);
                    DynTag(nameVal, "Text", zonePrefix + "_TblName_" + slot);

                    var locVal = MakeLiveText(sc, "Tr_Loc" + sfx, hxLoc, rY, locW, rowH, M_MUTED, "Center", 13, false);
                    DynTag(locVal, "Text", zonePrefix + "_TblLoc_" + slot);

                    // Word comes ready-made from the PLC (direct bind); only its colour needs the
                    // value map, which is allowed on colour properties but not on Text.
                    var stVal = MakeLiveText(sc, "Tr_St" + sfx, hxSt, rY, statusW, rowH, M_MUTED, "Center", 14, true);
                    DynTag(stVal, "Text", zonePrefix + "_TblStateTxt_" + slot);
                    AddValueMap(DynTag(stVal, "ForeColor", stateTag), TBL_CODES, TBL_COLORS);

                    int btnX = hxCmd;
                    var openBtn  = MakeBtn(sc, "Tr_Open"  + sfx, btnX,                rY + 2, btnW, rowH - 4, "OPEN",  M_BOX, M_GREEN, M_GREEN, 1, 15, true);
                    var closeBtn = MakeBtn(sc, "Tr_Close" + sfx, btnX + btnW + btnGap, rY + 2, btnW, rowH - 4, "CLOSE", M_BOX, M_RED,   M_RED,   1, 15, true);
                    // Fill only in the matching state; every other state falls back to the
                    // button's own white background (see FILL_OPEN/FILL_CLOSE).
                    AddValueMap(DynTag(openBtn,  "BackColor", stateTag), FILL_OPEN_CODES,  FILL_OPEN);
                    AddValueMap(DynTag(closeBtn, "BackColor", stateTag), FILL_CLOSE_CODES, FILL_CLOSE);
                    // Commands stay scripts, but these are click *events* — they run on tap, never
                    // on screen activation, so they cost nothing at load time. The valve is
                    // resolved at click time from the slot's live NO. tag.
                    AddSlotCmdScript(openBtn,  zonePrefix, slot, zoneStart, true);
                    AddSlotCmdScript(closeBtn, zonePrefix, slot, zoneStart, false);
                }
            }
        }

        // ── CONFIGURATION SCREEN — all 88 valves, one global paged table ───────────────────
        // Replaces the old Screen_Diagnostics placeholder. Shows every valve's Zone/Name/Location/
        // live Status plus a Configured on/off toggle - the same flag that gates whether
        // FB_ValveLoop runs a slot's control logic at all (the pre-allocated-UDT-pool "enable"
        // mechanism from the original spec). Single-column, full-width rows (v2 rework) - the
        // original 2-column layout wasted vertical space (~103px design-space rows for content
        // that needs ~44) and made NAME/LOCATION cramped; this fits 16 rows/page (6 pages total,
        // down from 7) with more room per field. Name/Location edit happens in a separate popup
        // (Screen_ValveEdit), not inline - the table's cells are PLC-mirrored display tags that
        // FB_ValveLoop overwrites every scan, so binding an editable field directly to them would
        // race against the PLC's own re-mirroring and could lose the edit before it's ever read.
        const int CFG_ROWS_PER_PAGE = 16;
        const int CFG_MAX_PAGE = 5; // 88 valves / 16 per page - 1, 0-based

        static void BuildConfigScreen(HmiScreen sc)
        {
            Console.WriteLine("  Drawing Screen_Diagnostics as VALVE CONFIGURATION (88 valves, 6 pages)...");
            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);
            BuildHomeHeader(sc);
            BuildNav(sc, "Screen_Diagnostics");

            const int px = 16, py = 198, pw = 1888, ph = 760;
            BuildConfigTable(sc, px, py, pw, ph);

            // ── Control bar row 1: page nav, live PAGE label, GO TO VALVE# jump, summary count ──
            const int pbW = 95, pbY = py + ph + 12;
            var upBtn   = MakeBtn(sc, "Cfg_PageUp",   px,            pbY, pbW, 42, "&#x25B2; UP",   M_HDR, M_HDRTXT, M_BORDER, 1, 14, true);
            var downBtn = MakeBtn(sc, "Cfg_PageDown", px + pbW + 8,  pbY, pbW, 42, "&#x25BC; DN",   M_HDR, M_HDRTXT, M_BORDER, 1, 14, true);
            AddPageNavScript(upBtn, "Valves_DB_CfgPage", -1, CFG_MAX_PAGE);
            AddPageNavScript(downBtn, "Valves_DB_CfgPage", 1, CFG_MAX_PAGE);
            AddValueMap(DynTag(upBtn, "BackColor", "Valves_DB_CfgPage"), new int[] { 0 }, new object[] { Color.FromArgb(255, 58, 67, 86) });
            AddValueMap(DynTag(upBtn, "ForeColor", "Valves_DB_CfgPage"), new int[] { 0 }, new object[] { Color.FromArgb(255, 122, 132, 148) });
            AddValueMap(DynTag(downBtn, "BackColor", "Valves_DB_CfgPage"), new int[] { CFG_MAX_PAGE }, new object[] { Color.FromArgb(255, 58, 67, 86) });
            AddValueMap(DynTag(downBtn, "ForeColor", "Valves_DB_CfgPage"), new int[] { CFG_MAX_PAGE }, new object[] { Color.FromArgb(255, 122, 132, 148) });

            int xAfterPage = px + 2 * pbW + 8 + 20;
            var pageLbl = MakeLiveText(sc, "Cfg_PageLbl", xAfterPage, pbY + 10, 130, 22, M_MUTED, "Left", 14, false);
            Dyn(pageLbl, "Text",
                JS_READ + "let p=r(Tags(\"Valves_DB_CfgPage\").Read())||0;\nreturn \"PAGE \" + (p+1) + \" / 6\";",
                "AutomaticTags");

            int xJump = xAfterPage + 140;
            MakeTb(sc, "Cfg_JumpLbl", xJump, pbY + 10, 100, 22, "GO TO VALVE#", M_TRANS, M_MUTED, 0, "Left", 13, false);
            var jumpField = sc.ScreenItems.Create<HmiIOField>("Cfg_JumpInput");
            jumpField.Left = SX(xJump + 104); jumpField.Top = SY(pbY); jumpField.Width = (uint)SX(60); jumpField.Height = (uint)SY(42);
            jumpField.BackColor = Color.White; jumpField.ForeColor = M_TEXT; jumpField.BorderColor = M_BORDER; jumpField.BorderWidth = 1;
            SetPropEnum(jumpField, "IOFieldType", "InputOutput");
            SetPropEnum(jumpField, "TextHorizontalAlignment", "Center");
            DynTag(jumpField, "ProcessValue", "CfgJumpTarget");

            var goBtn = MakeBtn(sc, "Cfg_JumpGo", xJump + 104 + 66, pbY, 60, 42, "GO", M_ACCENT, Color.White, M_ACCENT, 1, 14, true);
            AddScriptEvent(goBtn,
                JS_READ +
                "let t=r(Tags(\"CfgJumpTarget\").Read())||1;\n" +
                "if(t<1)t=1; if(t>88)t=88;\n" +
                "let pg=Math.floor((t-1)/" + CFG_ROWS_PER_PAGE + ");\n" +
                "if(pg<0)pg=0; if(pg>" + CFG_MAX_PAGE + ")pg=" + CFG_MAX_PAGE + ";\n" +
                "Tags(\"Valves_DB_CfgPage\").Write(pg);");

            int xSummary = xJump + 104 + 66 + 60 + 30;
            var sumVal = MakeLiveText(sc, "Cfg_SummaryVal", xSummary, pbY + 10, 40, 22, M_GREEN, "Right", 15, true);
            DynTag(sumVal, "Text", "Valves_DB_TotalConfigured");
            MakeTb(sc, "Cfg_SummaryLbl", xSummary + 44, pbY + 10, 160, 22, "/ 88 CONFIGURED", M_TRANS, M_MUTED, 0, "Left", 13, false);

            // ── Control bar row 2: bulk configure per zone ──
            int pbY2 = pbY + 50;
            MakeTb(sc, "Cfg_BulkLbl", px, pbY2 + 4, 140, 26, "CONFIGURE ALL:", M_TRANS, M_MUTED, 0, "Left", 13, false);
            int bulkX = px + 150;
            var aftBtn = MakeBtn(sc, "Cfg_BulkAft", bulkX, pbY2, 150, 34, "AFT BALLAST", M_HDR, M_HDRTXT, M_BORDER, 1, 13, true);
            SetStr(aftBtn, "Authorization", "Operate");
            AddScriptEvent(aftBtn, ZoneConfigureAllScript(1, 28));

            var bilgeBtn = MakeBtn(sc, "Cfg_BulkBilge", bulkX + 160, pbY2, 150, 34, "BILGE / ER", M_HDR, M_HDRTXT, M_BORDER, 1, 13, true);
            SetStr(bilgeBtn, "Authorization", "Operate");
            AddScriptEvent(bilgeBtn, ZoneConfigureAllScript(29, 56));

            var fwdBtn = MakeBtn(sc, "Cfg_BulkFwd", bulkX + 320, pbY2, 150, 34, "BALLAST FWD", M_HDR, M_HDRTXT, M_BORDER, 1, 13, true);
            SetStr(fwdBtn, "Authorization", "Operate");
            AddScriptEvent(fwdBtn, ZoneConfigureAllScript(57, 88));
        }

        // One-time cost on tap only (not recurring per-scan work) - loops the zone's fixed valve
        // range writing Configured=true + Healthy=true (matching the popup's SERVICE-ON behaviour)
        // for every valve in it. Only "configure all ON" - mass-disabling a zone is a much riskier
        // bulk action than mass-enabling, so it's left to the per-row toggle (which itself confirms
        // before disabling an active valve) rather than offered as a one-tap bulk option.
        static string ZoneConfigureAllScript(int start, int end)
        {
            return
                "for (let i = " + start + "; i <= " + end + "; i++) {\n" +
                "  let v = \"V\" + (\"000\" + i).slice(-3);\n" +
                "  Tags(v + \"_Configured\").Write(true);\n" +
                "  Tags(v + \"_Healthy\").Write(true);\n" +
                "}";
        }

        static void BuildConfigTable(HmiScreen sc, int px, int py, int pw, int ph)
        {
            MakePanel(sc, "CfgTbl_BG", px, py, pw, ph, M_BOX, M_BORDER, 1);
            MakeRect(sc, "CfgTbl_Hdr", px, py, pw, 38, M_HDR, M_HDR, 0);
            MakeTb(sc, "CfgTbl_Ttl", px + 14, py + 6, pw - 28, 26, "VALVE CONFIGURATION &#x2014; ALL 88 VALVES  (tap a row to edit name/location)", M_TRANS, M_HDRTXT, 0, "Left", 16, true);

            const int colHdrH = 28;
            int rowH = (ph - 38 - colHdrH - 8) / CFG_ROWS_PER_PAGE;

            // Full-width single column - field widths chosen against the ~1856px usable content
            // width (pw - 32 side margins), with room to spare rather than the old 2-column split
            // which forced everything narrow.
            const int tagW = 90, zoneW = 160, nameW = 340, locW = 320, statusW = 160, cfgW = 180, pad = 16;

            int bodyTop = py + 38 + 2 + colHdrH + 4;
            MakeRect(sc, "CfgTbl_HdrBand", px + 1, py + 38, pw - 2, colHdrH + 6, M_HDRBAND, M_HDRBAND, 0);

            int cx0 = px + 16;
            int hy = py + 38 + 2;
            int hxZone = cx0 + tagW + pad;
            int hxName = hxZone + zoneW + pad;
            int hxLoc = hxName + nameW + pad;
            int hxStatus = hxLoc + locW + pad;
            int hxCfg = hxStatus + statusW + pad;

            MakeTb(sc, "CfgTbl_H_Tag",    cx0,      hy, tagW,    colHdrH, "TAG",         M_TRANS, M_MUTED, 0, "Center", 13, true);
            MakeTb(sc, "CfgTbl_H_Zone",   hxZone,   hy, zoneW,   colHdrH, "ZONE",        M_TRANS, M_MUTED, 0, "Center", 13, true);
            MakeTb(sc, "CfgTbl_H_Name",   hxName,   hy, nameW,   colHdrH, "NAME",        M_TRANS, M_MUTED, 0, "Center", 13, true);
            MakeTb(sc, "CfgTbl_H_Loc",    hxLoc,    hy, locW,    colHdrH, "LOCATION",    M_TRANS, M_MUTED, 0, "Center", 13, true);
            MakeTb(sc, "CfgTbl_H_Status", hxStatus, hy, statusW, colHdrH, "STATUS",      M_TRANS, M_MUTED, 0, "Center", 13, true);
            MakeTb(sc, "CfgTbl_H_Cfg",    hxCfg,    hy, cfgW,    colHdrH, "CONFIGURED",  M_TRANS, M_MUTED, 0, "Center", 13, true);

            int rowW = (hxCfg - 6) - (cx0 - 6); // hit-area + zebra span TAG..STATUS, stopping short of the toggle column

            for (int r = 0; r < CFG_ROWS_PER_PAGE; r++) {
                int slot = r + 1;
                int rY = bodyTop + r * rowH;
                string sfx = "_" + r;

                if (r % 2 == 1)
                    MakeRect(sc, "CfgTr_Zeb" + sfx, cx0 - 6, rY, rowW, rowH, M_ZEBRA, M_ZEBRA, 0);

                var tagTb = MakeTb(sc, "CfgTr_Tag" + sfx, cx0, rY, tagW, rowH, "", M_TRANS, M_TEXT, 0, "Center", 14, true);
                DynTag(tagTb, "Text", "Cfg_TblTag_" + slot);
                var zoneTb = MakeLiveText(sc, "CfgTr_Zone" + sfx, hxZone, rY, zoneW, rowH, M_MUTED, "Center", 12, false);
                DynTag(zoneTb, "Text", "Cfg_TblZone_" + slot);
                var nameVal = MakeLiveText(sc, "CfgTr_Name" + sfx, hxName, rY, nameW, rowH, M_TEXT, "Center", 13, false);
                DynTag(nameVal, "Text", "Cfg_TblName_" + slot);
                var locVal = MakeLiveText(sc, "CfgTr_Loc" + sfx, hxLoc, rY, locW, rowH, M_MUTED, "Center", 13, false);
                DynTag(locVal, "Text", "Cfg_TblLoc_" + slot);
                var statusVal = MakeLiveText(sc, "CfgTr_Status" + sfx, hxStatus, rY, statusW, rowH, M_MUTED, "Center", 13, true);
                DynTag(statusVal, "Text", "Cfg_TblStateTxt_" + slot);
                AddValueMap(DynTag(statusVal, "ForeColor", "Cfg_TblState_" + slot), TBL_CODES, TBL_COLORS);

                // Row-tap hit area, created LAST so it sits on top of the (non-interactive) text
                // above it and reliably captures the tap - same pattern DrawValveSym's own "hit"
                // button uses over its badge/glyph. Stops short of the CONFIGURED column so it
                // never competes with that button's own tap.
                var hit = MakeBtn(sc, "CfgTr_Hit" + sfx, cx0 - 6, rY, rowW, rowH, "", M_TRANS, M_TRANS, M_TRANS, 0, 10, false);
                AddScriptEvent(hit, ConfigRowTapScript(slot));

                var cfgBtn = MakeBtn(sc, "CfgTr_Toggle" + sfx, hxCfg, rY + 2, cfgW, rowH - 4, "", M_HDR, M_HDRTXT, M_BORDER, 1, 13, true);
                SetStr(cfgBtn, "Authorization", "Operate");
                AddConfigToggleTextAndColor(cfgBtn, slot);
                AddScriptEvent(cfgBtn, ConfigToggleScript(slot));
            }
        }

        // Text/colour follow the row's live Configured tag - same script-driven approach as the
        // popup's SERVICE button (a bool can't use AddValueMap's Range/Simple int-keyed mapping).
        static void AddConfigToggleTextAndColor(HmiButton btn, int slot)
        {
            string tagName = "Cfg_TblConfigured_" + slot;
            Dyn(btn, "Text",
                JS_READ + "let cfg=r(Tags(\"" + tagName + "\").Read());\nreturn cfg ? \"\\u2713 CONFIGURED\" : \"DISABLED\";",
                "AutomaticTags");
            Dyn(btn, "BackColor",
                JS_READ + "let cfg=r(Tags(\"" + tagName + "\").Read());\nreturn cfg ? 0xFF00C7BE : 0xFF3A3A3C;",
                "AutomaticTags");
        }

        // Tapping a row opens the Edit Valve popup - reads the tapped row's live NO. tag (so it's
        // always correct regardless of which page is showing) rather than anything baked in at
        // build time, sets SelectedValve, pre-fills the edit buffers from that valve's OWN Name/
        // Location tags (not the table's PLC-mirrored display tags, which FB_ValveLoop overwrites
        // every scan), then opens the popup.
        static string ConfigRowTapScript(int slot)
        {
            return JS_READ +
                PopupOpenGuardJs() +
                "let no=r(Tags(\"Cfg_TblNo_" + slot + "\").Read());\n" +
                "if(!no) return;\n" +
                "Tags(\"SelectedValve\").Write(no);\n" +
                "let vTag=\"V\"+(\"000\"+no).slice(-3);\n" +
                "Tags(\"EditNameBuffer\").Write(r(Tags(vTag+\"_Name\").Read())||\"\");\n" +
                "Tags(\"EditLocBuffer\").Write(r(Tags(vTag+\"_Location\").Read())||\"\");\n" +
                PopupMarkOpenJs() +
                "HMIRuntime.UI.SysFct.OpenScreenInPopup(\"Popup_ValveEdit\", \"Screen_ValveEdit\", false, \" \", " + SX(760) + ", " + SY(400) + ", false);";
        }

        // Resolves the absolute valve number from the row's live NO. tag at click time (same
        // pattern as AddSlotCmdScript). Turning a valve ON writes Configured+Healthy immediately,
        // same as the popup's SERVICE toggle. Turning OFF checks the row's own live STATUS first -
        // if the valve is currently OPEN(3) or MOVING(5), it doesn't write immediately; it stashes
        // the valve number and opens a confirm popup instead, since disabling control logic on a
        // valve mid-operation seems like it should need a second thought.
        static string ConfigToggleScript(int slot)
        {
            return JS_READ +
                "let no=r(Tags(\"Cfg_TblNo_" + slot + "\").Read());\n" +
                "if(!no) return;\n" +
                "let vTag=\"V\"+(\"000\"+no).slice(-3);\n" +
                "let cur=r(Tags(vTag+\"_Configured\").Read());\n" +
                "if(!cur) {\n" +
                "  Tags(vTag+\"_Configured\").Write(true);\n" +
                "  Tags(vTag+\"_Healthy\").Write(true);\n" +
                "  return;\n" +
                "}\n" +
                "let st=r(Tags(\"Cfg_TblState_" + slot + "\").Read());\n" +
                "if(st===3||st===5) {\n" +
                "  if(r(Tags(\"AnyPopupOpen\").Read())) return;\n" +
                "  Tags(\"ConfirmValveIdx\").Write(no);\n" +
                "  Tags(\"AnyPopupOpen\").Write(true);\n" +
                "  HMIRuntime.UI.SysFct.OpenScreenInPopup(\"Popup_ConfirmDisable\", \"Screen_ConfirmDisable\", false, \" \", " + SX(730) + ", " + SY(430) + ", false);\n" +
                "  return;\n" +
                "}\n" +
                "Tags(vTag+\"_Configured\").Write(false);";
        }

        // ── EDIT VALVE popup — Name/Location, opened by tapping a Configuration screen row ──────
        // Fields bind to plain internal buffer tags (EditNameBuffer/EditLocBuffer), pre-filled by
        // the row's tap script and written through to the real per-valve tags only on SAVE - this
        // sidesteps the paged-mirror race condition entirely (see BuildConfigScreen comment above).
        static void BuildValveEditScreen(HmiScreen sc)
        {
            Console.WriteLine("  Drawing Screen_ValveEdit...");
            SetPropUInt(sc, "Width", (uint)SX(400));
            SetPropUInt(sc, "Height", (uint)SY(280));
            sc.BackColor = BG_DARK;

            MakeRect(sc, "Edit_Header", 0, 0, 400, 38, BG_HEADER, BORDER, 1);
            var titleIO = sc.ScreenItems.Create<HmiIOField>("Edit_Title");
            titleIO.Left = SX(0); titleIO.Top = SY(6); titleIO.Width = (uint)SX(370); titleIO.Height = (uint)SY(26);
            titleIO.BackColor = BG_HEADER; titleIO.ForeColor = Color.White;
            titleIO.BorderColor = BG_HEADER; titleIO.BorderWidth = 0;
            SetPropEnum(titleIO, "IOFieldType", "Output");
            SetPropEnum(titleIO, "TextHorizontalAlignment", "Center");
            SetMLText(titleIO, "Text", "EDIT VALVE DETAILS");
            var tDyn = titleIO.Dynamizations.Create<ScriptDynamization>("ProcessValue");
            tDyn.ScriptCode =
                JS_READ + "let idx=r(Tags(\"SelectedValve\").Read());\nlet num=(\"000\"+(idx||1)).slice(-3);\nreturn \"EDIT V-\" + num;";
            tDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");

            var closeBtn = MakeBtn(sc, "Edit_CloseX", 368, 4, 28, 28, "&#x2715;", BG_HEADER, Color.White, BG_HEADER, 0, 15, true);
            AddScriptEvent(closeBtn, PopupMarkClosedJs() + "HMIRuntime.UI.SysFct[\"CloseScreenInPopup\"](\"Popup_ValveEdit\");");

            MakeTb(sc, "Edit_NameLbl", 20, 54, 360, 22, "NAME", M_TRANS, M_MUTED, 0, "Left", 13, false);
            var nameField = sc.ScreenItems.Create<HmiIOField>("Edit_NameField");
            nameField.Left = SX(20); nameField.Top = SY(78); nameField.Width = (uint)SX(360); nameField.Height = (uint)SY(36);
            nameField.BackColor = Color.White; nameField.ForeColor = M_TEXT; nameField.BorderColor = BORDER; nameField.BorderWidth = 1;
            SetPropEnum(nameField, "IOFieldType", "InputOutput");
            DynTag(nameField, "ProcessValue", "EditNameBuffer");

            MakeTb(sc, "Edit_LocLbl", 20, 122, 360, 22, "LOCATION", M_TRANS, M_MUTED, 0, "Left", 13, false);
            var locField = sc.ScreenItems.Create<HmiIOField>("Edit_LocField");
            locField.Left = SX(20); locField.Top = SY(146); locField.Width = (uint)SX(360); locField.Height = (uint)SY(36);
            locField.BackColor = Color.White; locField.ForeColor = M_TEXT; locField.BorderColor = BORDER; locField.BorderWidth = 1;
            SetPropEnum(locField, "IOFieldType", "InputOutput");
            DynTag(locField, "ProcessValue", "EditLocBuffer");

            var saveBtn = MakeBtn(sc, "Edit_Save", 20, 200, 170, 46, "&#x2713; SAVE", Color.FromArgb(255, 16, 185, 129), Color.White, Color.FromArgb(255, 52, 211, 153), 2, 15, true);
            SetStr(saveBtn, "Authorization", "Operate");
            AddScriptEvent(saveBtn,
                JS_READ +
                "let idx=r(Tags(\"SelectedValve\").Read());\n" +
                "let vTag=\"V\"+(\"000\"+(idx||1)).slice(-3);\n" +
                "Tags(vTag+\"_Name\").Write(r(Tags(\"EditNameBuffer\").Read())||\"\");\n" +
                "Tags(vTag+\"_Location\").Write(r(Tags(\"EditLocBuffer\").Read())||\"\");\n" +
                PopupMarkClosedJs() +
                "HMIRuntime.UI.SysFct[\"CloseScreenInPopup\"](\"Popup_ValveEdit\");");

            var cancelBtn = MakeBtn(sc, "Edit_Cancel", 210, 200, 170, 46, "CANCEL", Color.FromArgb(255, 55, 65, 81), Color.White, Color.FromArgb(255, 107, 114, 128), 2, 15, true);
            AddScriptEvent(cancelBtn, PopupMarkClosedJs() + "HMIRuntime.UI.SysFct[\"CloseScreenInPopup\"](\"Popup_ValveEdit\");");
        }

        // ── CONFIRM DISABLE popup — shown when un-configuring an OPEN/MOVING valve ──────────────
        static void BuildConfirmDisableScreen(HmiScreen sc)
        {
            Console.WriteLine("  Drawing Screen_ConfirmDisable...");
            SetPropUInt(sc, "Width", (uint)SX(460));
            SetPropUInt(sc, "Height", (uint)SY(220));
            sc.BackColor = BG_DARK;

            MakeRect(sc, "Confirm_Header", 0, 0, 460, 38, Color.FromArgb(255, 194, 65, 12), BORDER, 1);
            MakeTb(sc, "Confirm_Title", 0, 8, 460, 24, "&#x26A0; CONFIRM", Color.FromArgb(255, 194, 65, 12), Color.White, 0, "Center", 15, true);

            // No SetFont before -> unshrunk default font overflowed the field, cutting text off on
            // both edges. Explicit SFont() + a shorter message (no embedded \n - IOField single-line
            // text doesn't reliably wrap) fixes it.
            var msgIO = sc.ScreenItems.Create<HmiIOField>("Confirm_Message");
            msgIO.Left = SX(20); msgIO.Top = SY(60); msgIO.Width = (uint)SX(420); msgIO.Height = (uint)SY(60);
            msgIO.BackColor = BG_DARK; msgIO.ForeColor = Color.White;
            msgIO.BorderColor = BG_DARK; msgIO.BorderWidth = 0;
            SetPropEnum(msgIO, "IOFieldType", "Output");
            SetPropEnum(msgIO, "TextHorizontalAlignment", "Center");
            SetFont(msgIO, SFont(14), false);
            SetMLText(msgIO, "Text", "This valve is OPEN/MOVING. Disable anyway?");
            var mDyn = msgIO.Dynamizations.Create<ScriptDynamization>("ProcessValue");
            mDyn.ScriptCode =
                JS_READ +
                "let idx=r(Tags(\"ConfirmValveIdx\").Read());\n" +
                "let num=(\"000\"+(idx||1)).slice(-3);\n" +
                "return \"V-\" + num + \" is OPEN/MOVING. Disable anyway?\";";
            mDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");

            var yesBtn = MakeBtn(sc, "Confirm_Yes", 60, 150, 160, 46, "YES, DISABLE", Color.FromArgb(255, 205, 32, 38), Color.White, Color.FromArgb(255, 255, 100, 100), 2, 14, true);
            SetStr(yesBtn, "Authorization", "Operate");
            AddScriptEvent(yesBtn,
                JS_READ +
                "let idx=r(Tags(\"ConfirmValveIdx\").Read());\n" +
                "if(!idx) return;\n" +
                "let vTag=\"V\"+(\"000\"+idx).slice(-3);\n" +
                "Tags(vTag+\"_Configured\").Write(false);\n" +
                PopupMarkClosedJs() +
                "HMIRuntime.UI.SysFct[\"CloseScreenInPopup\"](\"Popup_ConfirmDisable\");");

            var noBtn = MakeBtn(sc, "Confirm_No", 240, 150, 160, 46, "CANCEL", Color.FromArgb(255, 55, 65, 81), Color.White, Color.FromArgb(255, 107, 114, 128), 2, 14, true);
            AddScriptEvent(noBtn, PopupMarkClosedJs() + "HMIRuntime.UI.SysFct[\"CloseScreenInPopup\"](\"Popup_ConfirmDisable\");");
        }
    }
}
