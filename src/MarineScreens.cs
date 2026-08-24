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
using Siemens.Engineering.HmiUnified.UI.Controls;

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
            // "Center", not "Middle": HmiVerticalAlignment is {Top, Center, Bottom, Stretch}, so
            // "Middle" threw inside SetProp's Enum.Parse and was swallowed by its empty catch -
            // every textbox in the project silently kept the default top alignment instead.
            SetProp(tb, "VerticalTextAlignment", "Center");
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

        // Same idea, for the badge's CM-number label text (no precedent elsewhere in this codebase
        // of binding HmiButton.Text via native TagDynamization, unlike BackColor/ForeColor above —
        // untested until a real build runs, so this flips to script for every badge on first failure.
        static bool s_nativeBadgeTextOk = true;

        // State-code palette/labels, shared by every native mapping (same order and meaning as
        // STATE_COLOR_LOGIC / STATE_TEXT_LOGIC, which the Bilge table's script slots still use).
        // Mimic/diagram boxes: FILL shows position, BORDER shows trouble. Splitting them means a
        // faulted valve still says where it is — the same information loss the popup had, and it
        // mattered more here because the mimic is what an operator scans first.
        // Deliberately NOT a flashing script: 89 boxes re-evaluating on every 1Hz clock tick is
        // real load on the Home screen, and native value maps cost nothing at runtime. It also
        // keeps flashing available to mean "unacknowledged" later, which is the usual convention.
        // Mimic box colour, flash included. Bound to <valve>_DispCode, which the PLC already
        // alternates between 1 (red) and the position code at 0.25 Hz when a fault is latched.
        // ONE native value map, ZERO scripts: 89 scripted boxes re-evaluating on a clock tick is
        // real load, and this project already learned that with the popup (handoff item 15).
        // When nothing is faulted DispCode equals PosCode and never changes, so a healthy plant
        // costs nothing at all — the load scales with faults, not with valve count.
        static readonly int[] DISP_CODES = { 0, 1, 2, 3, 4, 5, 6, 7 };
        static readonly object[] DISP_COLORS = {
            Color.FromArgb(255, 154, 163, 176), // 0 UNCONFIGURED
            Color.FromArgb(255, 235,  60,  48), // 1 FAULT — red half of the flash
            Color.FromArgb(255, 226, 168,   0), // 2 LOCAL — steady amber, never flashes
            Color.FromArgb(255,   0, 158,  74), // 3 OPEN
            Color.FromArgb(255,  96, 106, 122), // 4 CLOSED
            Color.FromArgb(255,   0, 162, 255), // 5 NO POSITION
            Color.FromArgb(255,   0, 162, 255), // 6 OPENING
            Color.FromArgb(255,   0, 162, 255)  // 7 CLOSING
        };

        static readonly int[] POS_CODES = { 0, 3, 4, 5, 6, 7 };
        static readonly object[] POS_COLORS = {
            Color.FromArgb(255, 154, 163, 176), // 0 UNCONFIGURED
            Color.FromArgb(255,   0, 158,  74), // 3 OPEN
            Color.FromArgb(255,  96, 106, 122), // 4 CLOSED
            Color.FromArgb(255,   0, 162, 255), // 5 NO POSITION
            Color.FromArgb(255,   0, 162, 255), // 6 OPENING
            Color.FromArgb(255,   0, 162, 255)  // 7 CLOSING
        };
        // Border keyed off StateCode, which is the one that DOES go to 1 on any fault.
        static readonly int[] BORDER_CODES = { 0, 1, 2, 3, 4, 5, 6, 7 };
        static readonly object[] BORDER_COLORS = {
            Color.FromArgb(255,  17,  17,  17), // 0 normal
            Color.FromArgb(255, 235,  60,  48), // 1 FAULT  — red outline
            Color.FromArgb(255, 226, 168,   0), // 2 LOCAL  — amber outline
            Color.FromArgb(255,  17,  17,  17), // 3
            Color.FromArgb(255,  17,  17,  17), // 4
            Color.FromArgb(255,  17,  17,  17), // 5
            Color.FromArgb(255,  17,  17,  17), // 6
            Color.FromArgb(255,  17,  17,  17)  // 7
        };

        static readonly int[] STATE_CODES = { 0, 1, 2, 3, 4, 5, 6, 7 };
        static readonly object[] STATE_COLORS = {
            Color.FromArgb(255, 154, 163, 176), // 0 UNCONFIGURED
            Color.FromArgb(255, 205,  32,  38), // 1 FAULT
            Color.FromArgb(255, 226, 168,   0), // 2 LOCAL
            Color.FromArgb(255,   0, 158,  74), // 3 OPEN
            Color.FromArgb(255,  96, 106, 122), // 4 CLOSED
            Color.FromArgb(255,   0, 162, 255), // 5 POSITION UNKNOWN
            Color.FromArgb(255,   0, 162, 255), // 6 OPENING — one motion colour, not three
            Color.FromArgb(255,   0, 162, 255)  // 7 CLOSING
        };

        // Table variant adds code 9 = "no valve in this slot on this page" (a zone whose valve
        // count isn't a multiple of 14 leaves the last page short), drawn in transparent ink so the
        // row reads as blank. Positive rather than -1: negative codes are avoided since the mapping
        // table is fussy about what it accepts, and there was no reason to risk it.
        // It was 6 until 2026-08-16, when 6/7 became real StateCodes (OPENING/CLOSING) — the two
        // shared this one value map, so an opening valve rendered its status text transparent.
        // Moved to 9, which sits outside the StateCode range; keep it that way if more states appear.
        // There is deliberately no TBL_WORDS — a mapping table is rejected on a Text property
        // ("Creation of Tag dynamization entries is not allowed for this property"), so the status
        // word is supplied ready-made by the PLC in <Zone>TblStateTxt and bound directly. That is
        // also why OPENING/CLOSING needed no HMI change to appear in the list: the PLC writes the
        // word, the HMI just displays it.
        static readonly int[] TBL_CODES = { 0, 1, 2, 3, 4, 5, 6, 7, 9 };
        static readonly object[] TBL_COLORS = {
            Color.FromArgb(255, 154, 163, 176),   // 0 UNCONFIGURED
            Color.FromArgb(255, 205,  32,  38),   // 1 FAULT
            Color.FromArgb(255, 226, 168,   0),   // 2 LOCAL
            Color.FromArgb(255,   0, 158,  74),   // 3 OPEN
            Color.FromArgb(255,  96, 106, 122),   // 4 CLOSED
            Color.FromArgb(255,   0, 162, 255),   // 5 POSITION UNKNOWN
            Color.FromArgb(255,   0, 162, 255),   // 6 OPENING  — motion blue, same as 5/7
            Color.FromArgb(255,   0, 162, 255),   // 7 CLOSING  — direction is carried by the word
            M_TRANS                               // 9 empty slot
        };

        // Command buttons fill only in their own state; every other state has no entry, so the
        // button keeps its own (white) background. Verify this fallback holds in runtime — if an
        // unmatched value renders wrong instead, these need all seven codes spelled out.
        // Command buttons grey out when the PLC will actually REFUSE the press, so a tap never
        // fails silently. Two states qualify: 0 = UNCONFIGURED (FC_IoMapper skips the valve
        // entirely, so nothing happens at all) and 2 = LOCAL (hand control at the valve).
        // A latched FAULT deliberately does NOT grey out — a latched alarm must never block a
        // command, because you have to be able to close a ballast valve in an emergency without
        // first finding a reset button. Greying it would imply blocked when it is not.
        // Known gap: an UNHEALTHY valve is also refused but reports StateCode 1, which it shares
        // with latched-alarm-but-healthy. StateCode alone cannot separate them, so that case is
        // not covered here; it needs a per-slot fault code in the table window.
        static readonly int[] LOCK_CODES = { 0, 1, 2, 3, 4, 5, 6, 7 };
        static readonly object[] LOCK_OPEN_FORE = {
            Color.FromArgb(255, 108, 117, 128), Color.FromArgb(255,   0, 158,  74),
            Color.FromArgb(255, 108, 117, 128), Color.FromArgb(255,   0, 158,  74),
            Color.FromArgb(255,   0, 158,  74), Color.FromArgb(255,   0, 158,  74),
            Color.FromArgb(255,   0, 158,  74), Color.FromArgb(255,   0, 158,  74)
        };
        static readonly object[] LOCK_CLOSE_FORE = {
            Color.FromArgb(255, 108, 117, 128), Color.FromArgb(255, 205,  32,  38),
            Color.FromArgb(255, 108, 117, 128), Color.FromArgb(255, 205,  32,  38),
            Color.FromArgb(255, 205,  32,  38), Color.FromArgb(255, 205,  32,  38),
            Color.FromArgb(255, 205,  32,  38), Color.FromArgb(255, 205,  32,  38)
        };

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
                    "Tags(vTag+\"" + setSuf + "\").Write(true);\n";
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

        // ── BALLAST SYSTEM DIAGRAM SCREENS ────────────────────────────────────────────
        // A dedicated screen per zone showing the real plan-view P&ID artwork (hull, tanks,
        // ballast mains, every valve at its true position) as an imported SVG background,
        // with one live overlay per valve on top of the drawing's own valve boxes.
        //
        // Why a whole screen instead of the zone screen's existing mimic strip: the artwork is
        // 1760x700 (2.5:1) and the mimic slot is 1888x460 (4.1:1). Squeezing it in would scale
        // the drawing to ~0.66 and drop its 10px labels to ~6.6px — unreadable on the panel.
        // Given its own screen it sits at ~1.07 scale, so every CM tag, DN size and capacity
        // stays legible at the size it was designed for.
        //
        // The SVG is a STATIC background: it draws the orange valve boxes, but knows nothing
        // about live state. Each overlay below covers its box with a state-coloured square and
        // a transparent hit target wired to the same popup every other valve control opens.
        // ARTWORK COORDINATES BELOW MUST STAY IN SYNC WITH hmi_graphics/ballast_*.svg.
        // Home layout, design space (1920x1080 canvas, scaled to the 1366x768 panel by SX/SY).
        const int HOME_BAL_X = 16,   HOME_BAL_Y = 174, HOME_BAL_W = 1792, HOME_BAL_H = 554;
        const int HOME_BLG_X = 16,   HOME_BLG_Y = 744, HOME_BLG_W = 1208, HOME_BLG_H = 320;
        const int HOME_ALM_X = 1240, HOME_ALM_Y = 744, HOME_ALM_W = 664,  HOME_ALM_H = 320;
        // AFT valves occupy artwork x 92..898 and FWD x 946..1565 - a clean 48px gap between the
        // two groups. The touch split sits at its midpoint.
        const int HOME_SPLIT_AX = 922;

        struct DiagValve
        {
            public string Cm;    // client CM number, for the item name only
            public int Slot;     // absolute PLC slot -> V001..V096
            public int Ax, Ay;   // centre of this valve's box, in ARTWORK coordinates
            public DiagValve(string cm, int slot, int ax, int ay) { Cm = cm; Slot = slot; Ax = ax; Ay = ay; }
        }

        // ── HOME SCREEN artwork, re-measured 2026-08-20 ────────────────────────
        // Home was split into two drawings. "Ballast Home.png" (1792x554) holds the 27 AFT +
        // 35 FWD ballast valves; "Bilge home.png" (1208x320) holds the 27 bilge/ER valves that
        // used to sit in a meaningless parked grid in the corner of the old full.png.
        // Both are authored at exactly the box they are drawn into, so these coordinates map 1:1.
        // Every CM number was read off 6x crops of the artwork and cross-checked against the
        // zone tables: all 27 AFT valves land at x 92..898 and all 35 FWD at x 946..1565, with no
        // interleaving - a single misread label would have put a valve in the wrong group.
        // The two CM00 boxes in the drawing get no overlay, by design.
        static readonly DiagValve[] HOME_BALLAST_DIAGRAM = {
            new DiagValve("CM25",  1,  592, 212), new DiagValve("CM26",  2,  592, 324), new DiagValve("CM50",  3,   92, 395),
            new DiagValve("CM51",  4,   92, 268), new DiagValve("CM52",  5,   92, 146), new DiagValve("CM53",  6,   92, 216),
            new DiagValve("CM54",  7,   92, 327), new DiagValve("CM55",  8,  792, 146), new DiagValve("CM56",  9,  792, 392),
            new DiagValve("CM57", 10,  870, 329), new DiagValve("CM58", 11,  898, 198), new DiagValve("CM59", 12,  713, 392),
            new DiagValve("CM60", 13,  740, 146), new DiagValve("CM67", 14,  244, 157), new DiagValve("CM68", 15,  184, 217),
            new DiagValve("CM69", 16,  244, 403), new DiagValve("CM70", 17,  184, 403), new DiagValve("CM71", 18,  214, 129),
            new DiagValve("CM72", 19,  214, 379), new DiagValve("CM78", 20,  319, 157), new DiagValve("CM79", 21,  377, 157),
            new DiagValve("CM80", 22,  421, 228), new DiagValve("CM81", 23,  319, 403), new DiagValve("CM82", 24,  380, 403),
            new DiagValve("CM83", 25,  377, 112), new DiagValve("CM84", 26,  380, 452), new DiagValve("CM85", 27,  132, 485),
            new DiagValve("CM27", 55,  946, 198), new DiagValve("CM28", 56,  946, 339), new DiagValve("CM29", 57,  988, 267),
            new DiagValve("CM30", 58, 1011, 267), new DiagValve("CM31", 59, 1037, 207), new DiagValve("CM32", 60, 1037, 326),
            new DiagValve("CM33", 61, 1070, 207), new DiagValve("CM34", 62, 1070, 326), new DiagValve("CM35", 63, 1351, 212),
            new DiagValve("CM36", 64, 1351, 326), new DiagValve("CM37", 65, 1399, 259), new DiagValve("CM38", 66, 1375, 278),
            new DiagValve("CM39", 67, 1468, 215), new DiagValve("CM40", 68, 1565, 241), new DiagValve("CM41", 69, 1491, 278),
            new DiagValve("CM42", 70, 1423, 259), new DiagValve("CM43", 71, 1446, 277), new DiagValve("CM44", 72, 1515, 259),
            new DiagValve("CM45", 73, 1565, 301), new DiagValve("CM46", 74, 1468, 326), new DiagValve("CM47", 75, 1565, 271),
            new DiagValve("CM48", 76, 1246, 215), new DiagValve("CM49", 77, 1246, 326), new DiagValve("CM61", 78, 1189, 382),
            new DiagValve("CM62", 79, 1223, 149), new DiagValve("CM63", 80, 1197, 149), new DiagValve("CM64", 81, 1217, 382),
            new DiagValve("CM65", 82, 1332, 370), new DiagValve("CM66", 83, 1313, 158), new DiagValve("CM77", 84, 1539, 259),
            new DiagValve("CM86", 85, 1104, 382), new DiagValve("CM87", 86, 1133, 382), new DiagValve("CM88", 87, 1161, 382),
            new DiagValve("CM89", 88, 1270, 369), new DiagValve("CM90", 89, 1293, 393),
        };

        static readonly DiagValve[] HOME_BILGE_DIAGRAM = {
            new DiagValve("CM01", 28, 1127, 169), new DiagValve("CM02", 29,  656, 133), new DiagValve("CM03", 30,  656, 175),
            new DiagValve("CM04", 31,  741, 135), new DiagValve("CM05", 32,  741, 177), new DiagValve("CM06", 33,   92, 154),
            new DiagValve("CM07", 34,  382, 121), new DiagValve("CM08", 35,  382, 183), new DiagValve("CM09", 36,  158, 113),
            new DiagValve("CM10", 37,  237,  68), new DiagValve("CM11", 38,  897, 135), new DiagValve("CM12", 39,  897, 175),
            new DiagValve("CM13", 40, 1055, 173), new DiagValve("CM14", 41, 1066, 135), new DiagValve("CM15", 42,  178, 208),
            new DiagValve("CM16", 43,  201, 170), new DiagValve("CM17", 44,  255, 207), new DiagValve("CM18", 45,  140,  20),
            new DiagValve("CM19", 46,  540, 175), new DiagValve("CM20", 47,  456, 132), new DiagValve("CM21", 48,  461, 175),
            new DiagValve("CM23", 49,  789, 136), new DiagValve("CM24", 50,  815,  20), new DiagValve("CM94", 51,  289, 274),
            new DiagValve("CM95", 52,  314, 274), new DiagValve("CM96", 53,  339, 274), new DiagValve("CM97", 54,  365, 274),
        };


        // AFT zone, slots 1..27, in "AFT zone.png"'s own 1888x500 coordinates (re-exported
        // 2026-08-18 — NOT the earlier "AFT zone1" artwork; the graphic name changed too).
        // Coordinates measured with DetectBoxes.exe; every CM number read off 2.5x crops and
        // matched to its box. All 27 Ballast-Aft valves are placed — the earlier version omitted
        // CM57/CM58/CM80/CM85 because the old artwork did not label them; this one does.
        // The artwork has 28 boxes: the one at (299,126) carries a "?" and NO CM label, so it is
        // unconfirmed and deliberately gets no overlay.
        static readonly DiagValve[] AFT_DIAGRAM = {
            new DiagValve("CM25",  1, 1089, 183),  new DiagValve("CM26",  2, 1089, 300),
            new DiagValve("CM50",  3,  144, 375),  new DiagValve("CM51",  4,  134, 243),
            new DiagValve("CM52",  5,  145, 114),  new DiagValve("CM53",  6,  145, 188),
            new DiagValve("CM54",  7,  144, 304),  new DiagValve("CM55",  8, 1419, 126),
            new DiagValve("CM56",  9, 1420, 356),  new DiagValve("CM57", 10, 1603, 303),
            new DiagValve("CM58", 11, 1684, 176),  new DiagValve("CM59", 12, 1299, 366),
            new DiagValve("CM60", 13, 1341, 122),  new DiagValve("CM67", 14,  413, 126),
            new DiagValve("CM68", 15,  299, 189),  new DiagValve("CM69", 16,  413, 383),
            new DiagValve("CM70", 17,  299, 383),  new DiagValve("CM71", 18,  356,  84),
            new DiagValve("CM72", 19,  356, 341),  new DiagValve("CM78", 20,  572, 126),
            new DiagValve("CM79", 21,  681, 132),  new DiagValve("CM80", 22,  751, 200),
            new DiagValve("CM81", 23,  569, 383),  new DiagValve("CM82", 24,  674, 383),
            new DiagValve("CM83", 25,  681,  83),  new DiagValve("CM84", 26,  673, 435),
            new DiagValve("CM85", 27,  206, 436),
        };

        // FWD zone, slots 55..89, in "FWD Zone.png"'s own 1888x500 coordinates (re-exported
        // 2026-08-18; graphic name changed from "FWD Zone1"). All 35 Ballast-Fwd valves placed
        // against their PRINTED CM numbers. This replaces the earlier reading-order assignment,
        // which was explicitly arbitrary because that artwork carried no real labels.
        // The artwork has 36 boxes; 35 get overlays. The one at (1735,242) is labelled "CM00"
        // and gets none — it is a REAL valve in the forepeak (between TK 1 DB C and Forepeak TK C)
        // that the client's schedule simply does not contain, so there is no PLC slot to bind it
        // to. The numbering points at 11-059: the forepeak run goes 11-056 (CM45), 11-057 (CM46),
        // 11-058 (CM47) and then stops, and 11-059 is absent from the schedule. Unconfirmed —
        // inferred from the gap, not read off the P&ID. If the client confirms it is in scope the
        // pool needs a 90th slot. Same family as the seven other drawn-but-unscheduled valves
        // (11-007, 11-013, 11-017, 11-018, 11-019, 11-070, 11-090) in handoff item 3.
        // An earlier revision of this artwork also carried a duplicate CM48 outside the hull at
        // (1732,419); it was never mapped here, and has since been removed from the drawing.
        static readonly DiagValve[] FWD_DIAGRAM = {
            new DiagValve("CM27", 55,   21, 179),  new DiagValve("CM28", 56,   21, 306),
            new DiagValve("CM29", 57,   60, 244),  new DiagValve("CM30", 58,   96, 244),
            new DiagValve("CM31", 59,  175, 179),  new DiagValve("CM32", 60,  175, 306),
            new DiagValve("CM33", 61,  235, 179),  new DiagValve("CM34", 62,  235, 306),
            new DiagValve("CM35", 63,  897, 180),  new DiagValve("CM36", 64,  897, 305),
            new DiagValve("CM37", 65, 1007, 234),  new DiagValve("CM38", 66,  958, 249),
            new DiagValve("CM39", 67, 1171, 180),  new DiagValve("CM40", 68, 1373, 206),
            new DiagValve("CM41", 69, 1232, 250),  new DiagValve("CM42", 70, 1057, 234),
            new DiagValve("CM43", 71, 1106, 253),  new DiagValve("CM44", 72, 1282, 234),
            new DiagValve("CM45", 73, 1373, 281),  new DiagValve("CM46", 74, 1171, 305),
            new DiagValve("CM47", 75, 1374, 243),  new DiagValve("CM48", 76,  647, 179),
            new DiagValve("CM49", 77,  646, 306),  new DiagValve("CM61", 78,  448, 358),
            new DiagValve("CM62", 79,  594, 120),  new DiagValve("CM63", 80,  499, 125),
            new DiagValve("CM64", 81,  532, 358),  new DiagValve("CM65", 82,  809, 345),
            new DiagValve("CM66", 83,  781, 130),  new DiagValve("CM77", 84, 1333, 233),
            new DiagValve("CM86", 85,  295, 358),  new DiagValve("CM87", 86,  346, 358),
            new DiagValve("CM88", 87,  396, 358),  new DiagValve("CM89", 88,  692, 345),
            new DiagValve("CM90", 89,  750, 370),
        };

        // HOME overview, in "full.png"'s own 1888x584 coordinates. Boxes are 20px here, not 30.
        //
        // Home shows the FULL BALLAST system only. Bilge is a separate system, not a region of the
        // ballast one, and drawing both on one sheet crams it past readability — so Bilge gets its
        // own screen and its own artwork (decided 2026-08-18). The 27 Bilge valves are still PARKED
        // in a grid at the bottom here, so every valve stays reachable from the landing page even
        // though this drawing does not depict them.
        //
        // All 62 ballast valves placed against their printed CM numbers; coordinates measured with
        // DetectBoxes.exe, labels read off 3.2-6.0x crops.
        // The artwork has 64 boxes; 62 get overlays. The two labelled "CM00" at (196,156) and
        // (1808,274) are unconfirmed and get none — binding a real valve's live state to an
        // unconfirmed box on a ballast system is worse than leaving the box bare.
        // An earlier revision also carried a duplicate CM48 at (1851,56) in the top-right corner;
        // it was never mapped here, and has since been removed from the drawing.
        static readonly DiagValve[] HOME_DIAGRAM = {
            // ---- BALLAST AFT, slots 1-27 ----
            new DiagValve("CM25",  1,  638, 214),  new DiagValve("CM26",  2,  638, 331),
            new DiagValve("CM50",  3,  100, 405),  new DiagValve("CM51",  4,  100, 272),
            new DiagValve("CM52",  5,  100, 144),  new DiagValve("CM53",  6,  100, 217),
            new DiagValve("CM54",  7,  100, 334),  new DiagValve("CM55",  8,  846, 144),
            new DiagValve("CM56",  9,  846, 402),  new DiagValve("CM57", 10,  929, 336),
            new DiagValve("CM58", 11,  958, 198),  new DiagValve("CM59", 12,  764, 402),
            new DiagValve("CM60", 13,  792, 144),  new DiagValve("CM67", 14,  258, 156),
            new DiagValve("CM68", 15,  196, 219),  new DiagValve("CM69", 16,  258, 413),
            new DiagValve("CM70", 17,  196, 413),  new DiagValve("CM71", 18,  227, 127),
            new DiagValve("CM72", 19,  227, 388),  new DiagValve("CM78", 20,  338, 156),
            new DiagValve("CM79", 21,  397, 156),  new DiagValve("CM80", 22,  443, 230),
            new DiagValve("CM81", 23,  338, 413),  new DiagValve("CM82", 24,  401, 413),
            new DiagValve("CM83", 25,  397, 109),  new DiagValve("CM84", 26,  401, 464),
            new DiagValve("CM85", 27,  142, 497),

            // ---- BALLAST FWD, slots 55-89 ----
            new DiagValve("CM27", 55, 1008, 198),  new DiagValve("CM28", 56, 1008, 346),
            new DiagValve("CM29", 57, 1051, 271),  new DiagValve("CM30", 58, 1075, 271),
            new DiagValve("CM31", 59, 1103, 209),  new DiagValve("CM32", 60, 1103, 332),
            new DiagValve("CM33", 61, 1137, 208),  new DiagValve("CM34", 62, 1137, 332),
            new DiagValve("CM35", 63, 1431, 214),  new DiagValve("CM36", 64, 1431, 332),
            new DiagValve("CM37", 65, 1481, 263),  new DiagValve("CM38", 66, 1456, 283),
            new DiagValve("CM39", 67, 1554, 216),  new DiagValve("CM40", 68, 1655, 244),
            new DiagValve("CM41", 69, 1577, 282),  new DiagValve("CM42", 70, 1507, 263),
            new DiagValve("CM43", 71, 1530, 282),  new DiagValve("CM44", 72, 1602, 263),
            new DiagValve("CM45", 73, 1655, 306),  new DiagValve("CM46", 74, 1554, 332),
            new DiagValve("CM47", 75, 1655, 275),  new DiagValve("CM48", 76, 1321, 216),
            new DiagValve("CM49", 77, 1321, 332),  new DiagValve("CM61", 78, 1262, 392),
            new DiagValve("CM62", 79, 1297, 147),  new DiagValve("CM63", 80, 1270, 147),
            new DiagValve("CM64", 81, 1291, 392),  new DiagValve("CM65", 82, 1412, 378),
            new DiagValve("CM66", 83, 1392, 157),  new DiagValve("CM77", 84, 1628, 263),
            new DiagValve("CM86", 85, 1173, 392),  new DiagValve("CM87", 86, 1202, 391),
            new DiagValve("CM88", 87, 1232, 392),  new DiagValve("CM89", 88, 1346, 378),
            new DiagValve("CM90", 89, 1370, 403),

            // ---- BILGE, slots 28-54 — PARKED, not on this drawing (see header) ----
            new DiagValve("CM01", 28, 1640, 440),  new DiagValve("CM02", 29, 1678, 440),
            new DiagValve("CM03", 30, 1716, 440),  new DiagValve("CM04", 31, 1754, 440),
            new DiagValve("CM05", 32, 1792, 440),  new DiagValve("CM06", 33, 1830, 440),
            new DiagValve("CM07", 34, 1640, 468),  new DiagValve("CM08", 35, 1678, 468),
            new DiagValve("CM09", 36, 1716, 468),  new DiagValve("CM10", 37, 1754, 468),
            new DiagValve("CM11", 38, 1792, 468),  new DiagValve("CM12", 39, 1830, 468),
            new DiagValve("CM13", 40, 1640, 496),  new DiagValve("CM14", 41, 1678, 496),
            new DiagValve("CM15", 42, 1716, 496),  new DiagValve("CM16", 43, 1754, 496),
            new DiagValve("CM17", 44, 1792, 496),  new DiagValve("CM18", 45, 1830, 496),
            new DiagValve("CM19", 46, 1640, 524),  new DiagValve("CM20", 47, 1678, 524),
            new DiagValve("CM21", 48, 1716, 524),  new DiagValve("CM23", 49, 1754, 524),
            new DiagValve("CM24", 50, 1792, 524),  new DiagValve("CM94", 51, 1830, 524),
            new DiagValve("CM95", 52, 1640, 552),  new DiagValve("CM96", 53, 1678, 552),
            new DiagValve("CM97", 54, 1716, 552),
        };

        // BILGE zone, slots 28..54, in "Bilge.png"'s own 1888x500 coordinates.
        // Bilge is a SEPARATE SYSTEM from ballast, so it gets its own drawing and its own screen
        // (decided 2026-08-18). All 27 CM numbers printed on the artwork match the schedule's Bilge
        // system exactly: CM01-CM21 (the schedule has no CM22), CM23, CM24, and the four Fire tags
        // CM94-CM97. Coordinates measured with DetectBoxes.exe, labels read off 2.3x crops.
        // The artwork has 31 boxes; the four labelled "CM00" are UNCONFIRMED and get no overlay —
        // they sit at (434,138), (333,177), (240,284) and (428,283).
        static readonly DiagValve[] BILGE_DIAGRAM = {
            new DiagValve("CM01", 28, 1765, 263),  new DiagValve("CM02", 29, 1037, 207),
            new DiagValve("CM03", 30, 1036, 273),  new DiagValve("CM04", 31, 1168, 210),
            new DiagValve("CM05", 32, 1168, 276),  new DiagValve("CM06", 33,  143, 240),
            new DiagValve("CM07", 34,  592, 189),  new DiagValve("CM08", 35,  592, 285),
            new DiagValve("CM09", 36,  246, 177),  new DiagValve("CM10", 37,  368, 107),
            new DiagValve("CM11", 38, 1409, 210),  new DiagValve("CM12", 39, 1409, 272),
            new DiagValve("CM13", 40, 1654, 270),  new DiagValve("CM14", 41, 1671, 210),
            new DiagValve("CM15", 42,  277, 323),  new DiagValve("CM16", 43,  312, 265),
            new DiagValve("CM17", 44,  396, 322),  new DiagValve("CM18", 45,  218,  32),
            new DiagValve("CM19", 46,  856, 273),  new DiagValve("CM20", 47,  727, 205),
            new DiagValve("CM21", 48,  734, 272),  new DiagValve("CM23", 49, 1243, 212),
            new DiagValve("CM24", 50, 1282,  32),
            // Fire main tags, drawn as a labelled row at the bottom of the sheet.
            new DiagValve("CM94", 51,  294, 452),  new DiagValve("CM95", 52,  333, 452),
            new DiagValve("CM96", 53,  372, 452),  new DiagValve("CM97", 54,  411, 452),
        };

        // Draws the P&ID sheet + its live valve overlay inside an existing zone screen, replacing
        // that zone's simple badge strip. graphicName is the name the SVG carries in TIA's Graphics
        // collection after it is imported by hand (Openness has no API for that import — same
        // category of manual step as designing a Faceplate Type).
        //
        // The artwork is authored at exactly (pw x ph) so it is placed 1:1: artwork coordinates ARE
        // screen coordinates here. That is deliberate — scaling it down to fit is what made the
        // DN/function labels unreadable, so the drawing is redrawn at target size instead.
        // boxPx: the artwork's own valve-square size. 30 on the zone sheets, 20 on Home's
        // whole-vessel sheet. Must match the drawing or the overlay leaves a grey fringe / spills
        // onto the pipe lines — measure with DetectBoxes.exe rather than guessing.
        // -- Graphic name resolution ---------------------------------------------
        // TIA NEVER overwrites a graphic on import - it appends _1, _2, _3 and keeps the original.
        // So the artwork most recently imported is the highest-suffixed variant, and binding to the
        // bare base name silently pins the screen to the FIRST import forever.
        // Observed live 2026-08-19: Screen_Home still rendered 'Full' - the placeholder artwork with
        // CM00 on every box - while the corrected drawing sat unused in the project as 'full_3'.
        // Three earlier imports had all been ignored for the same reason.
        static readonly List<string> s_graphicCatalog = new List<string>();

        static void LoadGraphicCatalog(object project)
        {
            s_graphicCatalog.Clear();
            try {
                var gp = project.GetType().GetProperty("Graphics");
                if (gp == null) { Console.WriteLine("  [Graphics] no Graphics collection on project."); return; }
                var en = gp.GetValue(project, null) as IEnumerable;
                if (en == null) return;
                foreach (var o in en) {
                    var np = o.GetType().GetProperty("Name");
                    if (np == null) continue;
                    var n = np.GetValue(o, null) as string;
                    if (!string.IsNullOrEmpty(n)) s_graphicCatalog.Add(n);
                }
                Console.WriteLine("  [Graphics] catalog loaded: " + s_graphicCatalog.Count + " graphic(s).");
            } catch (Exception ex) {
                Console.WriteLine("  [Graphics] catalog unavailable: " + ex.Message);
            }
        }

        // Returns the newest variant of baseName: 'full' -> 'full_3' when full, full_1..full_3 exist.
        // Matches ONLY base and base_<int>, so 'AFT zone1' can never be mistaken for 'AFT zone'.
        static string ResolveGraphic(string baseName)
        {
            string best = baseName;
            int bestRank = -1;
            foreach (var n in s_graphicCatalog) {
                if (string.Equals(n, baseName, StringComparison.OrdinalIgnoreCase)) {
                    if (bestRank < 0) { best = n; bestRank = 0; }
                    continue;
                }
                if (n.Length <= baseName.Length + 1) continue;
                if (!n.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)) continue;
                if (n[baseName.Length] != '_') continue;
                int rank;
                if (!int.TryParse(n.Substring(baseName.Length + 1), out rank)) continue;
                if (rank > bestRank) { best = n; bestRank = rank; }
            }
            if (bestRank < 0)
                Console.WriteLine("  [Graphics] '" + baseName + "' NOT FOUND in project - screen will render blank.");
            else if (!string.Equals(best, baseName, StringComparison.Ordinal))
                Console.WriteLine("  [Graphics] '" + baseName + "' -> '" + best + "' (newest import)");
            return best;
        }

        // prefix: item-name namespace. Home now carries TWO diagrams, and both would otherwise
        // try to create "Dg_BG"/"Dg_Sheet" and collide.
        // viewOnly: skip the per-valve transparent hit buttons entirely. Home is an overview -
        // the client asked for no valve control there - so it gets zone navigation instead, and
        // not creating 89 buttons is also the single biggest object saving on that screen.
        static void BuildZoneDiagram(HmiScreen sc, int px, int py, int pw, int ph,
                                      string graphicName, string zoneLabel, DiagValve[] valves,
                                      int boxPx = 30, string prefix = "Dg", bool viewOnly = false)
        {
            Console.WriteLine("  Drawing P&ID mimic '" + graphicName + "' (" + zoneLabel + ", " +
                              valves.Length + " live valve overlays)...");

            MakePanel(sc, prefix + "_BG", px, py, pw, ph, M_BOX, M_BORDER, 1);
            // No title bar: same reasoning as the Home mimic and the other zone mimics — the
            // active nav tab already says which zone this is, so a repeated "AFT BALLAST —
            // SYSTEM DIAGRAM" band would just be more of the same wasted space. Removing it
            // gives the drawing itself the full panel height instead.

            // Exact artwork size, no margin: the overlay math below assumes drawX/drawY/drawW/drawH
            // map 1:1 onto the SVG's own 1888x500 viewBox. The previous pw-2/ph-2 "framing" margin
            // meant GraphicStretchMode.Uniform had to letterbox+scale the image to fit, shifting
            // every rendered point by a few px relative to where the overlays assumed it would be —
            // confirmed as a real bug, not a rounding artefact to live with.
            int drawX = px, drawY = py;
            int drawW = pw, drawH = ph;

            var gv = sc.ScreenItems.Create<HmiGraphicView>(prefix + "_Sheet");
            gv.Left = SX(drawX); gv.Top = SY(drawY);
            gv.Width = (uint)SX(drawW); gv.Height = (uint)SY(drawH);
            gv.BackColor = Color.Transparent;
            // Uniform: the box matches the artwork's own aspect, so nothing crops or distorts.
            SetPropEnumSafe(gv, "GraphicStretchMode", "Uniform");
            string resolvedGraphic = ResolveGraphic(graphicName);
            try { gv.Graphic = resolvedGraphic; }
            catch (Exception ex) {
                Console.WriteLine("  [WARN] Could not set Graphic='" + resolvedGraphic + "': " + ex.Message);
                Console.WriteLine("         Import hmi_graphics/" + graphicName + ".png into the HMI's");
                Console.WriteLine("         Graphics collection in TIA, then re-run this build.");
            }

            foreach (var v in valves) {
                int cx = drawX + v.Ax;
                int cy = drawY + v.Ay;
                string vTag = string.Format("V{0:D3}", v.Slot);

                // State square sitting exactly over the drawing's own valve box, so the static grey
                // box becomes the live state colour. 30px because that is what the 2026-08-16
                // artwork actually measures - DetectBoxes.exe reports every box in both zone PNGs
                // as 29-30px. Keep this equal to the artwork's real box size or the overlay either
                // leaves a grey fringe or spills onto the pipe lines.
                var box = MakeRect(sc, prefix + "_" + v.Cm + "_st", cx - boxPx / 2, cy - boxPx / 2, boxPx, boxPx,
                                   M_MUTED, Color.FromArgb(255, 17, 17, 17), 1);
                if (s_nativeBadgeOk) {
                    if (!AddValueMap(DynTag(box, "BackColor", vTag + "_DispCode"), DISP_CODES, DISP_COLORS)) {
                        RemoveDyn(box, "BackColor");
                        s_nativeBadgeOk = false;
                        Console.WriteLine("  [Diagram] native colour mapping unavailable — falling back to script.");
                    }
                }
                if (!s_nativeBadgeOk) Dyn(box, "BackColor", ValveStateColorScript(vTag), "AutomaticTags");
                // No fault BORDER any more. DispCode already carries fault (as the flash) and local
                // (steady amber) in the fill, so a second binding saying the same thing was
                // redundant — and dropping it halves the live bindings on this screen: 89 fewer on
                // Home. The border is now plain definition only.

                // Transparent hit target, deliberately larger than the 30px box so it is
                // comfortably touchable; carries the bowtie so the symbol survives on top of the
                // live colour, and opens the same SBO popup as every other valve control.
                if (viewOnly) continue;
                int hitPx = boxPx + 10;
                var hit = sc.ScreenItems.Create<HmiButton>(prefix + "_" + v.Cm + "_hit");
                hit.Left = SX(cx - hitPx / 2); hit.Top = SY(cy - hitPx / 2);
                hit.Width = (uint)SX(hitPx); hit.Height = (uint)SY(hitPx);
                hit.BackColor = M_TRANS; hit.ForeColor = Color.White;
                hit.BorderColor = M_TRANS; hit.BorderWidth = 0;
                SetProp(hit, "HorizontalTextAlignment", "Center");
                SetProp(hit, "VerticalTextAlignment", "Center");
                SetFont(hit, SFont(11), true);
                // No bowtie glyph on the overlay. The artwork already draws its own valve symbol
                // inside every box, so painting one on top just doubled it up and cluttered a
                // 20-30px square. Removed on request 2026-08-18; the button stays fully functional
                // as the click target, it simply has no text of its own.
                SetText(hit, "Text", "");
                try { hit.GetType().GetProperty("ShowFocusVisual").SetValue(hit, false, null); } catch {}
                AddPopupScript(hit, vTag);
            }
        }

        // SetPropEnum lives in GenerateHmiLayout.cs and throws on an unknown enum value; the
        // stretch-mode name is the one thing here not confirmed by reflection, so it must not be
        // allowed to abort a whole screen build if V20 spells it differently.
        static void SetPropEnumSafe(object obj, string propName, string value)
        {
            try { SetPropEnum(obj, propName, value); }
            catch (Exception ex) { Console.WriteLine("  [WARN] " + propName + "='" + value + "': " + ex.Message); }
        }

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
            // IS the real CM-number label, and its native click opens the SBO popup — one item
            // doing both jobs instead of a separate label textbox plus a separate hit-target button.
            //
            // The label used to be a static "CM-" + slot-index string (Disp(), now removed) — that
            // was always fake: slot index is our own internal pool position, not the client's CM
            // number, and the two only coincide for the first ~21 valves before drifting apart (the
            // client's CM numbering runs continuously across the whole ship, not per zone). The
            // click/popup binding below (vTag) was always correct — it's real physical-slot data —
            // so the label just needs to catch up to what it already opens. Bound to the same
            // V{slot}_CmNo tag the popup title already uses successfully. No fallback text for a
            // blank CmNo (unconfigured slots, none today at 89/89): the badge is already grey/muted for
            // that state, so a blank label is honest, unlike a synthesized number that could be
            // mistaken for a real one.
            var hit = sc.ScreenItems.Create<HmiButton>(name + "_hit");
            hit.Left = SX(cx - 30); hit.Top = SY(cy - R); hit.Width = (uint)SX(60); hit.Height = (uint)SY(R * 2 + 18);
            hit.BackColor = M_TRANS; hit.ForeColor = M_TEXT;
            hit.BorderColor = M_TRANS; hit.BorderWidth = 0;
            SetProp(hit, "HorizontalTextAlignment", "Center");
            SetProp(hit, "VerticalTextAlignment", "Bottom");
            SetFont(hit, SFont(13), true);
            bool labelBoundNative = false;
            if (s_nativeBadgeTextOk) {
                labelBoundNative = DynTag(hit, "Text", vTag + "_CmNo") != null;
                if (!labelBoundNative) {
                    s_nativeBadgeTextOk = false;
                    Console.WriteLine("  [DrawValveSym] native text binding unavailable — falling back to script for all badge labels.");
                }
            }
            if (!labelBoundNative) {
                Dyn(hit, "Text",
                    "function readTag(v){return (v!==null&&typeof v===\"object\"&&\"Value\" in v)?v.Value:v;}\n" +
                    "return readTag(Tags(\"" + vTag + "_CmNo\").Read());", "AutomaticTags");
            }
            try { hit.GetType().GetProperty("ShowFocusVisual").SetValue(hit, false, null); } catch {}
            AddPopupScript(hit, vTag);
        }

        // ── HOME SCREEN ─────────────────────────────────────────────────
        static void BuildScreenHome(HmiScreen sc)
        {
            Console.WriteLine("  Drawing Home Screen (v5 - view only, ballast + bilge, zone touch areas)...");

            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);

            BuildHomeHeader(sc);
            BuildNav(sc, "Screen_Home");

            // Two drawings, stacked. Both are authored at exactly the box they occupy, so the
            // overlay coordinates map 1:1 and nothing is rescaled at runtime.
            // Full width for both was arithmetically impossible: 584 + 500 = 1084px of artwork
            // into 890px of screen. The ballast drawing was re-exported 5% smaller (1792x554)
            // instead, which is the point where the two artworks' different printed box sizes
            // (20px and 30px) both land on 19px - so ballast and bilge squares match on the glass.
            BuildZoneDiagram(sc, HOME_BAL_X, HOME_BAL_Y, HOME_BAL_W, HOME_BAL_H,
                             "Ballast Home", "BALLAST OVERVIEW", HOME_BALLAST_DIAGRAM, 19, "Dg", true);
            BuildZoneDiagram(sc, HOME_BLG_X, HOME_BLG_Y, HOME_BLG_W, HOME_BLG_H,
                             "Bilge home", "BILGE OVERVIEW", HOME_BILGE_DIAGRAM, 19, "Bg", true);

            // The five KPI cards came off on request - the bilge drawing occupies that row now.
            // The one figure per zone worth keeping is the fault count, which moves into a strip
            // under the alarm card rather than being lost entirely.
            BuildAlarmPanel(sc, HOME_ALM_X, HOME_ALM_Y, HOME_ALM_W, HOME_ALM_H - 74);
            BuildZoneFaultStrip(sc, HOME_ALM_X, HOME_ALM_Y + HOME_ALM_H - 66, HOME_ALM_W, 66);

            // The artwork carries no divider of its own, so without these there is nothing telling
            // the operator that the ballast picture is two separate touch areas.
            int divX = HOME_BAL_X + HOME_SPLIT_AX;
            MakeRect(sc, "Home_Div", divX - 1, HOME_BAL_Y + 6, 2, HOME_BAL_H - 12, M_MUTED, M_MUTED, 0);
            MakeTb(sc, "Home_LblAft", divX - 210, HOME_BAL_Y + 6, 200, 24,
                   "◄ AFT BALLAST", M_TRANS, M_MUTED, 0, "Right", 15, true);
            MakeTb(sc, "Home_LblFwd", divX + 10, HOME_BAL_Y + 6, 240, 24,
                   "FORWARD BALLAST ►", M_TRANS, M_MUTED, 0, "Left", 15, true);
            MakeTb(sc, "Home_LblBlg", HOME_BLG_X + 10, HOME_BLG_Y + HOME_BLG_H - 32, 240, 24,
                   "BILGE / ER ►", M_TRANS, M_MUTED, 0, "Left", 15, true);

            // Created LAST so they sit above both the artwork and the status squares: a tap
            // anywhere in a half - including straight on a valve box - navigates to that zone.
            MakeZoneTouch(sc, "Home_HitAft", HOME_BAL_X, HOME_BAL_Y, HOME_SPLIT_AX, HOME_BAL_H, "Screen_AftBallast");
            MakeZoneTouch(sc, "Home_HitFwd", divX, HOME_BAL_Y, HOME_BAL_W - HOME_SPLIT_AX, HOME_BAL_H, "Screen_FwdBallast");
            MakeZoneTouch(sc, "Home_HitBlg", HOME_BLG_X, HOME_BLG_Y, HOME_BLG_W, HOME_BLG_H, "Screen_Bilge");
        }

        // Full-bleed transparent navigation target.
        static void MakeZoneTouch(HmiScreen sc, string name, int x, int y, int w, int h, string target)
        {
            var b = sc.ScreenItems.Create<HmiButton>(name);
            b.Left = SX(x); b.Top = SY(y);
            b.Width = (uint)SX(w); b.Height = (uint)SY(h);
            b.BackColor = M_TRANS; b.ForeColor = M_TRANS;
            b.BorderColor = M_TRANS; b.BorderWidth = 0;
            SetText(b, "Text", "");
            try { b.GetType().GetProperty("ShowFocusVisual").SetValue(b, false, null); } catch {}
            AddNavClick(b, target);
        }

        // Fault count per zone - the one number kept from the five KPI cards that were removed.
        static void BuildZoneFaultStrip(HmiScreen sc, int x, int y, int w, int h)
        {
            MakePanel(sc, "Zs_BG", x, y, w, h, M_BOX, M_BORDER, 1);
            string[] lbl = { "AFT", "BILGE / ER", "FWD" };
            string[] pfx = { "Aft", "Er", "Fwd" };
            int cw = w / 3;
            for (int i = 0; i < 3; i++) {
                int cx = x + i * cw;
                if (i > 0) MakeRect(sc, "Zs_Sep" + i, cx, y + 12, 1, h - 24, M_LINE, M_LINE, 0);
                MakeDot(sc, "Zs_Dot" + i, cx + 20, y + h / 2, 7, 7, M_TRANS, M_RED, 2);
                MakeTb(sc, "Zs_Lbl" + i, cx + 34, y + 10, cw - 90, h - 20, lbl[i], M_TRANS, M_MUTED, 0, "Left", 14, false);
                var v = MakeLiveText(sc, "Zs_Val" + i, cx + cw - 74, y + 10, 62, h - 20, M_RED, "Right", 24, true);
                DynTag(v, "Text", "Valves_DB_" + pfx[i] + "Fault");
            }
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
            var userText = MakeLiveText(sc, "Hdr_User", 1290, 8, 400, 30, M_HDRTXT, "Right", 19, false);
            Dyn(userText, "Text",
                "var u = \"\"; try { u = Tags(\"@UserName\").Read(); } catch(e){}\n" +
                "if (!u) u = \"GUEST\";\n" +
                "return \"\\uD83D\\uDC64  USER: \" + u.toUpperCase();", "AutomaticTags");

            // One button, not two. Which action it performs is decided at click time from
            // @UserName, and its caption follows the same test - so it reads LOGIN while nobody is
            // signed in and LOGOUT once someone is. Living in the header means it is on every
            // screen, which matters for audit: a user left signed in across a watch change has the
            // next person's valve commands recorded against their name, and the way to prevent
            // that is to make signing out reachable from wherever the operator happens to be.
            var authBtn = MakeBtn(sc, "Hdr_AuthBtn", 1700, 8, 200, 30, "LOGIN",
                                  M_ACCENT, Color.White, M_ACCENT, 0, 16, true);
            Dyn(authBtn, "Text",
                "var u = \"\"; try { u = Tags(\"@UserName\").Read(); } catch(e){}\n" +
                "if (!u || String(u).toUpperCase() === \"DEFAULTUSER\") return \"LOGIN\";\n" +
                "return \"LOGOUT\";", "AutomaticTags");
            AddScriptEvent(authBtn,
                "var u = \"\"; try { u = Tags(\"@UserName\").Read(); } catch(e){}\n" +
                "if (!u || String(u).toUpperCase() === \"DEFAULTUSER\") {\n" +
                "  HMIRuntime.UI.UserManagement.SysFct.ShowLoginDialog();\n" +
                "} else {\n" +
                "  HMIRuntime.UI.SysFct.LogOff();\n" +
                "}\n");

            // Title band.
            MakeRect(sc, "Title_Rule", 0, 46, 1920, 4, M_ACCENT, M_ACCENT, 0);
            MakeTb(sc, "Title_Main", 0, 50, 1920, 54, "MV WESTERLY  &#xB7;  VALVE REMOTE CONTROL SYSTEM",
                   M_TRANS, M_TEXT, 0, "Center", 32, true);
            // Subtitle ("Bilge & Ballast Distribution — 89 Motorised Valves") removed on request
            // to give its 24px back to the illustration panels below on every screen - the nav
            // bar and everything under it moved up to reclaim it (see BuildNav's y=104, was 128).
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

        // Screen_Login now hosts the AUDIT LOG. The screen keeps its original project name so the
        // --only=Login key, the nav target list and the dormant --finish-login-auth fixup all stay
        // valid; only its content and nav label changed.
        //
        // A dedicated login screen is redundant in WinCC Unified - runtime raises its own login
        // dialog as soon as an Authorization-protected control is touched. LOGOUT is deliberately
        // kept: audit attribution depends on it. Leave a user logged in across a watch change and
        // the next person's valve commands are recorded against the previous operator's name.
        public static void BuildAuditLogScreen(HmiScreen sc)
        {
            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);
            BuildHomeHeader(sc);
            BuildNav(sc, "Screen_Login");

            MakeTb(sc, "Audit_Title", 20, 166, 1000, 32,
                   "AUDIT LOG  ·  OPERATOR ACTION HISTORY", M_TRANS, M_TEXT, 0, "Left", 22, true);


            // Siemens' own Audit Viewer, shipped with TIA V20 as a custom web control and already
            // registered in runtime (UAAuditViewerExtension / AuditDL). Its manifest declares
            // class "PC, Comfort", so a Unified Comfort Panel like this MTP1500 can render it.
            //
            // "Siemens.AuditViewer" is the ONLY accepted ContainedType: TIA normalises it to
            // {4727C505-0E12-46AB-BF7B-42ECD1E66FD2} and rejects every guid spelling outright with
            // "No control exists for given contained type value" - checked on a scratch screen
            // 2026-08-23 rather than guessed.
            //
            // This reads the Audit Trail itself, which is why it beats rebuilding operator actions
            // as alarms: the real 365-day record instead of a 7-day copy, plus filtering and the
            // integrity-checked CSV export an auditor actually asks for.
            var av = sc.ScreenItems.Create<HmiCustomWebControlContainer>("AuditViewer_1", "Siemens.AuditViewer");
            // Placed large deliberately. There is no "open maximised" option - HmiWindowFlag
            // offers CanMaximize, which is permission to maximise, not a starting state - so
            // the only way to stop the operator maximising it by hand every time is to give
            // it the room up front.
            av.Left  = SX(12);   av.Top    = SY(206);
            av.Width = (uint)SX(1896); av.Height = (uint)SY(862);
            av.Authorization = "Operate";

        }

        // Shared by every screen — all 7 targets now exist (Screen_Home, Screen_Bilge,
        // Screen_FwdBallast, Screen_AftBallast, Screen_Alarms, Screen_Diagnostics,
        // Screen_Login are all created by Run()), so every button is live. `activeTarget`
        // is whichever of these the calling screen IS, so its own button highlights.
        static void BuildNav(HmiScreen sc, string activeTarget)
        {
            MakeRect(sc, "Nav_BG", 0, 104, 1920, 58, M_BOX, M_LINE, 1);

            // Zone buttons run in valve-number order (AFT slots 1-27, BILGE/ER slots 28-54,
            // FWD slots 55-89), which is also stern->bow, matching the mimic's zone order.
            string[] labels  = { "&#x2302;  HOME", "&#x2693;  BALLAST AFT", "&#x1F4A7;  BILGE / ER",
                                 "&#x2693;  BALLAST FWD", "&#x1F514;  ALARMS", "&#x1F4C8;  CONFIG", "&#x1F4CB;  AUDIT LOG" };
            string[] targets = { "Screen_Home", "Screen_AftBallast", "Screen_Bilge", "Screen_FwdBallast",
                                 "Screen_Alarms", "Screen_Diagnostics", "Screen_Login" };

            int w = 258, h = 46, y = 110, x0 = 20, gap = 8;
            for (int i = 0; i < labels.Length; i++) {
                bool active = (targets[i] == activeTarget);
                Color bg = active ? M_ACCENT : M_BOX;
                Color fg = active ? Color.White : M_TEXT;
                Color bd = active ? M_ACCENT : M_LINE;
                var btn = MakeBtn(sc, "Nav_" + i, x0 + i * (w + gap), y, w, h, labels[i], bg, fg, bd, 1, 18, active);
                if (!active) AddNavClick(btn, targets[i]); // no self-navigation needed on the active screen
            }
        }

        // ── VESSEL MIMIC — all 89 slots, full width/height ─────────────
        static void BuildVesselMimic(HmiScreen sc, int px, int py, int pw, int ph)
        {
            MakePanel(sc, "Mim_BG", px, py, pw, ph, M_BOX, M_BORDER, 1);
            // No title bar and no colour-key legend: the title only repeated what the nav tab
            // and page heading already say, and the legend just restated colours an operator
            // learns once from the valve popup. Both removed to give the mimic itself more
            // room instead of spending it on a header band and a footer strip.

            // Hull geometry — derived from (px,py,pw,ph) so this stays correct if the panel
            // is ever resized again. Zone order is AFT (stern, left) -> ER (mid) -> FWD (bow,
            // right) — real vessel geography; the previous ER-FWD-AFT order put "AFT BALLAST"
            // beside a "BOW" label, which was backwards.
            // topY starts right under the panel border now that the 42px header band is gone
            // (was py+56); botY is unchanged — the space the legend used to occupy at the
            // bottom is left as clean panel background rather than being filled with something
            // new that wasn't asked for.
            int topY = py + 16, botY = py + 368, midY = (topY + botY) / 2;
            int sternX = px + 54;
            const int bowMargin = 34, bowLen = 220, hullT = 4;
            int bowTipX = px + pw - bowMargin;
            int straightX = bowTipX - bowLen;

            int zoneL = px + 94, zoneR = straightX;
            int zoneW = (zoneR - zoneL) / 3;
            int[] zoneX = { zoneL, zoneL + zoneW, zoneL + zoneW * 2 };
            int[] div = { zoneX[1], zoneX[2] };

            string[] zoneNames  = { "AFT BALLAST", "BILGE / ER", "FORWARD BALLAST" };
            // Slots follow physical position stern->bow, so the mimic reads straight across
            // instead of jumping between zones. FB_ValveLoop uses the exact same boundaries
            // (i<=28 Aft, i<=56 Er, else Fwd) for its per-zone counters.
            // FWD is the widest zone (40 slots): the client schedule puts 35 of its 89 valves
            // forward, against 27 each aft and midships, so the even split the mimic used to
            // assume never matched the real vessel.
            int[] zoneVStart    = { 1, 28, 55 };
            int[] zoneVEnd      = { 27, 54, 89 };
            // 7+7+9 columns x 4 rows = 28+28+36 capacity for 27+27+35 real valves — a little
            // slack in each row rather than an exact fit, now that there's no spare zone padding.
            int[] zoneCols      = { 7, 7, 9 };
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
            MakeTb(sc, "Pls_Val0", x + w - 78, rY0, 68, rowH, "89", M_TRANS, M_TEXT, 0, "Right", 22, true);
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

            // Panels start 24px higher than before (174, was 198) since the header's subtitle
            // line was removed. All three zones now share the same 500px illustration height -
            // matches AFT's diagram exactly, so FWD/Bilge's placeholder mimic sits at the same
            // size their real artwork will eventually take. None of them grow to fill the
            // reclaimed 24px themselves - it all goes to the table below instead.
            // AFT's diagram MUST stay exactly 1888x500 - that's the native size of the
            // already-designed "AFT zone" artwork, and any other size would letterbox it under
            // GraphicStretchMode.Uniform, throwing the live overlay coordinates out of alignment
            // again (the exact bug fixed earlier this session). Sizing FWD/Bilge to match is safe
            // since their mimic is still plain code-drawn, no fixed artwork to stay matched to yet.
            const int illustH = 500;
            int tableY = 174 + illustH + 24;          // 698, same on every zone
            int tableH = 1062 - tableY;                // all end on Home's 1062 bottom edge

            // All three zones have real artwork now (Bilge added 2026-08-18), so the code-drawn
            // BuildZoneMimic fallback is no longer reachable from here.
            // Graphic names must match EXACTLY what was imported into TIA. These were re-exported
            // on 2026-08-18 and are not the earlier "AFT zone1"/"FWD Zone1" names.
            if (zonePrefix == "Aft")
                BuildZoneDiagram(sc, 16, 174, 1888, 500, "AFT zone", zoneLabel, AFT_DIAGRAM);
            else if (zonePrefix == "Fwd")
                BuildZoneDiagram(sc, 16, 174, 1888, 500, "FWD Zone", zoneLabel, FWD_DIAGRAM);
            else
                BuildZoneDiagram(sc, 16, 174, 1888, 500, "Bilge", zoneLabel, BILGE_DIAGRAM);

            // Table takes width back off the summary (1650 -> 1674): the summary only has to fit
            // six short label/number rows, whereas the table has six columns fighting for room.
            BuildValveTable(sc, 16, tableY, 1674, tableH, zonePrefix, vStart, maxPage);

            // Summary sits beside the table, sharing its top edge, and now runs the full width left
            // over to the right margin (1680..1904) instead of stopping at 1880 and leaving a dead
            // strip. Its height reaches down to just above the page buttons for the same reason —
            // 260 left an odd blank gap in the middle of the column.
            // Short title: BuildKpiBox gives the title only (w-108)px, so the full zone name would
            // clip; the zone is already named on the illustration header directly above.
            // 200 wide, not narrower: BuildKpiBox gives its title only (w-108)px, and "SUMMARY" at
            // font 17 needs ~75 of the 92 that leaves — any narrower and the title starts clipping.
            const int sumX = 1704, sumW = 200;      // 1704 + 200 = 1904 = 1920 - 16px margin
            // Shares the table's top edge and stops just above the page buttons, so it shortens
            // with the table when a zone carries the taller P&ID drawing.
            BuildKpiBox(sc, "SUMMARY", vStart, vEnd, zonePrefix, sumX, tableY, sumW, 1002 - tableY);

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
            // No title bar here either — the nav tab and the zone's own valve-list header
            // directly below already say which zone and how many valves; repeating it in a
            // header band on the illustration was the same wasted space as Home's mimic title.

            int hullL = px + 24, hullT = py + 12, hullR = px + pw - 24, hullB = py + ph - 16;
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
            // v2 columns follow the client's own valve schedule. Two were dropped:
            //   NO.   - a zone-relative counter (1..28) that existed only because the paging
            //           window needed one; it means nothing to anyone operating the valve.
            //   NAME  - the client's schedule has no "name" field, so it could never be filled.
            // TAG became CM NO, reading the real stored CmNo instead of the PLC-CONCAT'd
            // 'CM-' + index string, which mislabelled most valves once the real schedule showed
            // CM numbers skip values and run past 88.
            // FUNCTION added 2026-08-16 on request - it was previously left out as "already dense,
            // and it's on the popup", but the popup's meta card was cut back to VALVE TAG only, so
            // Function had nowhere left to appear. It needed a PLC change too: the zone windows
            // mirrored Name/Loc/CmNo/VTag but never FuncName (only the Config screen did), so
            // <Zone>TblFunc arrays were added to Valve_Meta_DB and packed in FB_ValveLoop.
            // Budget at pw=1674: colW=809; fields 64+100+145+155+110+180 = 754, plus 5 pads at 10
            // = 804, leaving 5px slack. LOCATION gave up the most room since its real values are
            // short ("DB tank", "Aft peak P") even though the field allows 20 chars.
            // Widths against worst-case content:
            //   CM NO     64  "CM97" @15 bold           ~44px
            //   VALVE TAG100  "11-020-A1" @14           ~70px  (String[12] worst ~92px)
            //   LOCATION 145  "Pump 2 discharge" @13   ~114px  (String[20] worst ~143px)
            //   FUNCTION 155  "Cross-over manifold" @13 ~136px  (String[22] worst ~157px)
            //   STATUS   110  "UNCONFIGURED" @14 bold  ~101px  (longest state word)
            //   COMMAND  180  two 86px buttons + 8 gap
            const int cmW = 64, vtagW = 100, locW = 145, funcW = 155, statusW = 110,
                      btnW = 86, btnGap = 8, pad = 10;

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
                int hxVTag = cx0 + cmW + pad;
                int hxLoc  = hxVTag + vtagW + pad;
                int hxFunc = hxLoc + locW + pad;
                int hxSt   = hxFunc + funcW + pad;
                int hxCmd  = hxSt + statusW + pad;

                MakeTb(sc, "Tbl_H_CmNo" + col, cx0,    hy, cmW,               colHdrH, "CM NO",     M_TRANS, M_MUTED, 0, "Left",   14, true);
                MakeTb(sc, "Tbl_H_VTag" + col, hxVTag, hy, vtagW,             colHdrH, "VALVE TAG", M_TRANS, M_MUTED, 0, "Left",   14, true);
                MakeTb(sc, "Tbl_H_Loc"  + col, hxLoc,  hy, locW,              colHdrH, "LOCATION",  M_TRANS, M_MUTED, 0, "Left",   14, true);
                MakeTb(sc, "Tbl_H_Func" + col, hxFunc, hy, funcW,             colHdrH, "FUNCTION",  M_TRANS, M_MUTED, 0, "Left",   14, true);
                MakeTb(sc, "Tbl_H_St"   + col, hxSt,   hy, statusW,           colHdrH, "STATUS",    M_TRANS, M_MUTED, 0, "Center", 14, true);
                MakeTb(sc, "Tbl_H_Cmd"  + col, hxCmd,  hy, btnW * 2 + btnGap, colHdrH, "COMMAND",   M_TRANS, M_MUTED, 0, "Center", 14, true);

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

                    // Identity straight from the client's schedule, mirrored per page by the PLC.
                    // All three are TextBoxes so their left alignment actually applies - the
                    // HmiButton that MakeLiveText creates has no alignment property at all, so its
                    // text is always centred and its align argument is silently dropped.
                    var cmTb = MakeTb(sc, "Tr_CmNo" + sfx, cx0, rY, cmW, rowH, "", M_TRANS, M_ACCENT, 0, "Left", 15, true);
                    DynTag(cmTb, "Text", zonePrefix + "_TblCmNo_" + slot);
                    var vtagTb = MakeTb(sc, "Tr_VTag" + sfx, hxVTag, rY, vtagW, rowH, "", M_TRANS, M_TEXT, 0, "Left", 14, false);
                    DynTag(vtagTb, "Text", zonePrefix + "_TblVTag_" + slot);

                    var locVal = MakeTb(sc, "Tr_Loc" + sfx, hxLoc, rY, locW, rowH, "", M_TRANS, M_MUTED, 0, "Left", 13, false);
                    DynTag(locVal, "Text", zonePrefix + "_TblLoc_" + slot);

                    var funcVal = MakeTb(sc, "Tr_Func" + sfx, hxFunc, rY, funcW, rowH, "", M_TRANS, M_MUTED, 0, "Left", 13, false);
                    DynTag(funcVal, "Text", zonePrefix + "_TblFunc_" + slot);

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
                    // Grey the label when the PLC will refuse the press (unconfigured / local).
                    // Colour only — Authorization below and the PLC interlock are the real guards.
                    AddValueMap(DynTag(openBtn,  "ForeColor", stateTag), LOCK_CODES, LOCK_OPEN_FORE);
                    AddValueMap(DynTag(closeBtn, "ForeColor", stateTag), LOCK_CODES, LOCK_CLOSE_FORE);
                    // Set HERE, in the build path, not by the --finish-login-auth repair pass.
                    // These buttons bypass the popup entirely, so without it a logged-out user can
                    // stroke any valve straight from the list. The repair pass set it on the live
                    // objects, which meant EVERY zone-screen rebuild silently wiped it again — and
                    // that is exactly what happened on 2026-08-15, when Screen_AftBallast was
                    // recreated for the popup refactor and the 28 table buttons came back
                    // unprotected. The popup's own six buttons never had this problem precisely
                    // because they set it at creation, like this. See handoff item 17.
                    SetStr(openBtn,  "Authorization", "Operate");
                    SetStr(closeBtn, "Authorization", "Operate");
                    // Commands stay scripts, but these are click *events* — they run on tap, never
                    // on screen activation, so they cost nothing at load time. The valve is
                    // resolved at click time from the slot's live NO. tag.
                    AddSlotCmdScript(openBtn,  zonePrefix, slot, zoneStart, true);
                    AddSlotCmdScript(closeBtn, zonePrefix, slot, zoneStart, false);
                }
            }
        }

        // ── SYSTEM DIAGNOSTICS SCREEN ────────────────────────────────────────────────────
        // Which of the connected components is healthy: the PLC, the panel, and the three
        // ET200SP stations with their 36 I/O modules.
        //
        // None of that is drawn by hand. HmiSystemDiagnosisControl is a Siemens control that
        // builds itself from the hardware configuration, so adding a module in TIA shows up here
        // with no HMI work at all - as against roughly 45 status bits, a diagnostics DB for the
        // PLC to write them into, and a tile per device to keep in step with the rack by hand.
        //
        // Two view types exist, and the property is design-time, so the screen carries one
        // control of each and toggles Visible between them. (The alarm screen switches its own
        // view by writing AlarmSourceType at runtime; SystemDiagnosisViewType is not known to be
        // writable from runtime script, and two controls need no such assumption.)
        //
        //   Matrix     - grid of devices and their modules, coloured by status. The "what is
        //                connected and is it alive" view, and the reason this screen exists.
        //   Diagnosis  - the PLC's own diagnostic buffer as an event list, with timestamps.
        //                Says what happened and when, once the matrix has said what is wrong.
        //
        // NOT TESTABLE IN SIMULATION - there are no stations to unplug. It compiles, downloads,
        // and draws, but whether an S7-1200 populates the matrix as richly as an S7-1500 is a
        // real-panel question. See README, "System diagnostics".
        static void BuildSysDiagScreen(HmiScreen sc)
        {
            Console.WriteLine("  Drawing Screen_SysDiag (system diagnostics)...");
            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);
            BuildHomeHeader(sc);
            // No nav button targets this screen, so nothing matches and all seven stay live.
            BuildNav(sc, "Screen_SysDiag");

            MakeTb(sc, "SD_Ttl", 16, 174, 900, 30, "SYSTEM DIAGNOSTICS &#x2014; PLC, PANEL AND REMOTE I/O",
                   M_TRANS, M_TEXT, 0, "Left", 20, true);

            // Same two-tab shape as the alarm screen, so the interaction is already familiar.
            var btnMatrix = MakeBtn(sc, "SD_TabMatrix", 16, 214, 220, 46, "MODULE MATRIX",
                                    M_ACCENT, M_HDRTXT, M_BORDER, 1, 14, true);
            var btnBuffer = MakeBtn(sc, "SD_TabBuffer", 246, 214, 220, 46, "DIAGNOSTIC BUFFER",
                                    M_HDR, M_HDRTXT, M_BORDER, 1, 14, false);

            // Each tab shows one control, hides the other, and repaints both buttons so the
            // active one is obvious. Written out rather than shared because the two differ only
            // in which way round the colours and Visible flags go.
            AddScriptEvent(btnMatrix,
                "Screen.Items(\"SD_Matrix\").Visible = true;\n" +
                "Screen.Items(\"SD_Buffer\").Visible = false;\n" +
                "Screen.Items(\"SD_TabMatrix\").BackColor = 0xFF0074BA;\n" +
                "Screen.Items(\"SD_TabBuffer\").BackColor = 0xFF263242;");
            AddScriptEvent(btnBuffer,
                "Screen.Items(\"SD_Matrix\").Visible = false;\n" +
                "Screen.Items(\"SD_Buffer\").Visible = true;\n" +
                "Screen.Items(\"SD_TabMatrix\").BackColor = 0xFF263242;\n" +
                "Screen.Items(\"SD_TabBuffer\").BackColor = 0xFF0074BA;");

            MakeTb(sc, "SD_Hint", 486, 226, 1418, 24,
                   "Red or amber here means the station or module below it - walk to the rack and count along the slots.",
                   M_TRANS, M_MUTED, 0, "Left", 13, false);

            // ── Controls placed LAST ──────────────────────────────────────────────────────
            // HmiAlarmControl deadlocks the Openness API if anything is created after it (see
            // BuildAlarmScreen). This control is from the same family, so it gets the same
            // treatment rather than finding out the hard way on a 40-minute rebuild.
            const int cx = 16, cy = 270, cw = 1888, ch = 790;
            try {
                Console.WriteLine("  [DEBUG] Placing system diagnosis controls (may take a while)...");
                Console.Out.Flush();

                var matrix = sc.ScreenItems.Create<HmiSystemDiagnosisControl>("SD_Matrix");
                matrix.Left = SX(cx); matrix.Top = SY(cy);
                matrix.Width = (uint)SX(cw); matrix.Height = (uint)SY(ch);
                SetPropEnum(matrix, "SystemDiagnosisViewType", "Matrix");
                matrix.Visible = true;

                var buffer = sc.ScreenItems.Create<HmiSystemDiagnosisControl>("SD_Buffer");
                buffer.Left = SX(cx); buffer.Top = SY(cy);
                buffer.Width = (uint)SX(cw); buffer.Height = (uint)SY(ch);
                SetPropEnum(buffer, "SystemDiagnosisViewType", "Diagnosis");
                buffer.Visible = false;

                Console.WriteLine("  [DEBUG] Both diagnosis controls placed.");
            } catch (Exception ex) {
                Console.WriteLine("  [WARN] SystemDiagnosisControl creation failed: " + Root(ex));
            }
        }

        // ── CONFIGURATION SCREEN — all 89 slots, one global paged table ───────────────────
        // Replaces the old Screen_Diagnostics placeholder. Shows every valve's Name/Location/live
        // Status plus an Enable/Disable toggle - the same Configured flag that gates whether
        // FB_ValveLoop runs a slot's control logic at all (the pre-allocated-UDT-pool "enable"
        // mechanism from the original spec). v3: Name/Location editing was removed entirely - real
        // ships set valve names/locations once at commissioning and never change them, so the
        // editable popup (Screen_ValveEdit) was solving a problem that doesn't occur in practice.
        // See memory file editable-hmi-text-fields.md for the full method if this is ever needed
        // again elsewhere. Table is now narrower with a dedicated commissioning-status summary
        // panel to its right (BuildConfigSummaryPanel) instead of a single inline count - v2's
        // table alone still fits 16 rows/page (6 pages total).
        const int CFG_ROWS_PER_PAGE = 16;
        const int CFG_MAX_PAGE = 5; // 89 slots / 16 per page - 1, 0-based (6 pages, last one ragged: 9 of 16)

        static void BuildConfigScreen(HmiScreen sc)
        {
            Console.WriteLine("  Drawing Screen_Diagnostics as VALVE CONFIGURATION (89 slots, 6 pages)...");
            sc.BackColor = M_BG;
            MakeRect(sc, "BG", 0, 0, 1920, 1080, M_BG, M_BG, 0);
            BuildHomeHeader(sc);
            BuildNav(sc, "Screen_Diagnostics");

            // Table now spans the screen's full width - the commissioning counts moved out of a
            // right-hand column into the bottom strip below, next to the page/jump controls, to
            // free that width for the client's 5 schedule columns.
            const int px = 16, py = 174, pw = 1888, ph = 784;
            BuildConfigTable(sc, px, py, pw, ph);
            BuildConfigSummaryBar(sc, 1104, 966, 800, 92);

            // ── Control bar row 1: page nav, live PAGE label, GO TO VALVE# jump ──
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
                "if(t<1)t=1; if(t>" + VALVE_COUNT + ")t=" + VALVE_COUNT + ";\n" +
                "let pg=Math.floor((t-1)/" + CFG_ROWS_PER_PAGE + ");\n" +
                "if(pg<0)pg=0; if(pg>" + CFG_MAX_PAGE + ")pg=" + CFG_MAX_PAGE + ";\n" +
                "Tags(\"Valves_DB_CfgPage\").Write(pg);");

            // ── Control bar row 2: bulk configure per zone ──
            int pbY2 = pbY + 50;
            MakeTb(sc, "Cfg_BulkLbl", px, pbY2 + 4, 140, 26, "CONFIGURE ALL:", M_TRANS, M_MUTED, 0, "Left", 13, false);
            int bulkX = px + 150;
            var aftBtn = MakeBtn(sc, "Cfg_BulkAft", bulkX, pbY2, 150, 34, "AFT BALLAST", M_HDR, M_HDRTXT, M_BORDER, 1, 13, true);
            SetStr(aftBtn, "Authorization", "Operate");
            AddScriptEvent(aftBtn, ZoneConfigureAllScript(1, 27));

            var bilgeBtn = MakeBtn(sc, "Cfg_BulkBilge", bulkX + 160, pbY2, 150, 34, "BILGE / ER", M_HDR, M_HDRTXT, M_BORDER, 1, 13, true);
            SetStr(bilgeBtn, "Authorization", "Operate");
            AddScriptEvent(bilgeBtn, ZoneConfigureAllScript(28, 54));

            var fwdBtn = MakeBtn(sc, "Cfg_BulkFwd", bulkX + 320, pbY2, 150, 34, "BALLAST FWD", M_HDR, M_HDRTXT, M_BORDER, 1, 13, true);
            SetStr(fwdBtn, "Authorization", "Operate");
            AddScriptEvent(fwdBtn, ZoneConfigureAllScript(55, 89));

            // Way in to system diagnostics. It lives here rather than in the nav bar because the
            // nav is full at seven buttons and an eighth would mean narrowing all of them on
            // every screen - a full rebuild for a screen an operator has no reason to open.
            // Config is already the engineering screen, which is who this is for. Set apart from
            // the CONFIGURE ALL group so it does not read as a fourth bulk action.
            var sysBtn = MakeBtn(sc, "Cfg_SysDiag", bulkX + 530, pbY2, 240, 34,
                                 "&#x2699;  SYSTEM DIAGNOSTICS", M_ACCENT, Color.White, M_ACCENT, 1, 13, true);
            AddNavClick(sysBtn, "Screen_SysDiag");
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
            MakeTb(sc, "CfgTbl_Ttl", px + 14, py + 6, pw - 28, 26, "VALVE CONFIGURATION &#x2014; ALL 89 SLOTS", M_TRANS, M_HDRTXT, 0, "Left", 16, true);

            const int colHdrH = 28;
            int rowH = (ph - 38 - colHdrH - 8) / CFG_ROWS_PER_PAGE;

            // v4 columns mirror the client's own valve-schedule format exactly - CM No / Valve Tag /
            // System / Location / Function - so a row on screen can be checked line-for-line against
            // the sheet they issue. Replaces v3.1's ZONE + folded TAG-NAME pair: both of those were
            // derived from the array index, which the real schedule disproves (CM numbers have gaps
            // and run past 88, and a valve's system is schedule data, not an index range).
            const int cmW = 110, vtagW = 210, sysW = 190, locW = 400, funcW = 370, statusW = 170, cfgW = 270, pad = 16;

            int bodyTop = py + 38 + 2 + colHdrH + 4;
            MakeRect(sc, "CfgTbl_HdrBand", px + 1, py + 38, pw - 2, colHdrH + 6, M_HDRBAND, M_HDRBAND, 0);

            int cx0 = px + 16;
            int hy = py + 38 + 2;
            int hxVTag   = cx0 + cmW + pad;
            int hxSys    = hxVTag + vtagW + pad;
            int hxLoc    = hxSys + sysW + pad;
            int hxFunc   = hxLoc + locW + pad;
            int hxStatus = hxFunc + funcW + pad;
            int hxCfg    = hxStatus + statusW + pad;

            MakeTb(sc, "CfgTbl_H_CmNo",   cx0,      hy, cmW,     colHdrH, "CM NO",             M_TRANS, M_MUTED, 0, "Left",   13, true);
            MakeTb(sc, "CfgTbl_H_VTag",   hxVTag,   hy, vtagW,   colHdrH, "VALVE TAG",         M_TRANS, M_MUTED, 0, "Left",   13, true);
            MakeTb(sc, "CfgTbl_H_Sys",    hxSys,    hy, sysW,    colHdrH, "SYSTEM",            M_TRANS, M_MUTED, 0, "Left",   13, true);
            MakeTb(sc, "CfgTbl_H_Loc",    hxLoc,    hy, locW,    colHdrH, "LOCATION",          M_TRANS, M_MUTED, 0, "Left",   13, true);
            MakeTb(sc, "CfgTbl_H_Func",   hxFunc,   hy, funcW,   colHdrH, "FUNCTION",          M_TRANS, M_MUTED, 0, "Left",   13, true);
            MakeTb(sc, "CfgTbl_H_Status", hxStatus, hy, statusW, colHdrH, "STATUS",            M_TRANS, M_MUTED, 0, "Center", 13, true);
            MakeTb(sc, "CfgTbl_H_Cfg",    hxCfg,    hy, cfgW,    colHdrH, "ENABLE / DISABLE",  M_TRANS, M_MUTED, 0, "Center", 13, true);

            int rowW = pw - 26; // zebra/row background spans the full table width now

            for (int r = 0; r < CFG_ROWS_PER_PAGE; r++) {
                int slot = r + 1;
                int rY = bodyTop + r * rowH;
                string sfx = "_" + r;

                if (r % 2 == 1)
                    MakeRect(sc, "CfgTr_Zeb" + sfx, cx0 - 6, rY, rowW, rowH, M_ZEBRA, M_ZEBRA, 0);

                // Text columns are HmiTextBox, not MakeLiveText's HmiButton: HmiButton exposes NO
                // alignment property at all (confirmed by reflection - only Authorization), so its
                // text is permanently centred and MakeLiveText's align argument is silently
                // discarded by SetProp. That mismatched these left-aligned headers. A plain
                // DynTag on a TextBox's Text is already proven in this project (the previous
                // layout's TAG sub-column did exactly this); only ScriptDynamization needs a button.
                var cmVal = MakeTb(sc, "CfgTr_CmNo" + sfx, cx0, rY, cmW, rowH, "", M_TRANS, M_ACCENT, 0, "Left", 14, true);
                DynTag(cmVal, "Text", "Cfg_TblCmNo_" + slot);
                var vtagVal = MakeTb(sc, "CfgTr_VTag" + sfx, hxVTag, rY, vtagW, rowH, "", M_TRANS, M_TEXT, 0, "Left", 14, false);
                DynTag(vtagVal, "Text", "Cfg_TblVTag_" + slot);
                var sysVal = MakeTb(sc, "CfgTr_Sys" + sfx, hxSys, rY, sysW, rowH, "", M_TRANS, M_MUTED, 0, "Left", 13, false);
                DynTag(sysVal, "Text", "Cfg_TblSys_" + slot);
                var locVal = MakeTb(sc, "CfgTr_Loc" + sfx, hxLoc, rY, locW, rowH, "", M_TRANS, M_MUTED, 0, "Left", 13, false);
                DynTag(locVal, "Text", "Cfg_TblLoc_" + slot);
                var funcVal = MakeTb(sc, "CfgTr_Func" + sfx, hxFunc, rY, funcW, rowH, "", M_TRANS, M_MUTED, 0, "Left", 13, false);
                DynTag(funcVal, "Text", "Cfg_TblFunc_" + slot);

                var statusVal = MakeLiveText(sc, "CfgTr_Status" + sfx, hxStatus, rY, statusW, rowH, M_MUTED, "Center", 13, true);
                DynTag(statusVal, "Text", "Cfg_TblStateTxt_" + slot);
                AddValueMap(DynTag(statusVal, "ForeColor", "Cfg_TblState_" + slot), TBL_CODES, TBL_COLORS);

                var cfgBtn = MakeBtn(sc, "CfgTr_Toggle" + sfx, hxCfg, rY + 2, cfgW, rowH - 4, "", M_HDR, M_HDRTXT, M_BORDER, 1, 13, true);
                SetStr(cfgBtn, "Authorization", "Operate");
                AddConfigToggleTextAndColor(cfgBtn, slot);
                AddScriptEvent(cfgBtn, ConfigToggleScript(slot));
            }
        }

        // ── COMMISSIONING STATUS strip — bottom-right, alongside the page/jump controls ─────────
        // Was a full-height panel down the right side; moved here (v4) so the valve table can use
        // the screen's full width for the client's 5 schedule columns. Same four counts, laid out
        // horizontally instead of stacked. Reuses the per-zone summary tags FB_ValveLoop already
        // maintains (Valves_DB_AftConfigured etc. - built for Screen_Home's zone captions) so this
        // costs zero new PLC data.
        static void BuildConfigSummaryBar(HmiScreen sc, int px, int py, int pw, int ph)
        {
            MakePanel(sc, "CfgSum_BG", px, py, pw, ph, M_BOX, M_BORDER, 1);

            // Plant-wide total, sized up as the headline figure of the strip.
            MakeTb(sc, "CfgSum_TotalLbl", px + 18, py + 10, 190, 18, "TOTAL CONFIGURED", M_TRANS, M_MUTED, 0, "Left", 11, true);
            var totVal = MakeLiveText(sc, "CfgSum_TotalVal", px + 18, py + 28, 84, 46, M_GREEN, "Left", 32, true);
            DynTag(totVal, "Text", "Valves_DB_TotalConfigured");
            MakeTb(sc, "CfgSum_TotalOf", px + 104, py + 40, 90, 30, "/ " + VALVE_COUNT, M_TRANS, M_MUTED, 0, "Left", 17, false);

            MakeRect(sc, "CfgSum_DivMain", px + 215, py + 14, 1, ph - 28, M_LINE, M_LINE, 0);

            string[] zoneLabel = { "AFT BALLAST", "BILGE / ER", "FWD BALLAST" };
            string[] zoneTag   = { "Valves_DB_AftConfigured", "Valves_DB_ErConfigured", "Valves_DB_FwdConfigured" };
            int[] zoneMax      = { 28, 28, 40 };

            const int zx0 = 238, zw = 184;
            for (int i = 0; i < 3; i++) {
                int zx = px + zx0 + i * zw;
                MakeTb(sc, "CfgSum_ZLbl" + i, zx, py + 10, zw - 12, 18, zoneLabel[i], M_TRANS, M_MUTED, 0, "Left", 11, true);
                var zVal = MakeLiveText(sc, "CfgSum_ZVal" + i, zx, py + 30, 56, 40, M_TEXT, "Left", 24, true);
                DynTag(zVal, "Text", zoneTag[i]);
                MakeTb(sc, "CfgSum_ZOf" + i, zx + 58, py + 40, 80, 30, "/ " + zoneMax[i], M_TRANS, M_MUTED, 0, "Left", 15, false);
                if (i < 2)
                    MakeRect(sc, "CfgSum_ZDiv" + i, zx + zw - 10, py + 14, 1, ph - 28, M_LINE, M_LINE, 0);
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
                "  Tags(\"ConfirmValveIdx\").Write(no);\n" +
                "  HMIRuntime.UI.SysFct.OpenScreenInPopup(\"Popup_ConfirmDisable\", \"Screen_ConfirmDisable\", false, \" \", " + SX(730) + ", " + SY(430) + ", false);\n" +
                "  return;\n" +
                "}\n" +
                "Tags(vTag+\"_Configured\").Write(false);\n";
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
            // Write FIRST, close LAST: confirmed live that closing before the write silently
            // dropped it - CloseScreenInPopup appears to interrupt/tear down script execution, so
            // anything scheduled after it doesn't reliably run.
            AddScriptEvent(yesBtn,
                JS_READ +
                "let idx=r(Tags(\"ConfirmValveIdx\").Read());\n" +
                "if(!idx) return;\n" +
                "let vTag=\"V\"+(\"000\"+idx).slice(-3);\n" +
                "Tags(vTag+\"_Configured\").Write(false);\n" +
                "HMIRuntime.UI.SysFct[\"CloseScreenInPopup\"](\"Popup_ConfirmDisable\");");

            var noBtn = MakeBtn(sc, "Confirm_No", 240, 150, 160, 46, "CANCEL", Color.FromArgb(255, 55, 65, 81), Color.White, Color.FromArgb(255, 107, 114, 128), 2, 14, true);
            AddScriptEvent(noBtn, "HMIRuntime.UI.SysFct[\"CloseScreenInPopup\"](\"Popup_ConfirmDisable\");");
        }
    }
}
