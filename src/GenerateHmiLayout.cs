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
// valveDemo2 — Full 89-Slot HMI Layout Generator v8
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
        private const int    VALVE_COUNT    = 89;
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
        // Dimmed caption colour for the popup's identity-card field labels, so the labels read as
        // subordinate to the values they describe rather than competing with them.
        private static readonly Color TXT_MUTED  = Color.FromArgb(255, 142, 148, 160);

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
            bool alarmColorsOnly = false;
            bool finishLoginAuth = false;
            string purgeAlarmSuffix = null;
            foreach (var a in args) {
                if (a.StartsWith("--only=")) {
                    only = new HashSet<string>(a.Substring(7).Split(','), StringComparer.OrdinalIgnoreCase);
                } else if (a == "--fix-tags") {
                    fixTags = true;
                } else if (a.StartsWith("--dump-tag=")) {
                    dumpTag = a.Substring(11);
                } else if (a.StartsWith("--export-block=")) {
                    exportBlock = a.Substring(15);
                } else if (a == "--alarm-colors") {
                    alarmColorsOnly = true;
                } else if (a == "--import-only") {
                    importOnly = true;
                } else if (a == "--finish-login-auth") {
                    finishLoginAuth = true;
                } else if (a.StartsWith("--purge-alarms=")) {
                    purgeAlarmSuffix = a.Substring(15);
                }
            }
            if (finishLoginAuth) { try { RunFinishLoginAuth(); } catch (Exception ex) { Console.WriteLine("\n[ERROR] " + ex); } Console.WriteLine("\nDone."); return; }
            if (purgeAlarmSuffix != null) { try { RunPurgeAlarms(purgeAlarmSuffix); } catch (Exception ex) { Console.WriteLine("\n[ERROR] " + ex); } Console.WriteLine("\nDone."); return; }
            try { Run(only, fixTags, dumpTag, exportBlock, importOnly, alarmColorsOnly); }
            catch (Exception ex) { Console.WriteLine("\n[ERROR] " + ex); }
            Console.WriteLine("\nDone."); 
        }

        static bool Want(HashSet<string> only, string key) { return only == null || only.Contains(key); }

        // Deletes every discrete alarm whose name ends with the given suffix.
        // Needed because renaming an alarm does not remove the old one: the generator creates the new
        // name and the previous alarm survives, still bound to the SAME trigger tag and bit. TIA
        // rejects that outright - "the tag/bit combination is also used for other HMI alarms" - so a
        // rename is always a two-step operation: regenerate, then purge the old names.
        // Suffix rather than an exact list so it stays useful for the next rename.
        static void RunPurgeAlarms(string suffix)
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("[ERROR] TIA Portal not running."); return; }
            TiaPortal portal = null; Project project = null;
            foreach (var p in procs) {
                try {
                    var att = p.Attach();
                    if (att != null && att.Projects.Count > 0) { portal = att; project = att.Projects[0]; break; }
                } catch { }
            }
            if (portal == null || project == null) { Console.WriteLine("[ERROR] Could not attach."); return; }
            Console.WriteLine("Attached to Project: " + project.Name);

            Device hmiDevice = FindDeviceByPartialName(project, "HMI");
            if (hmiDevice == null) { Console.WriteLine("[ERROR] HMI device not found."); return; }
            HmiSoftware hmi = FindHmiSoftware(hmiDevice);
            if (hmi == null) { Console.WriteLine("[ERROR] HMI software not found."); return; }

            // Collect first, delete after: mutating the composition while enumerating it is the
            // classic way to silently skip half the entries.
            var doomed = new List<string>();
            foreach (var al in hmi.DiscreteAlarms)
                if (al.Name != null && al.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    doomed.Add(al.Name);

            Console.WriteLine("  Found " + doomed.Count + " alarms ending in '" + suffix + "'.");
            int killed = 0;
            foreach (var name in doomed) {
                try {
                    var al = hmi.DiscreteAlarms.Find(name);
                    if (al != null) { al.Delete(); killed++; }
                } catch (Exception ex) {
                    Console.WriteLine("  [WARN] could not delete " + name + ": " + Root(ex));
                }
            }
            Console.WriteLine("  Deleted " + killed + " of " + doomed.Count + ".");
        }

        static void Run(HashSet<string> only, bool fixTags = false, string dumpTag = null, string exportBlock = null, bool importOnly = false, bool alarmColorsOnly = false)
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
                string outPath = @"C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\temp_export_" + exportBlock + ".xml";
                block.Export(new FileInfo(outPath), ExportOptions.WithDefaults);
                Console.WriteLine("\n[EXPORT-BLOCK] " + exportBlock + " -> " + outPath);
                Console.WriteLine("\n=== Export-block complete! ===");
                return;
            }

            // Recolour the four alarm classes and nothing else. Deliberately ahead of
            // ImportPlcBlocks: this touches four objects, so it has no business spending a
            // minute re-importing PLC blocks or regenerating 721 alarm definitions first.
            if (alarmColorsOnly) {
                Device acDev = FindDeviceByPartialName(project, "HMI");
                if (acDev == null) { Console.WriteLine("[ERROR] HMI device not found."); return; }
                HmiSoftware acHmi = FindHmiSoftware(acDev);
                if (acHmi == null) { Console.WriteLine("[ERROR] HMI software not found."); return; }
                ApplyAlarmClassColors(acHmi);
                Console.WriteLine("\n=== Alarm colours applied! (no other changes) ===");
                return;
            }

            // Import updated PLC blocks (FB_ValveLoop with Configured headroom logic)
            ImportPlcBlocks(project);

            if (importOnly) {
                Console.WriteLine("\n=== Import-only complete! (PLC blocks re-imported, no HMI changes) ===");
                // Save here too. The save at the end of this method covers the normal path, but
                // this branch returns before reaching it - so --import-only was still leaving
                // finished work in TIA's memory only, which is exactly the failure that lost an
                // 80-minute screen build on 2026-08-27. An early return is where that hides.
                SaveProject(project);
                return;
            }

            Device hmiDevice = FindDeviceByPartialName(project, "HMI");
            if (hmiDevice == null) { Console.WriteLine("[ERROR] HMI device not found."); return; }
            HmiSoftware hmi = FindHmiSoftware(hmiDevice);
            if (hmi == null) { Console.WriteLine("[ERROR] HMI software not found."); return; }

            // Create HMI Summary Tags with valid PLC Tag references
            CreateSummaryHmiTags(hmi);

            // Read the real graphic names out of the project before drawing any mimic.
            LoadGraphicCatalog(project);

            // STEP 1 – Rebuild screens for Marine UI Redesign
            Console.WriteLine("\n[STEP 1] Rebuilding Marine screens for 1920x1080 resolution...");
            
            // Clean up legacy screens
            HmiScreen old1 = FindScreen(hmi, "Screen_1");
            if (old1 != null) { try { CleanScreen(old1); old1.Delete(); } catch {} }
            HmiScreen oldP = FindScreen(hmi, "Screen_Popup");
            if (oldP != null) { try { CleanScreen(oldP); oldP.Delete(); } catch {} }

            // Audit Trail needs GMP on at the device level. Cheap, idempotent, and doing it here
            // means a project rebuilt from scratch is audit-ready without a separate manual step.
            EnsureGmpEnabled(hmi);

            if (Want(only, "Home")) {
                HmiScreen scHome = RecreateScreen(hmi, "Screen_Home");
                if (scHome != null) {
                    BuildScreenHome(scHome);
                    // MUST stay inside the Home branch, immediately after the rebuild.
                    // RecreateScreen deletes the screen, taking its event handlers with it, so the
                    // StartAuditLog() call that arms the Audit Trail dies with every Home rebuild.
                    // It previously lived only in the live project (wired by hand via a one-off
                    // tool), which meant any rebuild silently disarmed audit logging with no error
                    // anywhere - the trail just stopped recording. Generating it here keeps it in
                    // source control and makes it survive.
                    WireStartAuditLog(scHome);
                }
            } else Console.WriteLine("  Skipping Screen_Home (not in --only)...");

            // Mimic valves open the SBO popup, so it must exist alongside every screen — always
            // rebuilt regardless of selection (cheap, ~30s, and a universal dependency).
            EnsurePopupScreen(hmi);

            // BuildAlarmScreen already exists (below) and was fully written previously but
            // never actually wired into Run() — the nav bar pointed at a screen that was
            // never created. Enabling it now that the nav is being made fully functional.
            if (Want(only, "Alarms")) EnsureAlarmScreen(hmi);
            else Console.WriteLine("  Skipping Screen_Alarms (not in --only)...");

            // Nav-only: redraw the nav bar on every screen that has one, touching nothing else.
            // Adding the eighth button changed the width of all of them, and BuildNav runs on
            // every screen - so without this, a nav change means regenerating the lot. Screen_Alarms
            // alone is ~45 minutes, because --only=Alarms also re-runs the full 632-alarm
            // generation, and it carries the COM deadlock risk documented in BuildAlarmScreen.
            if (Want(only, "Nav")) PatchNav(hmi);

            // StripLegend-only: delete the zone screens' colour-legend strip in place.
            // The strip was built and rejected on the same day, and rebuilding the three zone
            // screens to drop twelve items would cost ~50 minutes - Aft, Bilge and Fwd each redraw
            // an illustration, its live valve overlays and a 14-row table.
            if (Want(only, "StripLegend")) PatchStripLegend(hmi);
            if (Want(only, "TableButtons")) PatchTableButtons(hmi);

            // AlarmColumns-only: patch columns on the EXISTING AlarmView without deleting the screen.
            // Run this after Pass-2 alarm additions (--only=DiscreteAlarms) to re-apply column config.
            if (Want(only, "AlarmColumns")) PatchAlarmColumns(hmi);

            // The remaining nav targets have no dedicated screen design yet.
            // Rather than leave their nav buttons dead-clicking, each gets a
            if (Want(only, "Bilge")) {
                HmiScreen scBilge = RecreateScreen(hmi, "Screen_Bilge");
                if (scBilge != null) BuildZoneScreen(scBilge, "Screen_Bilge", "BILGE AND FIRE", 28, 54, "Er", 14);
            } else Console.WriteLine("  Skipping Screen_Bilge (not in --only)...");

            if (Want(only, "Fwd")) {
                HmiScreen scFwd = RecreateScreen(hmi, "Screen_FwdBallast");
                if (scFwd != null) BuildZoneScreen(scFwd, "Screen_FwdBallast", "FORWARD BALLAST", 55, 89, "Fwd", 18);
            } else Console.WriteLine("  Skipping Screen_FwdBallast (not in --only)...");

            if (Want(only, "Aft")) {
                HmiScreen scAft = RecreateScreen(hmi, "Screen_AftBallast");
                if (scAft != null) BuildZoneScreen(scAft, "Screen_AftBallast", "AFT BALLAST", 1, 27, "Aft", 14);
            } else Console.WriteLine("  Skipping Screen_AftBallast (not in --only)...");

            if (Want(only, "Diag")) {
                HmiScreen scDiag = RecreateScreen(hmi, "Screen_Diagnostics");
                if (scDiag != null) BuildConfigScreen(scDiag);
                // Screen_ValveEdit is retired (Name/Location editing removed - see
                // editable-hmi-text-fields.md memory file) - delete it outright rather than
                // recreating, so no orphaned popup screen is left behind in the live project.
                HmiScreen scEditOld = FindScreen(hmi, "Screen_ValveEdit");
                if (scEditOld != null) {
                    Console.WriteLine("  Deleting retired Screen_ValveEdit...");
                    try { CleanScreen(scEditOld); scEditOld.Delete(); } catch (Exception ex) {
                        Console.WriteLine("  [WARN] Could not delete Screen_ValveEdit: " + ex.Message);
                    }
                }
                HmiScreen scConfirm = RecreateScreen(hmi, "Screen_ConfirmDisable");
                if (scConfirm != null) BuildConfirmDisableScreen(scConfirm);
            } else Console.WriteLine("  Skipping Screen_Diagnostics (not in --only)...");

            // Discrete alarms can be created/updated independently — does NOT touch Screen_Alarms layout.
            if (Want(only, "Alarms") || Want(only, "DiscreteAlarms")) {
                CreateAlarms(hmi, "Valves_DB");
            }

            // Own --only key: the diagnosis controls are slow to place, and the config screen
            // it is reached from does not need rebuilding to change this one.
            if (Want(only, "SysDiag")) {
                HmiScreen scSys = RecreateScreen(hmi, "Screen_SysDiag");
                if (scSys != null) BuildSysDiagScreen(scSys);
            } else Console.WriteLine("  Skipping Screen_SysDiag (not in --only)...");

            if (Want(only, "Login")) {
                HmiScreen scLogin = RecreateScreen(hmi, "Screen_Login");
                if (scLogin != null) BuildAuditLogScreen(scLogin);
            } else Console.WriteLine("  Skipping Screen_Login (not in --only)...");

            Console.WriteLine("\n=== Complete! ===");

            // Save. This was missing until 2026-08-27 and it cost an 80-minute build: the zone
            // screens were rebuilt at 03:31-04:53, TIA crashed later that morning, and because
            // nothing here ever called Save the entire run existed only in TIA's memory. The
            // project reopened at its last MANUAL save (02:27) with the work gone.
            //
            // Every build this project has ever done carried that exposure - Home and the nav bar
            // survive today only because somebody happened to press save in the UI afterwards.
            // A builder that spends an hour writing to a project it never persists is a builder
            // that loses an hour to any crash, and TIA has crashed repeatedly during this work.
            //
            // Deliberately last, after every --only branch and patch, so it covers all of them.
            // Failure is reported loudly rather than swallowed: a silent save failure would put us
            // straight back to believing work is on disk when it is not.
            SaveProject(project);
            Console.WriteLine("Screens: Screen_Home, Screen_Popup, Screen_Alarms, Screen_Bilge, Screen_FwdBallast, Screen_AftBallast, Screen_Diagnostics, Screen_SysDiag, Screen_Login (AUDIT LOG)");
            Console.WriteLine("All 8 nav bar buttons target real screens, DIAGNOSTICS included.");
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
        // Deletes the existing Nav_* items on each screen and calls BuildNav to lay them out
        // again. Reusing BuildNav rather than editing buttons individually is the whole point:
        // a hand-rolled patcher would have to resize seven buttons, retarget two of them, add an
        // eighth and move the active-highlight index on four different screens - four chances to
        // get something subtly wrong that only shows up when somebody taps the wrong tab. This way
        // the patched nav and a freshly built one come from the same code by construction.
        //
        // Screens are found by looking for a Nav_0, so popups and any screen without a nav bar are
        // skipped without needing a list to keep in step.
        // One save, called from every path that can finish work: the normal end of Run() and the
        // --import-only early return. Failure is reported loudly rather than swallowed - a silent
        // save failure would recreate the belief that just proved false, that work is on disk.
        static void SaveProject(Project project)
        {
            try {
                Console.WriteLine("Saving project...");
                project.Save();
                Console.WriteLine("[SAVE] Project saved.");
            } catch (Exception ex) {
                Console.WriteLine("[SAVE ERR] PROJECT WAS NOT SAVED - " + Root(ex));
                Console.WriteLine("[SAVE ERR] Save manually in TIA before closing, or this run is lost.");
            }
        }

        static void PatchNav(HmiSoftware hmi)
        {
            Console.WriteLine("\n[Nav] Redrawing nav bars in place...");
            int done = 0;
            foreach (var sc in hmi.Screens) {
                var navItems = new System.Collections.Generic.List<object>();
                bool hasNav = false;
                foreach (var item in sc.ScreenItems) {
                    if (!item.Name.StartsWith("Nav_", StringComparison.Ordinal)) continue;
                    hasNav = true;
                    navItems.Add(item);
                }
                if (!hasNav) { Console.WriteLine("  " + sc.Name + " - no nav bar, skipped"); continue; }

                int killed = 0;
                foreach (var item in navItems) {
                    try { ((dynamic)item).Delete(); killed++; } catch (Exception ex) {
                        Console.WriteLine("  [WARN] " + sc.Name + ": could not delete an item: " + Root(ex));
                    }
                }
                // The nav background is drawn by BuildNav too, so it goes with them.
                foreach (var item in sc.ScreenItems) {
                    if (item.Name != "Nav_BG") continue;
                    try { item.Delete(); killed++; } catch { }
                    break;
                }

                // activeTarget is the screen's own name - a screen whose name matches no nav
                // target simply gets eight live buttons, which is correct for Screen_SysDiag
                // before it had a tab of its own.
                BuildNav(sc, sc.Name);
                Console.WriteLine("  " + sc.Name + " - " + killed + " old item(s) replaced");
                done++;
            }
            Console.WriteLine("[Nav] " + done + " screen(s) updated.");
        }

        // Removes the horizontal colour legend (LegR_Chip*/LegR_Txt*) from every screen carrying
        // one. Matches on the name prefix rather than a screen list so it is self-limiting: screens
        // that never had a strip are skipped, and running it twice is harmless.
        //
        // Deliberately a deletion pass and not a rebuild. Everything else on those screens - the
        // artwork, the valve overlays, the table, the station-offline banner - is correct and took
        // most of an hour to draw.

        // ── Patch: blank the command buttons on EMPTY table rows ────────────────────────
        // Reported from the panel 2026-08-30: the last page of a zone list (FWD page 3 carries 7
        // of 14 rows, BILGE page 2 carries 13) and CONFIG page 6 of 6 (9 of 16) drew full-strength
        // OPEN/CLOSE and DISABLED buttons on every unused row.
        //
        // Never dangerous - AddSlotCmdScript reads the slot's NO. tag and returns on zero, and the
        // Config toggle does the same - but a button that looks pressable and does nothing teaches
        // an operator that buttons sometimes do not work, which is not a lesson worth teaching on
        // a panel that strokes ballast valves.
        //
        // Runs as a PATCH rather than a screen rebuild: it touches ~100 existing buttons and
        // nothing else - no screen deleted, no artwork re-placed, no tags, no alarms, no PLC
        // import. The equivalent full rebuild of four screens is minutes of work to change three
        // colour properties.
        //
        // The same fix is ALSO in the creation path (BuildValveTable's value-map constants and
        // AddConfigToggleTextAndColor). That duplication is deliberate and item 17 is why: the
        // Authorization property was once applied only by a repair pass over live objects, so
        // every later zone-screen rebuild silently wiped it - which really happened on 2026-08-15
        // and left 28 table command buttons unprotected. A patch that is not mirrored in the
        // builder is a fix with an expiry date.
        static void PatchTableButtons(HmiSoftware hmi)
        {
            Console.WriteLine();
            Console.WriteLine("[TableButtons] Blanking command buttons on empty table rows...");

            string[][] zones = {
                new[] { "Screen_AftBallast", "Aft" },
                new[] { "Screen_Bilge",      "Er"  },   // the Bilge screen's PLC window is Er*
                new[] { "Screen_FwdBallast", "Fwd" },
            };

            int total = 0;
            foreach (var z in zones) {
                var sc = FindScreen(hmi, z[0]);
                if (sc == null) { Console.WriteLine("  [WARN] " + z[0] + " not found - skipped."); continue; }
                int n = 0;
                for (int col = 0; col < 2; col++) {
                    for (int r = 0; r < 7; r++) {
                        int slot = col * 7 + r + 1;
                        string sfx = "_" + col + "_" + r;
                        string stateTag = z[1] + "_TblState_" + slot;
                        n += RewireCmdButton(sc, "Tr_Open"  + sfx, stateTag, true)  ? 1 : 0;
                        n += RewireCmdButton(sc, "Tr_Close" + sfx, stateTag, false) ? 1 : 0;
                    }
                }
                Console.WriteLine("  " + z[0] + ": " + n + "/28 command buttons rewired.");
                total += n;
            }

            // CONFIG lives on Screen_Diagnostics. Its toggle is script-driven, not value-mapped -
            // the source is a Bool and the mapping table is int-keyed - so the fix there is to
            // re-apply the (now slot-guarded) scripts rather than to edit a mapping.
            var scCfg = FindScreen(hmi, "Screen_Diagnostics");
            if (scCfg == null) {
                Console.WriteLine("  [WARN] Screen_Diagnostics not found - CONFIG toggles not patched.");
            } else {
                int n = 0;
                for (int r = 0; r < 16; r++) {
                    var btn = FindItemByName(scCfg, "CfgTr_Toggle_" + r) as HmiButton;
                    if (btn == null) continue;
                    RemoveDyn(btn, "Text");
                    RemoveDyn(btn, "BackColor");
                    RemoveDyn(btn, "BorderColor");
                    AddConfigToggleTextAndColor(btn, r + 1);   // slot is 1-based
                    n++;
                }
                Console.WriteLine("  Screen_Diagnostics: " + n + "/16 CONFIG toggles rewired.");
                total += n;
            }

            Console.WriteLine("[TableButtons] " + total + " buttons patched.");
        }

        // Re-applies all three colour maps on one command button, including code 9 (empty slot)
        // and the BorderColor map that never existed. Remove-then-add: Dynamizations.Create<T>
        // throws on a property that already carries one.
        static bool RewireCmdButton(HmiScreen sc, string name, string stateTag, bool isOpen)
        {
            var btn = FindItemByName(sc, name) as HmiButton;
            if (btn == null) return false;
            RemoveDyn(btn, "BackColor");
            RemoveDyn(btn, "ForeColor");
            RemoveDyn(btn, "BorderColor");
            AddValueMap(DynTag(btn, "BackColor", stateTag),
                        isOpen ? FILL_OPEN_CODES : FILL_CLOSE_CODES,
                        isOpen ? FILL_OPEN : FILL_CLOSE);
            AddValueMap(DynTag(btn, "ForeColor", stateTag),
                        LOCK_CODES, isOpen ? LOCK_OPEN_FORE : LOCK_CLOSE_FORE);
            AddValueMap(DynTag(btn, "BorderColor", stateTag), EMPTY_CODES, EMPTY_TRANSPARENT);
            return true;
        }

        static object FindItemByName(HmiScreen sc, string name)
        {
            foreach (var it in sc.ScreenItems)
                if (it.Name.Equals(name, StringComparison.Ordinal)) return it;
            return null;
        }

        static void PatchStripLegend(HmiSoftware hmi)
        {
            Console.WriteLine();
            Console.WriteLine("[StripLegend] Removing zone colour-legend strips...");
            int screens = 0, total = 0;
            foreach (var sc in hmi.Screens) {
                // Collect first, delete after: mutating ScreenItems while enumerating it is what
                // makes a patch skip every second item.
                var doomed = new System.Collections.Generic.List<object>();
                foreach (var item in sc.ScreenItems) {
                    if (item.Name.StartsWith("LegR_", StringComparison.Ordinal)) doomed.Add(item);
                }
                if (doomed.Count == 0) continue;

                int killed = 0;
                foreach (var item in doomed) {
                    try { ((dynamic)item).Delete(); killed++; }
                    catch (Exception ex) {
                        Console.WriteLine("  [WARN] " + sc.Name + ": " + ((dynamic)item).Name + " - " + Root(ex));
                    }
                }
                Console.WriteLine("  " + sc.Name + " - " + killed + " of " + doomed.Count + " legend item(s) removed");
                screens++; total += killed;
            }
            if (screens == 0) Console.WriteLine("  No legend strips found - nothing to do.");
            else Console.WriteLine("[StripLegend] " + total + " item(s) removed across " + screens + " screen(s).");
        }

        static void PatchAlarmColumns(HmiSoftware hmi)
        {
            Console.WriteLine("  [AlarmColumns] Finding existing Screen_Alarms...");
            HmiScreen sc = FindScreen(hmi, "Screen_Alarms");
            if (sc == null) { Console.WriteLine("  [AlarmColumns] Screen_Alarms not found — run --only=Alarms first."); return; }
            foreach (var item in sc.ScreenItems) {
                if (item.Name == "AlarmView" && item is HmiAlarmControl) {
                    Console.WriteLine("  [AlarmColumns] Found AlarmView. Applying column config...");
                    ConfigureAlarmColumns((HmiAlarmControl)item);
                    // Chrome too, on the same fast path. Rebuilding Screen_Alarms to change one
                    // flag would mean re-placing HmiAlarmControl, which takes minutes and carries
                    // the COM deadlock risk documented in BuildAlarmScreen.
                    StripChrome(item);
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
            // Redesigned 2026-08-17 after review on the panel. Four problems with the old layout:
            //   1. Position, health and mode were crammed into ONE text line, which truncated -
            //      "REMOTE" fell off the end entirely on a faulted valve.
            //   2. Position had no visual weight. It is the single most important fact about a
            //      ballast valve (open = flooding path, closed = blocked line) and it read as a
            //      fragment mid-sentence, the same size as everything else.
            //   3. The VALVE TAG card spent a whole row on one value. The tag now sits in the
            //      header beside the CM number, which buys that row back.
            //   4. CmdPos was computed in the PLC and never displayed, so a stuck limit switch -
            //      valve reports CLOSED, operator asked for OPEN, nothing technically "faulted" -
            //      was invisible.
            //
            // Layout: header 0..40 | position block 52..152 (circle left, big word right)
            //         fault row 164..196 | mode row 198..224 | open/close 236..288
            //         reset/service 300..348 | 32px bottom margin.
            SetPropUInt(sc, "Width", (uint)SX(460));
            SetPropUInt(sc, "Height", (uint)SY(380));
            sc.BackColor = BG_DARK;

            // Outer canvas
            MakeRect(sc, "Pop_BG", 0, 0, 460, 380, BG_DARK, BORDER, 2);

            // ─── HEADER (Y=0..40) ─────────
            MakeRect(sc, "Pop_Header", 0, 0, 460, 40, BG_HEADER, BORDER, 1);

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
                    // Titles by the client's CM number, not the array slot: the two are NOT the
                    // same. Their schedule skips numbers (no CM22, no CM73-76) and runs to CM97,
                    // so slot and CM number only coincide for the first 21 valves and drift apart
                    // after that. Falls back to the slot when CmNo is still blank (the whole fleet,
                    // until the confirmed schedule is entered) so the header never renders empty,
                    // and says so explicitly rather than showing a slot number that could be
                    // mistaken for a CM number.
                    // Both identities on one line: the CM number the crew uses, and the valve tag
                    // from the schedule. "CONTROL PANEL" was dropped - the operator opened this
                    // popup deliberately and does not need to be told what it is; the room it
                    // freed goes to the tag instead.
                    "let cm = readTag(Tags(\"Valve_Meta_DB_SelCmNo\").Read());\n" +
                    "let vt = readTag(Tags(\"Valve_Meta_DB_SelVTag\").Read());\n" +
                    "let idx = readTag(Tags(\"Valves_DB_SelIdx\").Read()) || 0;\n" +
                    "let head = (cm && cm !== \"\") ? cm : (\"VALVE \" + idx + \" (NO CM TAG)\");\n" +
                    "if (vt && vt !== \"\") head = head + \"      ·      \" + vt;\n" +
                    "return head;";
                tDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}

            // ─── IDENTITY CARD (Y=46..122): the client's schedule fields ─────────────
            // Replaces the old one-line "V-001 | STATE | HEALTH | MODE" strip. Two of those four
            // were pure duplication (the valve number repeats the title; the state repeats the big
            // circle and its label below), but HEALTH and MODE appeared nowhere else on the popup,
            // so they moved down onto the state label rather than being dropped - MODE especially,
            // since the client explicitly asked that a valve in manual mode reads on screen as not
            // remotely operable. The freed space now carries the reference data an operator
            // actually can't get anywhere else on this popup.
            // VALVE TAG only. SYSTEM / LOCATION / FUNCTION were dropped on request - they are
            // reference data the operator can read off the zone valve table, and each one cost a
            // polled T500ms script here (the tag name is computed from SelectedValve, so these
            // cannot bind natively). Removing three of the four cut the popup's polling load by
            // roughly a fifth, which is most of why it was slow to populate on open.
            // ─── POSITION BLOCK (Y=52..152) — the hero of the popup ────────────────
            // Big word beside the circle. An operator looking at a faulted valve reads the POSITION
            // first and the fault second, which is the right order: position decides whether this
            // is a flooding path or a blocked line.
            MakeRect(sc, "Pop_PosCard", 15, 52, 430, 100, BG_CARD, BORDER, 1);

            // TextBox, not MakeLiveText: that helper creates an HmiButton, which has no alignment
            // property at all - its text is always centred and the align argument is silently
            // dropped. These need to sit left, against the circle.
            var posText = MakeTb(sc, "Pop_PosText", 142, 74, 292, 42, "", BG_CARD, Color.White, 0, "Left", 27, true);
            Dyn(posText, "Text",
                JS_READ +
                "var code = r(Tags(\"Valves_DB_SelPosCode\").Read());\n" +
                "var names = [\"UNCONFIGURED\", \"\", \"\", \"FULLY OPEN\", \"FULLY CLOSED\", \"NO POSITION\", \"OPENING\", \"CLOSING\"];\n" +
                "return names[code] || \"NO POSITION\";\n", "AutomaticTags");
            Dyn(posText, "ForeColor",
                JS_READ +
                "var code = r(Tags(\"Valves_DB_SelPosCode\").Read());\n" +
                "if (code === 3) return 0xFF32C785;\n" +
                "if (code === 4) return 0xFFD8DEE6;\n" +
                "if (code === 0) return 0xFF8E8E93;\n" +
                "return 0xFF00A2FF;\n", "AutomaticTags");

            // Commanded-vs-actual. Blank unless they genuinely disagree — an always-on line saying
            // the obvious trains the operator to ignore it. This is what catches a stuck limit
            // switch, where nothing is "faulted" but the valve is not where it was told to go.
            var cmdText = MakeTb(sc, "Pop_CmdText", 142, 118, 292, 24, "", BG_CARD, Color.FromArgb(255, 226, 168, 0), 0, "Left", 13, true);
            Dyn(cmdText, "Text",
                JS_READ +
                "var cmd  = r(Tags(\"Valves_DB_SelCmdPos\").Read());\n" +
                "var code = r(Tags(\"Valves_DB_SelPosCode\").Read());\n" +
                "if (!cmd) return \"\";\n" +
                "if (code === 6 || code === 7) return \"\";\n" +   // still travelling, not yet a disagreement
                "var want = (cmd === 1) ? 3 : 4;\n" +
                "if (code === want) return \"\";\n" +
                "return \"COMMANDED:  \" + ((cmd === 1) ? \"OPEN\" : \"CLOSED\");\n", "AutomaticTags");

            // ─── OPEN / CLOSE Buttons (Y=136..184) ─────────────────────────────────
            var btnOpen = sc.ScreenItems.Create<HmiButton>("Btn_Open");
            btnOpen.Left = SX(20); btnOpen.Top = SY(236); btnOpen.Width = (uint)SX(200); btnOpen.Height = (uint)SY(52);
            btnOpen.BackColor = Color.FromArgb(255, 16, 185, 129); btnOpen.ForeColor = Color.White;
            btnOpen.BorderColor = Color.FromArgb(255, 52, 211, 153); btnOpen.BorderWidth = 2;
            SetMLText(btnOpen, "Text", "▲ OPEN VALVE");
            SetStr(btnOpen, "Authorization", "Operate");
            AddPopupActionButton(btnOpen, "OpenCmd");
            AddRemoteLockStyling(btnOpen, 0xFF10B981);

            var btnClose = sc.ScreenItems.Create<HmiButton>("Btn_Close");
            btnClose.Left = SX(240); btnClose.Top = SY(236); btnClose.Width = (uint)SX(200); btnClose.Height = (uint)SY(52);
            btnClose.BackColor = Color.FromArgb(255, 55, 65, 81); btnClose.ForeColor = Color.White;
            btnClose.BorderColor = Color.FromArgb(255, 107, 114, 128); btnClose.BorderWidth = 2;
            SetMLText(btnClose, "Text", "▼ CLOSE VALVE");
            SetStr(btnClose, "Authorization", "Operate");
            AddPopupActionButton(btnClose, "CloseCmd");
            AddRemoteLockStyling(btnClose, 0xFF374151);

            // ─── Large Status Circle (Diameter=90, Y=195..285) ──
            var dot = sc.ScreenItems.Create<HmiEllipse>("Pop_Dot");
            // Moved into the position card, left of the big word, so colour and text read as one
            // unit instead of the circle floating alone in the middle of the popup.
            dot.CenterX = SX(82); dot.CenterY = SY(102); dot.RadiusX = (uint)SX(38); dot.RadiusY = (uint)SY(38);
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
                    // The flash now alternates red against the valve's POSITION colour rather than
                    // against a near-black dark red. The dark half previously conveyed nothing;
                    // now a faulted-open valve pulses red/green and a faulted-closed one pulses
                    // red/grey, so the circle carries fault AND position in one element.
                    "let code  = readTag(Tags(\"Valves_DB_SelPosCode\").Read());\n" +
                    "let flt   = readTag(Tags(\"Valves_DB_SelFaultCode\").Read());\n" +
                    "let local = readTag(Tags(\"Valves_DB_SelLocalMode\").Read());\n" +
                    "let flash = readTag(Tags(\"Valves_DB_Clock1Hz\").Read());\n\n" +
                    "if (code === 0) return 0xFF8E8E93;\n" +
                    "let pos = 0xFF00A2FF;\n" +
                    "if (code === 3) pos = 0xFF32C785;\n" +
                    "if (code === 4) pos = 0xFF4B5563;\n" +
                    "if (flt > 0) return flash ? 0xFFFF0000 : pos;\n" +
                    "if (local) return 0xFFFF9F0A;\n" +
                    "return pos;\n";
                dotDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}

            // ─── FAULT ROW (Y=164..196) ────────────────────────────────────────────
            // Its own row, so nothing truncates the way the old three-in-one line did. Blank when
            // healthy rather than printing "HEALTHY": the absence of a red row is a cleaner signal
            // than a reassuring word, and it means anything appearing here is worth reading.
            var faultRow = MakeTb(sc, "Pop_FaultRow", 20, 164, 420, 32, "", BG_DARK,
                                  Color.FromArgb(255, 240, 90, 78), 0, "Left", 15, true);
            Dyn(faultRow, "Text",
                JS_READ +
                "var flt = r(Tags(\"Valves_DB_SelFaultCode\").Read());\n" +
                "var f = [\"\", \"UNHEALTHY — ACTUATOR FAULT\", \"DOUBLE INDICATION — BOTH LIMITS MADE\", \"FAIL TO OPEN\", \"FAIL TO CLOSE\", \"LOSS OF POSITION\", \"UNEXPECTED MOVEMENT\", \"DIRECTION / LIMIT FAULT\"];\n" +
                "if (!flt) return \"\";\n" +
                "return \"\\u26A0  \" + (f[flt] || \"FAULT\");\n", "AutomaticTags");

            // ─── MODE ROW (Y=198..224) ─────────────────────────────────────────────
            var modeRow = MakeTb(sc, "Pop_ModeRow", 20, 198, 420, 26, "", BG_DARK, TXT_MUTED, 0, "Left", 13, true);
            Dyn(modeRow, "Text",
                JS_READ +
                "var local = r(Tags(\"Valves_DB_SelLocalMode\").Read());\n" +
                "var cfg = r(Tags(\"Valves_DB_SelConfigured\").Read());\n" +
                "if (!cfg) return \"OUT OF SERVICE — NOT CONFIGURED\";\n" +
                // REMOTE/LOCAL rather than AUTO/LOCAL: the client's requirement is that a valve
                // under hand control reads as not remotely operable, and "AUTO" would imply
                // automatic sequencing this system does not do.
                "return local ? \"LOCAL — REMOTE COMMANDS LOCKED\" : \"REMOTE\";\n", "AutomaticTags");
            Dyn(modeRow, "ForeColor",
                JS_READ +
                "var local = r(Tags(\"Valves_DB_SelLocalMode\").Read());\n" +
                "var cfg = r(Tags(\"Valves_DB_SelConfigured\").Read());\n" +
                "return (local || !cfg) ? 0xFFE2A800 : 0xFF9AA3B0;\n", "AutomaticTags");

            // ─── RESET FAULT Button (Left=15, Y=292, Width=138, Height=46) ───────────
            var btnReset = sc.ScreenItems.Create<HmiButton>("Btn_Reset");
            btnReset.Left = SX(15); btnReset.Top = SY(300); btnReset.Width = (uint)SX(210); btnReset.Height = (uint)SY(46);
            btnReset.BackColor = Color.FromArgb(255, 194, 65, 12); btnReset.ForeColor = Color.White;
            btnReset.BorderColor = Color.FromArgb(255, 249, 115, 22); btnReset.BorderWidth = 2;
            SetMLText(btnReset, "Text", "⚡ RESET FAULT");
            SetFont(btnReset, SFont(12), true);
            SetStr(btnReset, "Authorization", "Operate");
            AddPopupActionButton(btnReset, "ResetFault");

            // ─── SERVICE ON/OFF Toggle Switch (Left=160, Y=292, Width=138, Height=46) ──
            var btnService = sc.ScreenItems.Create<HmiButton>("Btn_Service");
            btnService.Left = SX(235); btnService.Top = SY(300); btnService.Width = (uint)SX(210); btnService.Height = (uint)SY(46);
            btnService.BackColor = Color.FromArgb(255, 58, 58, 60); btnService.ForeColor = Color.White;
            btnService.BorderColor = TEAL; btnService.BorderWidth = 2;
            SetMLText(btnService, "Text", "🛠️ SERVICE:  OFF");
            SetFont(btnService, SFont(12), true);
            SetStr(btnService, "Authorization", "Operate");
            AddPopupActionButton(btnService, "ToggleService");
            try {
                var srvDyn = btnService.Dynamizations.Create<ScriptDynamization>("Text");
                srvDyn.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let cfg = readTag(Tags(\"Valves_DB_SelConfigured\").Read());\n" +
                    "return cfg ? \"🛠️ SERVICE:  ON\" : \"🛠️ SERVICE:  OFF\";\n";
                // AutomaticTags, not T500ms: this reads the fixed Valves_DB_SelConfigured mirror tag now,
                // not a name computed from SelectedValve, so the dependency is trackable.
                srvDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}
            try {
                var srvBg = btnService.Dynamizations.Create<ScriptDynamization>("BackColor");
                srvBg.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let cfg = readTag(Tags(\"Valves_DB_SelConfigured\").Read());\n" +
                    "return cfg ? 0xFF00C7BE : 0xFF3A3A3C;\n";
                srvBg.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}

            // SIMULATE STUCK button removed on request 2026-08-15. It was simulation-only
            // scaffolding (it drives Valves_DB.Stuck[i], which only the simulated-travel branch of
            // FB_ValveLoop reads) and is meaningless once a valve runs on real I/O - a real valve
            // that fails to travel latches Fail-to-Open/Close from the genuine timeout instead.
            // Its two T500ms scripts went with it. The Stuck member and the ToggleStuck action
            // handler are left in place, unused and inert, so simulated-valve testing still works
            // from a watch table if it is ever needed again.

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
            // LoggedAlarms (2) is a one-shot static snapshot query, not a live view - confirmed via
            // the HmiAlarmSourceType enum in the installed DLL. Clicking it re-queries once at that
            // instant; if the query lands before the log's WAL has flushed, it silently returns a
            // partial read that then never updates - the "sometimes shows everything, sometimes
            // shows one row" symptom. LoggedAlarmsUpdated (3) is the continuously-refreshing variant.
            AddScriptEvent(btnHist, "Screen.Items(\"AlarmView\").AlarmSourceType = 3;");

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
                // NO FILTER, deliberately. This previously excluded everything except
                // ValveFault, ValveWarning, ValveEvent and System, to keep native WinCC runtime
                // alarms out of the operator's list. That judgement was wrong twice over.
                //
                // Wrong on names: controller diagnostics arrive in Siemens' built-in classes -
                // SystemAlarm, SystemWarning, SystemInformation, SystemNotification and the two
                // WithoutClearEvent variants. Our custom class is called plain "System", which
                // matches none of them. So the filter would have dropped every station-failure
                // alarm, silently, including the ones the newly enabled "System diagnostics"
                // setting exists to produce.
                //
                // Wrong on intent: the alarms it excluded are ones a ship's operator needs.
                // PlcDisconnectedAlarm means the valves have stopped answering. The storage
                // alarms are what revealed, on 2026-08-24, that both logs had stopped recording
                // while everything else looked normal - nothing else reported that at all.
                //
                // Left empty rather than listing every class, because an empty filter cannot
                // hide anything by accident and a misspelt class name silently can. If the list
                // ever needs narrowing, exclude named classes rather than allow-listing.
                alarmCtrl.Filter = "";
                Console.WriteLine("  [DEBUG] HmiAlarmControl placed. Configuring columns...");
                Console.Out.Flush();
                ConfigureAlarmColumns(alarmCtrl);
                StripChrome(alarmCtrl);
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
                    // logged in. Real "who did what" tracking lives in the Audit Trail instead:
                    // command tags are flagged HmiTag.GmpRelevant and WinCC logs every operator
                    // change itself, verified 2026-08-23 by reading AUTDB directly.
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
            BuildHeaderBar(sc, "Valve Control System — 89 Slots Overview", true);
            BuildSummaryBar(sc);

            // Place VALVE_COUNT cards
            Console.WriteLine("  Placing " + VALVE_COUNT + " interactive valve buttons...");
            for (int v = 1; v <= VALVE_COUNT; v++) {
                if (v == 1 || v % 10 == 0) Console.WriteLine("    -> Valve " + v + " of " + VALVE_COUNT + "...");
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
                            "return " + VALVE_COUNT + " - (o + c + t + f + l);";
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
                        "Tags(vTag + \"_OpenCmd\").Write(true);\n";
                } else if (action == "CloseCmd") {
                    scriptBody =
                        helper +
                        "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                        "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                        "let cfg = readTag(Tags(vTag + \"_Configured\").Read());\n" +
                        "if (!cfg) return;\n" +
                        "Tags(vTag + \"_OpenCmd\").Write(false);\n" +
                        "Tags(vTag + \"_CloseCmd\").Write(true);\n";
                } else if (action == "ResetFault") {
                    // Clears the LATCHED alarms only. It must never write OpenFB/ClosedFB/Healthy:
                    // those four feedbacks (plus LocalMode) are valve-owned signals - the valve
                    // reports its real physical position on them, including while it is being
                    // operated by hand in Local Mode. The PLC and HMI read them, never drive them.
                    //
                    // An earlier version wrote Healthy:=true, OpenFB:=false, ClosedFB:=false here.
                    // On a simulated valve that broke a double-indication condition deliberately.
                    // On a REAL valve it was actively harmful: writing ClosedFB:=false on a valve
                    // that is genuinely closed makes FB_ValveLoop see a limit switch drop with no
                    // command running on the very next scan, which latches Unexpected Movement -
                    // so pressing "reset fault" manufactured a fresh fault. FC_IoMapper then
                    // restored the real value later in that same scan, leaving only the bogus
                    // alarm behind. Removed 2026-08-15.
                    //
                    // A condition that is still physically true cannot be acknowledged away, and
                    // should not be: double-indication re-trips while both limits are made, and
                    // Loss of Position (PosLostTmr) re-arms while the valve genuinely has no
                    // position. Both clear on their own once the valve reports a real position.
                    scriptBody =
                        helper +
                        "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                        "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                        "Tags(vTag + \"_TimeoutOpenAlarm\").Write(false);\n" +
                        "Tags(vTag + \"_TimeoutCloseAlarm\").Write(false);\n" +
                        "Tags(vTag + \"_UnexpMove\").Write(false);\n" +
                        // Direction/limit discrepancy (H) is latched like the three above, so it
                        // clears the same way. If the wiring really is crossed it re-latches within
                        // the 5s grace on the next command - correct, and the same self-re-trip
                        // behaviour double-indication and Loss of Position already have.
                        "Tags(vTag + \"_DirFault\").Write(false);\n";
                } else if (action == "ToggleService") {
                    // SERVICE and CONFIGURED are the same underlying flag (this toggle just flips
                    // vTag+"_Configured", same as the Config screen's toggle) - reusing the exact
                    // same Screen_ConfirmDisable/ConfirmValveIdx mechanism already built for that,
                    // rather than a second copy of the same popup. Turning ON is unchanged; turning
                    // OFF now checks live state first, same as the Config screen's toggle does.
                    scriptBody =
                        helper +
                        "let idx = readTag(Tags(\"SelectedValve\").Read());\n" +
                        "let vTag = \"V\" + (\"000\" + (idx || 1)).slice(-3);\n" +
                        "let cur = readTag(Tags(vTag + \"_Configured\").Read());\n" +
                        "if (!cur) {\n" +
                        "  Tags(vTag + \"_Configured\").Write(true);\n" +
                        "  Tags(vTag + \"_Healthy\").Write(true);\n" +
                        "} else {\n" +
                        "  let st = readTag(Tags(vTag + \"_State\").Read());\n" +
                        "  if (st === 3 || st === 5) {\n" +
                        "    Tags(\"ConfirmValveIdx\").Write(idx || 1);\n" +
                        "    HMIRuntime.UI.SysFct.OpenScreenInPopup(\"Popup_ConfirmDisable\", \"Screen_ConfirmDisable\", false, \" \", " + SX(730) + ", " + SY(430) + ", false);\n" +
                        "  } else {\n" +
                        "    Tags(vTag + \"_Configured\").Write(false);\n" +
                        "  }\n" +
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
                // The 730/340 position centers the popup on its parent screen — computed for a
                // 1920x1080 parent ((1920-460)/2=730, (1080-400)/2=340; the Y moved from 360 when
                // the popup grew from 360 to 400 tall for the identity card). Both the parent canvas
                // and the popup's own size now scale by SX()/SY() (1366x768 target), so the position
                // must scale the same way to stay centered instead of drifting toward one corner.
                // Popup_Valve is meant to be freely retargetable: tapping a different valve tile
                // while it's already open should just re-point the same popup at the new valve. An
                // AnyPopupOpen-style guard was briefly tried here and confirmed live to break exactly
                // that - it opened once, then never again, since nothing besides the X button ever
                // reset the flag and retargeting doesn't go through the X button at all. No guard
                // needed on any of the 3 popups in the end: Edit/Confirm are modal, which already
                // makes stacking structurally impossible without extra tracking.
                // Two writes on purpose. SelectedValve stays an HMI-internal tag and is what the
                // popup's ACTION scripts (open/close/reset/service) read to know which valve to
                // command. Valves_DB_SelIdx is the PLC-side copy: FB_ValveLoop mirrors that valve's
                // live fields into fixed Sel* tags so the popup's DISPLAY elements can bind to
                // static tag names and run on AutomaticTags instead of polling at T500ms.
                string jsCode =
                    "Tags(\"SelectedValve\").Write(" + vIndex + ");\n" +
                    "Tags(\"Valves_DB_SelIdx\").Write(" + vIndex + ");\n" +
                    "HMIRuntime.UI.SysFct.OpenScreenInPopup(\"Popup_Valve\", \"Screen_Popup\", false, \" \", " + SX(730) + ", " + SY(340) + ", false);";

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
                    "for (let i = 1; i <= " + VALVE_COUNT + "; i++) {\n" +
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

        // Confirmed against the client's actual BOM: 6AG1214-1AG40-4XB0 (SIPLUS S7-1200 CPU 1214C
        // DC/DC/DC), based on the standard 6ES7 214-1AG40-0XB0 - not the 1215C the project was
        // originally built around. The 1214C has only 1 onboard PROFINET port (vs the 1215C's 2),
        // so the PLC->ET200SP->HMI topology needs an external PROFINET switch - a physical/BOM
        // concern, nothing this script can express. Idempotent: does nothing if already correct,
        // so safe to call on every run rather than being a one-off manual Openness call whose
        // effect would otherwise only live in the saved project, with no record in source.
        static void EnsurePlcCpuType(Device plcDevice, string requiredTypeIdentifier)
        {
            try {
                DeviceItem plc1 = null;
                foreach (DeviceItem item in plcDevice.DeviceItems)
                    if (item.Name == "PLC_1") { plc1 = item; break; }
                if (plc1 == null) { Console.WriteLine("  [PLC] PLC_1 device item not found - skipping CPU type check."); return; }

                if (string.Equals(plc1.TypeIdentifier, requiredTypeIdentifier, StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine("  [PLC] CPU type already correct (" + plc1.TypeIdentifier + ")");
                    return;
                }

                Console.WriteLine("  [PLC] CPU type is " + plc1.TypeIdentifier + ", changing to " + requiredTypeIdentifier + "...");
                var m = plc1.GetType().GetMethod("ChangeType", new Type[] { typeof(string) });
                if (m == null) { Console.WriteLine("  [PLC] [ERROR] ChangeType method not found."); return; }
                m.Invoke(plc1, new object[] { requiredTypeIdentifier });
                Console.WriteLine("  [PLC] CPU type changed successfully to " + plc1.TypeIdentifier);
            } catch (Exception ex) {
                Console.WriteLine("  [PLC] [ERROR] Could not verify/change CPU type: " + ex.Message);
            }
        }

        // Dims OPEN/CLOSE whenever the valve can't actually be driven from here - either it's in
        // LOCAL mode (someone has taken hand control at the valve) or it isn't configured at all.
        // The PLC already refuses those commands, but until now nothing on screen said so, and the
        // buttons stayed fully lit and clickable. This is the visible half of the client's
        // requirement that a valve in manual mode reads as not remotely operable.
        // Colour only - the button stays clickable and the PLC interlock remains the real guard;
        // the styling must never be mistaken for the safety mechanism.
        static void AddRemoteLockStyling(HmiButton btn, uint activeBackColor)
        {
            string guard =
                "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                "let local = readTag(Tags(\"Valves_DB_SelLocalMode\").Read());\n" +
                "let cfg = readTag(Tags(\"Valves_DB_SelConfigured\").Read());\n" +
                // Healthy added 2026-08-16 from live test C3. The PLC's command guard is
                // NOT LocalMode AND Healthy, but this styling only checked the first two - so on an
                // unhealthy valve the buttons stayed fully lit while the PLC silently discarded
                // every press. The lock must mirror what the PLC will actually accept, or the
                // operator gets no explanation for a button that does nothing.
                "let healthy = readTag(Tags(\"Valves_DB_SelHealthy\").Read());\n" +
                "let locked = local || !cfg || !healthy;\n";
            try {
                var bDyn = btn.Dynamizations.Create<ScriptDynamization>("BackColor");
                bDyn.ScriptCode = guard + "return locked ? 0xFF2A2E38 : " + string.Format("0x{0:X8}", activeBackColor) + ";";
                bDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}
            try {
                var fDyn = btn.Dynamizations.Create<ScriptDynamization>("ForeColor");
                fDyn.ScriptCode = guard + "return locked ? 0xFF6B7280 : 0xFFFFFFFF;";
                fDyn.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}
        }

        // One value cell of the popup's identity card. Has to be an IOField driven by a
        // ScriptDynamization rather than a plain tag bind, because the tag name isn't known at
        // build time - it's derived per-read from whichever valve SelectedValve currently points at.
        // Renders an em dash for an empty field so an un-entered value reads as deliberately blank
        // instead of looking like the popup failed to load.
        static void PopMetaField(HmiScreen sc, string name, int x, int y, int w, int h, string tagSuffix)
        {
            var f = sc.ScreenItems.Create<HmiIOField>(name);
            f.Left = SX(x); f.Top = SY(y); f.Width = (uint)SX(w); f.Height = (uint)SY(h);
            f.BackColor = BG_CARD; f.ForeColor = Color.White;
            f.BorderColor = BG_CARD; f.BorderWidth = 0;
            SetPropEnum(f, "IOFieldType", "Output");
            SetPropEnum(f, "TextHorizontalAlignment", "Left");
            SetFont(f, SFont(13), false);
            SetMLText(f, "Text", "—");
            try {
                var d = f.Dynamizations.Create<ScriptDynamization>("ProcessValue");
                d.ScriptCode =
                    "function readTag(v) { return (v !== null && typeof v === \"object\" && \"Value\" in v) ? v.Value : v; }\n" +
                    "let s = readTag(Tags(\"Valve_Meta_DB_SelVTag\").Read());\n" +
                    "return (s === null || s === undefined || s === \"\") ? \"—\" : s;";
                // AutomaticTags: reads the fixed Valve_Meta_DB_SelVTag mirror tag, so the dependency
                // is statically known - no polling needed.
                d.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), "AutomaticTags");
            } catch {}
        }

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
                EnsurePlcCpuType(plcDevice, "OrderNumber:6ES7 214-1AG40-0XB0/V4.0");

                // Import Valves_DB
                try {
                    string dbPath = @"C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\temp_valves_db.xml";
                    Console.WriteLine("  [PLC] Importing Valves_DB from " + dbPath + "...");
                    var dbBlock = plc.BlockGroup.Blocks.Import(new FileInfo(dbPath), ImportOptions.Override);
                    if (dbBlock != null && dbBlock.Count > 0) 
                        Console.WriteLine("  [PLC] Import successful: " + dbBlock[0].Name);
                } catch (Exception ex) {
                    // Print the real reason. This used to guess "PLC is online or block exists",
                    // which is a plausible-sounding line that hides the actual exception - and on
                    // 2026-08-29 it hid exactly that, while an import silently did nothing and the
                    // run looked successful. A swallowed error that invents its own explanation is
                    // worse than no message.
                    Console.WriteLine("  [PLC] IMPORT FAILED: Valves_DB - " + Root(ex));
                }
                
                // Import FB_ValveLoop
                try {
                    string loopPath = @"C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\temp_fb_valveloop.xml";
                    Console.WriteLine("  [PLC] Importing FB_ValveLoop from " + loopPath + "...");
                    var loopBlock = plc.BlockGroup.Blocks.Import(new FileInfo(loopPath), ImportOptions.Override);
                    if (loopBlock != null && loopBlock.Count > 0)
                        Console.WriteLine("  [PLC] Import successful: " + loopBlock[0].Name);
                } catch (Exception ex) {
                    // Print the real reason. This used to guess "PLC is online or block exists",
                    // which is a plausible-sounding line that hides the actual exception - and on
                    // 2026-08-29 it hid exactly that, while an import silently did nothing and the
                    // run looked successful. A swallowed error that invents its own explanation is
                    // worse than no message.
                    Console.WriteLine("  [PLC] IMPORT FAILED: FB_ValveLoop - " + Root(ex));
                }

                // Import Valve_Meta_DB
                try {
                    string metaPath = @"C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\temp_valve_meta_db.xml";
                    Console.WriteLine("  [PLC] Importing Valve_Meta_DB from " + metaPath + "...");
                    var metaBlock = plc.BlockGroup.Blocks.Import(new FileInfo(metaPath), ImportOptions.Override);
                    if (metaBlock != null && metaBlock.Count > 0)
                        Console.WriteLine("  [PLC] Import successful: " + metaBlock[0].Name);
                } catch (Exception ex) {
                    // Print the real reason. This used to guess "PLC is online or block exists",
                    // which is a plausible-sounding line that hides the actual exception - and on
                    // 2026-08-29 it hid exactly that, while an import silently did nothing and the
                    // run looked successful. A swallowed error that invents its own explanation is
                    // worse than no message.
                    Console.WriteLine("  [PLC] IMPORT FAILED: Valve_Meta_DB - " + Root(ex));
                }

                // Dynamic I/O Mapper scaffolding (UDT_Valve_Config, Valve_Channels_DB, IO_Buffer_DB,
                // FC_IoMapper) - not yet wired into Main[OB1], see FC_IoMapper's own header comment
                // for what's still needed before it does anything. Safe to keep re-importing: all
                // channel indices default to 0 (unassigned), which every consumer treats as
                // "still on symbolic simulation".
                try {
                    string udtPath = @"C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\temp_udt_valve_config.xml";
                    Console.WriteLine("  [PLC] Importing UDT_Valve_Config from " + udtPath + "...");
                    var udtResult = plc.TypeGroup.Types.Import(new FileInfo(udtPath), ImportOptions.Override);
                    if (udtResult != null && udtResult.Count > 0)
                        Console.WriteLine("  [PLC] Import successful: " + udtResult[0].Name);
                } catch (Exception ex) {
                    Console.WriteLine("  [PLC] (Skipping UDT_Valve_Config re-import - PLC is online or type exists)");
                }
                try {
                    string chPath = @"C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\temp_valve_channels_db.xml";
                    Console.WriteLine("  [PLC] Importing Valve_Channels_DB from " + chPath + "...");
                    var chBlock = plc.BlockGroup.Blocks.Import(new FileInfo(chPath), ImportOptions.Override);
                    if (chBlock != null && chBlock.Count > 0)
                        Console.WriteLine("  [PLC] Import successful: " + chBlock[0].Name);
                } catch (Exception ex) {
                    // Print the real reason. This used to guess "PLC is online or block exists",
                    // which is a plausible-sounding line that hides the actual exception - and on
                    // 2026-08-29 it hid exactly that, while an import silently did nothing and the
                    // run looked successful. A swallowed error that invents its own explanation is
                    // worse than no message.
                    Console.WriteLine("  [PLC] IMPORT FAILED: Valve_Channels_DB - " + Root(ex));
                }
                try {
                    string ioPath = @"C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\temp_io_buffer_db.xml";
                    Console.WriteLine("  [PLC] Importing IO_Buffer_DB from " + ioPath + "...");
                    var ioBlock = plc.BlockGroup.Blocks.Import(new FileInfo(ioPath), ImportOptions.Override);
                    if (ioBlock != null && ioBlock.Count > 0)
                        Console.WriteLine("  [PLC] Import successful: " + ioBlock[0].Name);
                } catch (Exception ex) {
                    // Print the real reason. This used to guess "PLC is online or block exists",
                    // which is a plausible-sounding line that hides the actual exception - and on
                    // 2026-08-29 it hid exactly that, while an import silently did nothing and the
                    // run looked successful. A swallowed error that invents its own explanation is
                    // worse than no message.
                    Console.WriteLine("  [PLC] IMPORT FAILED: IO_Buffer_DB - " + Root(ex));
                }
                try {
                    string fcPath = @"C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\temp_fc_iomapper.xml";
                    Console.WriteLine("  [PLC] Importing FC_IoMapper from " + fcPath + "...");
                    var fcBlock = plc.BlockGroup.Blocks.Import(new FileInfo(fcPath), ImportOptions.Override);
                    if (fcBlock != null && fcBlock.Count > 0)
                        Console.WriteLine("  [PLC] Import successful: " + fcBlock[0].Name);
                } catch (Exception ex) {
                    // Print the real reason. This used to guess "PLC is online or block exists",
                    // which is a plausible-sounding line that hides the actual exception - and on
                    // 2026-08-29 it hid exactly that, while an import silently did nothing and the
                    // run looked successful. A swallowed error that invents its own explanation is
                    // worse than no message.
                    Console.WriteLine("  [PLC] IMPORT FAILED: FC_IoMapper - " + Root(ex));
                }
                // Reads real %I/%Q bits (AFT/MID/FWD ET200SP stations, addresses confirmed live in
                // TIA 2026-08-08 after assigning all 3 to PLC_1's PROFINET IO system) into/out of
                // IO_Buffer_DB. Also not yet called from Main[OB1] - see its own header comment.
                try {
                    string physIoPath = @"C:\Users\abbas\OneDrive\Documents\Automation\valveDemo2\temp_fc_physical_io_copy.xml";
                    Console.WriteLine("  [PLC] Importing FC_PhysicalIoCopy from " + physIoPath + "...");
                    var physIoBlock = plc.BlockGroup.Blocks.Import(new FileInfo(physIoPath), ImportOptions.Override);
                    if (physIoBlock != null && physIoBlock.Count > 0)
                        Console.WriteLine("  [PLC] Import successful: " + physIoBlock[0].Name);
                } catch (Exception ex) {
                    // Print the real reason. This used to guess "PLC is online or block exists",
                    // which is a plausible-sounding line that hides the actual exception - and on
                    // 2026-08-29 it hid exactly that, while an import silently did nothing and the
                    // run looked successful. A swallowed error that invents its own explanation is
                    // worse than no message.
                    Console.WriteLine("  [PLC] IMPORT FAILED: FC_PhysicalIoCopy - " + Root(ex));
                }
            } catch (Exception ex) {
                Console.WriteLine("  [PLC] Skipping PLC block import: " + ex.Message);
            }
        }

        static System.Collections.Generic.Dictionary<string, byte> _classPriorities;

        // Slot -> client CM number, built from HOME_DIAGRAM (MarineScreens.cs) since that is the one
        // table covering all 89 slots. Both files are `partial class Program`, so it is visible here.
        // Single source of truth on purpose: a second hand-maintained list would drift the moment
        // the client confirms their numbering.
        static System.Collections.Generic.Dictionary<int, string> _slotCm;
        static string CmForSlot(int slot)
        {
            if (_slotCm == null) {
                _slotCm = new System.Collections.Generic.Dictionary<int, string>();
                foreach (var v in HOME_DIAGRAM) if (!_slotCm.ContainsKey(v.Slot)) _slotCm[v.Slot] = v.Cm;
            }
            string cm;
            // Falling back to the slot tag is deliberate: an unmapped slot must still get an alarm
            // with a usable identifier rather than an empty one.
            return _slotCm.TryGetValue(slot, out cm) ? cm : string.Format("V{0:D3}", slot);
        }

        // ── Alarm colour coding (ISA-18.2 / EEMUA 191) ──────────────────────────
        // Priority is carried by HUE, "needs attention" by FLASHING. Four states per class:
        //   RaisedState              saturated hue + flashing - live and unacknowledged
        //   AcknowledgedState        same hue, solid - still a live condition, operator has seen it
        //   ClearedState             pale hue on white - condition gone but still needs acknowledging
        //   AcknowledgedClearedState neutral grey - resolved, about to leave the list
        // Amber takes DARK text; red/orange/blue take white. Straight white-on-amber fails contrast.
        // Events never flash - a valve going to Local is information, not something to chase.
        static void ApplyAlarmClassColors(HmiSoftware hmi)
        {
            Color neutralBg = Color.FromArgb(255, 240, 242, 245);
            Color neutralTx = Color.FromArgb(255,  96, 106, 122);
            Color white     = Color.FromArgb(255, 255, 255, 255);
            Color dark      = Color.FromArgb(255,  22,  28,  38);

            // name, raisedBg, raisedText, clearedBg, clearedText, flashWhenRaised
            object[][] scheme = new object[][] {
                new object[] { "ValveFault",   Color.FromArgb(255, 205,  32,  38), white,
                                               Color.FromArgb(255, 255, 228, 228), Color.FromArgb(255, 168,  26,  31), true  },
                new object[] { "System",       Color.FromArgb(255, 214,  93,  20), white,
                                               Color.FromArgb(255, 255, 238, 224), Color.FromArgb(255, 168,  73,  16), true  },
                new object[] { "ValveWarning", Color.FromArgb(255, 226, 168,   0), dark,
                                               Color.FromArgb(255, 255, 248, 220), Color.FromArgb(255, 140, 104,   0), true  },
                new object[] { "ValveEvent",   Color.FromArgb(255,   0, 120, 200), white,
                                               Color.FromArgb(255, 226, 240, 252), Color.FromArgb(255,   0,  92, 154), false },
            };

            int done = 0, failed = 0;
            foreach (var row in scheme) {
                string name = (string)row[0];
                var cls = hmi.AlarmClasses.Find(name);
                if (cls == null) { Console.WriteLine("  [AlarmColor] class not found: " + name); failed++; continue; }
                bool ok = true;
                ok &= PaintAlarmState(name, "RaisedState",              cls, (Color)row[1], (Color)row[2], (bool)row[5]);
                ok &= PaintAlarmState(name, "AcknowledgedState",        cls, (Color)row[1], (Color)row[2], false);
                ok &= PaintAlarmState(name, "ClearedState",             cls, (Color)row[3], (Color)row[4], false);
                ok &= PaintAlarmState(name, "AcknowledgedClearedState", cls, neutralBg,     neutralTx,     false);
                if (ok) done++; else failed++;
            }
            Console.WriteLine("  [AlarmColor] " + done + "/" + scheme.Length + " classes coloured" +
                              (failed > 0 ? "  (" + failed + " with problems)" : ""));
        }

        // Reflection rather than direct typed access: the state objects live in a deep
        // HmiAlarmCommon namespace and a rename between V20 updates would otherwise break the
        // build outright. Failures are REPORTED, not swallowed - a silent catch here is exactly
        // what let the "PLC tag is invalid" bug hide for weeks.
        static bool PaintAlarmState(string clsName, string stateName, object cls,
                                     Color back, Color text, bool flashing)
        {
            object st = null;
            try {
                var sp = cls.GetType().GetProperty(stateName);
                if (sp != null) st = sp.GetValue(cls, null);
            } catch (Exception ex) {
                Console.WriteLine("  [AlarmColor] " + clsName + "." + stateName + ": " + Root(ex)); return false;
            }
            if (st == null) { Console.WriteLine("  [AlarmColor] " + clsName + "." + stateName + ": missing"); return false; }
            bool ok = true;
            ok &= PaintOne(clsName, stateName, st, "BackColor", back);
            ok &= PaintOne(clsName, stateName, st, "TextColor", text);
            ok &= PaintOne(clsName, stateName, st, "Flashing",  flashing);
            return ok;
        }

        static bool PaintOne(string clsName, string stateName, object st, string prop, object val)
        {
            try {
                var pi = st.GetType().GetProperty(prop);
                if (pi == null || !pi.CanWrite) {
                    Console.WriteLine("  [AlarmColor] " + clsName + "." + stateName + "." + prop + ": not writable");
                    return false;
                }
                pi.SetValue(st, val, null);
                return true;
            } catch (Exception ex) {
                Console.WriteLine("  [AlarmColor] " + clsName + "." + stateName + "." + prop + ": " + Root(ex));
                return false;
            }
        }

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
            ApplyAlarmClassColors(hmi);
            // Setting the class's Priority does NOT flow through to alarms already assigned to it -
            // confirmed live: after setting ValveFault.Priority = 14, V001_Unhealthy.Priority (its
            // own, separate property) still read 0. Each alarm instance carries its own Priority
            // that CreateDiscreteAlarm must set explicitly, matching its class's value.
            _classPriorities = classPriorities;

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

            for (int i = 1; i <= VALVE_COUNT; i++) {
                string vId = string.Format("V{0:D3}", i);
                // Alarm NAME stays V0xx — a stable internal identifier that does not churn if the
                // client renumbers. Everything the operator reads (Origin column and message text)
                // uses the CM number, because that is what the crew and the schedule call the valve.
                // Reported from the panel 2026-08-16: the alarm list said V001 for CM25.
                string cm = CmForSlot(i);
                // Position-based zone boundaries: AFT 1-27, BILGE/ER 28-54, FWD 55-89.
                string zoneArea = (i <= 27) ? "BALLAST AFT" : (i <= 54) ? "BILGE-ER" : "BALLAST FWD";

                // Pass 1: High priority alarms
                CreateDiscreteAlarm(hmi, vId + "_Unhealthy", "ValveFault", cm + " reported Unhealthy status.", dbName + "_W_Unhealthy_" + ((i-1)/16), (i-1)%16, cm, zoneArea);
                // W_Conflict is packed from OpenFB AND ClosedFB (FB_ValveLoop ~L545), i.e. DOUBLE
                // INDICATION - not a command conflict. The old text said "Open and Close requested",
                // which sent a technician looking at the command path when the actual fault is a
                // limit switch or its wiring. Proven live 2026-08-16: test D1 (both limits made)
                // raised it; test C5 (a genuine OpenCmd+CloseCmd in one scan) raised nothing.
                // The real command conflict is handled at ~L210 but never packed, and deliberately
                // gets no alarm: an operator cannot press two buttons in the same scan, the
                // interlock clears both, and nothing moves. It is only reachable from a watch table.
                // Renamed from _Conflict - the old alarms must be deleted during the regeneration.
                CreateDiscreteAlarm(hmi, vId + "_DoubleInd", "ValveFault", cm + " Double indication - both limit switches made.", dbName + "_W_Conflict_" + ((i-1)/16), (i-1)%16, cm, zoneArea);
                // "on remote command", not "in automatic mode" - this system has REMOTE and LOCAL
                // only, no automatic sequencing. The old wording caused real confusion during the
                // 2026-08-15 local-mode test.
                CreateDiscreteAlarm(hmi, vId + "_FailOpen", "ValveWarning", cm + " Failed to Open on remote command.", dbName + "_W_FailOpen_" + ((i-1)/16), (i-1)%16, cm, zoneArea);
                CreateDiscreteAlarm(hmi, vId + "_FailClose", "ValveWarning", cm + " Failed to Close on remote command.", dbName + "_W_FailClose_" + ((i-1)/16), (i-1)%16, cm, zoneArea);

                // Pass 2: E (Loss of Position, MED), F (Unexpected Movement, MED), G (Local Mode, LOW).
                // Reuse ValveWarning for E/F (same tier as C/D's timeouts) and the new ValveEvent
                // class for G, since Local Mode is a logged event rather than a real fault.
                CreateDiscreteAlarm(hmi, vId + "_LossPos", "ValveWarning", cm + " Loss of Position Feedback (idle, no limit switch made).", dbName + "_W_LossPos_" + ((i-1)/16), (i-1)%16, cm, zoneArea);
                CreateDiscreteAlarm(hmi, vId + "_UnexpMove", "ValveWarning", cm + " Unexpected Movement detected (uncommanded limit switch change).", dbName + "_W_UnexpMove_" + ((i-1)/16), (i-1)%16, cm, zoneArea);
                CreateDiscreteAlarm(hmi, vId + "_Local", "ValveEvent", cm + " switched to Local Control.", dbName + "_W_Local_" + ((i-1)/16), (i-1)%16, cm, zoneArea);
                // Direction / limit discrepancy (H). W_DirFault has been packed by FB_ValveLoop
                // since 2026-08-16 but had NO alarm, so the fault latched, coloured the mimic and
                // the popup, and produced nothing in the alarm list — no timestamp, no ack, no
                // history. That gap gets worse once the box FLASHES, because flashing tells the
                // operator to go read an alarm list that would have had nothing in it.
                // Text names BOTH candidate causes on purpose: the PLC cannot tell a wrong-direction
                // actuator from a stuck limit switch, but the technician with a multimeter can.
                CreateDiscreteAlarm(hmi, vId + "_DirFault", "ValveWarning", cm + " Direction / limit fault - check actuator and limit switch wiring.", dbName + "_W_DirFault_" + ((i-1)/16), (i-1)%16, cm, zoneArea);
            }
            Console.WriteLine("  Created " + (VALVE_COUNT * 8 + 9) + " discrete alarms.");
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
            Console.WriteLine("\n[STEP 2] Checking and creating HMI tags for all 89 slots...");
            // SelectedValve is an INTERNAL HMI tag - no PLC address, just holds the selected index
            CreateInternalTag(hmi, "SelectedValve", "Int");
            // BilgePage tracks which page (0 or 1) of the Bilge valve table is currently shown —
            // internal only, no PLC binding, same pattern as SelectedValve.
            CreateInternalTag(hmi, "BilgePage", "Int");
            CreateInternalTag(hmi, "Internal_PrevFaultCount", "Int");
            // Written once a second by the home panel from GetActiveAlarms, so the home count and
            // the alarm list cannot drift apart.
            CreateInternalTag(hmi, "ActiveAlarmCount", "Int");
            // Station health, written by OB86 into Diag_DB. Nothing read these until the home
            // panel did - AnyStationLost was built as "one bit for the HMI to watch" and then
            // watched by nothing.
            CreateSummaryTag(hmi, "Diag_AftLost",        "Diag_DB.AftLost",        "Bool");
            CreateSummaryTag(hmi, "Diag_MidLost",        "Diag_DB.MidLost",        "Bool");
            CreateSummaryTag(hmi, "Diag_FwdLost",        "Diag_DB.FwdLost",        "Bool");
            CreateSummaryTag(hmi, "Diag_AnyStationLost", "Diag_DB.AnyStationLost", "Bool");
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

            // Selected-valve mirror for the control popup. The HMI writes SelIdx on valve tap;
            // FB_ValveLoop copies that valve's live fields into these fixed tags every scan. Fixed
            // names are the whole point - they let the popup's display scripts run on AutomaticTags
            // instead of a T500ms poll, which is what made the popup slow to populate. New tags, so
            // they need forceRefreshNewTags to get their PLC address bound (see the note above).
            CreateSummaryTag(hmi, "Valves_DB_SelIdx",        "Valves_DB.SelIdx",        "Int",  forceRefreshNewTags);
            CreateSummaryTag(hmi, "Valves_DB_SelState",      "Valves_DB.SelState",      "Int",  forceRefreshNewTags);
            CreateSummaryTag(hmi, "Valves_DB_SelHealthy",    "Valves_DB.SelHealthy",    "Bool", forceRefreshNewTags);
            CreateSummaryTag(hmi, "Valves_DB_SelLocalMode",  "Valves_DB.SelLocalMode",  "Bool", forceRefreshNewTags);
            CreateSummaryTag(hmi, "Valves_DB_SelConfigured", "Valves_DB.SelConfigured", "Bool", forceRefreshNewTags);
            CreateSummaryTag(hmi, "Valves_DB_SelCmdPos",    "Valves_DB.SelCmdPos",     "Int",  forceRefreshNewTags);
            CreateSummaryTag(hmi, "Valves_DB_SelPosCode",   "Valves_DB.SelPosCode",    "Int",  forceRefreshNewTags);
            CreateSummaryTag(hmi, "Valves_DB_SelFaultCode", "Valves_DB.SelFaultCode",  "Int",  forceRefreshNewTags);
            CreateSummaryTag(hmi, "Valve_Meta_DB_SelCmNo",   "Valve_Meta_DB.SelCmNo",   "String", forceRefreshNewTags);
            CreateSummaryTag(hmi, "Valve_Meta_DB_SelVTag",   "Valve_Meta_DB.SelVTag",   "String", forceRefreshNewTags);

            // Per-zone sub-totals — FB_ValveLoop computes these in the same 1..88 pass that
            // already builds the plant-wide totals above, so each KPI/caption cell on
            // Screen_Home can read one tag instead of looping its zone's valves itself.
            string[] zonePfx = { "Er", "Fwd", "Aft" };
            string[] statSuf = { "Open", "Closed", "Transit", "Fault", "Local", "Configured" };
            foreach (var zp in zonePfx)
                foreach (var st in statSuf)
                    CreateSummaryTag(hmi, "Valves_DB_" + zp + st, "Valves_DB." + zp + st, "Int", forceRefreshNewTags);

            // Add the word arrays for discrete alarms
            string[] conditions = { "Unhealthy", "Conflict", "FailOpen", "FailClose", "LossPos", "UnexpMove", "Local", "DirFault" };
            for (int w = 0; w < 6; w++) {
                foreach (var cond in conditions) {
                    CreateSummaryTag(hmi, "Valves_DB_W_" + cond + "_" + w, "Valves_DB.W_" + cond + "[" + w + "]", "UInt");
                }
            }

            Console.WriteLine("  Creating HMI tags (Configured, OpenCmd, CloseCmd, OpenFB, ClosedFB, Healthy, LocalMode) for 89 slots...");
            for (int i = 1; i <= VALVE_COUNT; i++) {
                string vTag = string.Format("V{0:D3}", i);
                string plcPrefix = string.Format("Valves_DB.Valve[{0}]", i);
                // These seven must pass forceRefreshNewTags like every other call below. Omitting
                // it defaulted forceRefresh to false, which made --fix-tags skip them entirely:
                // the tags were created but their PLC address was never applied, so they stayed
                // permanently unbound and failed the HMI compile with "The property PLC tag is
                // invalid". Latent since the refresh mechanism was added - it only surfaced when
                // slots 89-96 became the first new valves created after that point.
                CreateSummaryTag(hmi, vTag + "_Configured", plcPrefix + ".Configured", "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_OpenCmd",    plcPrefix + ".OpenCmd",    "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_CloseCmd",   plcPrefix + ".CloseCmd",   "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_OpenFB",     plcPrefix + ".OpenFB",     "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_ClosedFB",   plcPrefix + ".ClosedFB",   "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_Healthy",    plcPrefix + ".Healthy",    "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_LocalMode",  plcPrefix + ".LocalMode",  "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_Stuck",             "Valves_DB.Stuck[" + i + "]",             "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_TimeoutOpenAlarm",  "Valves_DB.TimeoutOpenAlarm[" + i + "]",  "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_TimeoutCloseAlarm", "Valves_DB.TimeoutCloseAlarm[" + i + "]", "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_UnexpMove",         "Valves_DB.UnexpMove[" + i + "]",         "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_DirFault",          "Valves_DB.DirFault[" + i + "]",          "Bool", forceRefreshNewTags);
                // _State is read by every mimic badge, every diagram overlay and the popup, but was
                // never CREATED here - the whole set was inherited from an earlier script and just
                // happened to resolve. It stopped happening the moment a screen referenced slot 89:
                // V089_State had never been made (the pool was 96 slots, then cut to 89, and the
                // orphan cleanup removed V090-V096), so Home's CM90 overlay failed to compile.
                // Created explicitly now so the set is owned by this generator and complete.
                CreateSummaryTag(hmi, vTag + "_State",             "Valves_DB.StateCode[" + i + "]",         "Int",  forceRefreshNewTags);
                // Position-only code for the mimic fill, so a faulted valve still shows where it is.
                CreateSummaryTag(hmi, vTag + "_PosCode",           "Valves_DB.PosCode[" + i + "]",           "Int",  forceRefreshNewTags);
                // Drives the mimic box colour INCLUDING the fault flash — the PLC does the
                // alternating, so the box binds one native value map and runs no script.
                CreateSummaryTag(hmi, vTag + "_DispCode",          "Valves_DB.DispCode[" + i + "]",          "Int",  forceRefreshNewTags);
                // Manually-maintained reference data (Valve_Meta_DB) — not written by any
                // script; an engineer fills these in by hand. Built for all 88 now so every
                // future zone screen (not just Bilge/ER) can reuse them without another pass.
                CreateSummaryTag(hmi, vTag + "_Name",     "Valve_Meta_DB.Name[" + i + "]",     "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_Location", "Valve_Meta_DB.Location[" + i + "]", "String", forceRefreshNewTags);
                // Client-schedule identity, per valve. The Configuration table reads its paged
                // Cfg_Tbl* window instead, but the popup resolves its tag names at runtime from
                // SelectedValve, so it needs a directly-addressable tag per valve.
                CreateSummaryTag(hmi, vTag + "_CmNo", "Valve_Meta_DB.CmNo[" + i + "]",     "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_VTag", "Valve_Meta_DB.VTag[" + i + "]",     "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_Sys",  "Valve_Meta_DB.SysName[" + i + "]",  "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, vTag + "_Func", "Valve_Meta_DB.FuncName[" + i + "]", "String", forceRefreshNewTags);
            }

            // Configuration screen's global (all-88) paged window - same naming convention as the
            // Aft/Er/Fwd_Tbl* tags the zone tables already use (Aft_TblNo_1..14 etc., confirmed live
            // in the project's ValveTags table - 1563 tags total).
            CreateSummaryTag(hmi, "Valves_DB_CfgPage", "Valves_DB.CfgPage", "Int", forceRefreshNewTags);
            for (int slot = 1; slot <= 16; slot++) {
                CreateSummaryTag(hmi, "Cfg_TblNo_" + slot,   "Valves_DB.CfgTblNo[" + slot + "]",   "Int",    forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblTag_" + slot,  "Valves_DB.CfgTblTag[" + slot + "]",  "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblZone_" + slot, "Valves_DB.CfgTblZone[" + slot + "]", "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblConfigured_" + slot, "Valves_DB.CfgTblConfigured[" + slot + "]", "Bool", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblState_" + slot, "Valves_DB.CfgTblState[" + slot + "]", "Int", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblStateTxt_" + slot, "Valves_DB.CfgTblStateTxt[" + slot + "]", "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblName_" + slot, "Valve_Meta_DB.CfgTblName[" + slot + "]", "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblLoc_" + slot,  "Valve_Meta_DB.CfgTblLoc[" + slot + "]",  "String", forceRefreshNewTags);
                // Client-schedule identity columns (CM No / Valve Tag / System / Function) - the
                // format the client actually issues valve lists in. Cfg_TblTag_/Cfg_TblZone_ above
                // are the older index-derived strings; the Configuration table binds these instead.
                CreateSummaryTag(hmi, "Cfg_TblCmNo_" + slot, "Valve_Meta_DB.CfgTblCmNo[" + slot + "]", "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblVTag_" + slot, "Valve_Meta_DB.CfgTblVTag[" + slot + "]", "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblSys_" + slot,  "Valve_Meta_DB.CfgTblSys[" + slot + "]",  "String", forceRefreshNewTags);
                CreateSummaryTag(hmi, "Cfg_TblFunc_" + slot, "Valve_Meta_DB.CfgTblFunc[" + slot + "]", "String", forceRefreshNewTags);
            }

            // Per-zone VALVE LIST identity columns. NOTE: the zone tables' other tags
            // (Aft_TblNo_/_TblTag_/_TblName_/_TblLoc_/_TblState_/_TblStateTxt_) are bound by
            // BuildValveTable but created nowhere in this generator - they exist in the live
            // project from an earlier script. Only the two new ones are created here; the older
            // set is left alone rather than re-registered, since re-pointing live, working tags
            // is a real risk for no benefit while they resolve correctly.
            foreach (var zp in new[] { "Aft", "Er", "Fwd" }) {
                for (int slot = 1; slot <= 14; slot++) {
                    CreateSummaryTag(hmi, zp + "_TblCmNo_" + slot, "Valve_Meta_DB." + zp + "TblCmNo[" + slot + "]", "String", forceRefreshNewTags);
                    CreateSummaryTag(hmi, zp + "_TblVTag_" + slot, "Valve_Meta_DB." + zp + "TblVTag[" + slot + "]", "String", forceRefreshNewTags);
                    // FUNCTION column, added 2026-08-16. Like CmNo/VTag above this is a new tag,
                    // so it needs the --fix-tags pass afterwards to bind its PLC address.
                    CreateSummaryTag(hmi, zp + "_TblFunc_" + slot, "Valve_Meta_DB." + zp + "TblFunc[" + slot + "]", "String", forceRefreshNewTags);
                }
            }

            // Internal (no PLC binding) tags for the Configuration screen's features - jump
            // target, and the confirm-disable popup's coordination value.
            CreateInternalTag(hmi, "CfgJumpTarget", "Int");
            CreateInternalTag(hmi, "ConfirmValveIdx", "Int");
        }

        // Creates an HMI tag that IS connected to a PLC tag
        static void EnsureGmpEnabled(HmiSoftware hmi)
        {
            try {
                if (hmi.RuntimeSettings.GMPEnabled) { Console.WriteLine("  [Audit] GMP already enabled."); return; }
                hmi.RuntimeSettings.GMPEnabled = true;
                Console.WriteLine("  [Audit] GMP enabled on HMI device.");
            } catch (Exception ex) { Console.WriteLine("  [Audit ERR] Could not enable GMP: " + ex.Message); }
        }

        // Arms the Audit Trail. Without this the log is configured but never started, and every
        // operator action goes unrecorded while everything still looks correct in the editor.
        static void WireStartAuditLog(HmiScreen sc)
        {
            try {
                var evProp = sc.GetType().GetProperty("EventHandlers");
                if (evProp == null) { Console.WriteLine("  [Audit ERR] No EventHandlers on Screen_Home."); return; }
                object evObj = evProp.GetValue(sc, null);

                MethodInfo create = null; Type enumType = null;
                foreach (var m in evObj.GetType().GetMethods()) {
                    if (m.Name != "Create") continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType.IsEnum) { create = m; enumType = ps[0].ParameterType; break; }
                }
                if (create == null) { Console.WriteLine("  [Audit ERR] No EventHandlers.Create(enum) overload."); return; }

                object handler = create.Invoke(evObj, new object[] { Enum.Parse(enumType, "Loaded") });
                if (handler == null) { Console.WriteLine("  [Audit ERR] Could not create Loaded handler."); return; }

                var script = handler.GetType().GetProperty("Script").GetValue(handler, null);
                var codeProp = script.GetType().GetProperty("ScriptCode");
                if (codeProp == null || !codeProp.CanWrite) { Console.WriteLine("  [Audit ERR] ScriptCode not writable."); return; }
                codeProp.SetValue(script, "HMIRuntime.Audit.SysFct.StartAuditLog();", null);
                Console.WriteLine("  [Audit] StartAuditLog() wired to Screen_Home Loaded.");
            } catch (Exception ex) { Console.WriteLine("  [Audit ERR] WireStartAuditLog: " + ex.Message); }
        }

        // Tags an operator drives from the HMI. Feedback signals (OpenFB, ClosedFB, Healthy,
        // LocalMode, State...) are written by the PLC, and Siemens' docs are explicit that
        // PLC-driven changes are never logged - flagging them would only add noise.
        static readonly string[] OperatorCommandSuffixes = {
            "_OpenCmd",           // OPEN pressed
            "_CloseCmd",          // CLOSE pressed
            "_Configured",        // valve enabled / disabled
            "_TimeoutOpenAlarm",  // FAULT RESET clears these four
            "_TimeoutCloseAlarm",
            "_UnexpMove",
            "_DirFault",
        };

        static bool IsOperatorCommandTag(string tagName)
        {
            foreach (var suf in OperatorCommandSuffixes)
                if (tagName.EndsWith(suf, StringComparison.Ordinal)) return true;
            return false;
        }

        static void CreateSummaryTag(HmiSoftware hmi, string tagName, string plcAddress, string dataType = "Int", bool forceRefresh = false)
        {
            try {
                var table = hmi.TagTables.Find("ValveTags");
                if (table == null) { table = hmi.TagTables.Create("ValveTags"); Console.WriteLine("  Created tag table: ValveTags"); }

                var tag = table.Tags.Find(tagName);
                bool isNewTag = (tag == null);
                // Existing tags never need their address/connection/type touched again —
                // re-setting all three on all 616 valve tags every run was costing
                // ~1800 redundant Openness round-trips (the dominant cost per call,
                // since there's no bulk-write API) on every single rebuild.
                // forceRefresh bypasses this: tags created while the PLC block existed but
                // wasn't yet compiled get their address bound to nothing and stay broken
                // forever — re-setting the address after the PLC is properly compiled fixes it.
                if (tag != null && !forceRefresh) return;
                if (tag == null) tag = table.Tags.Create(tagName, "ValveTags");

                // Audit Trail. WinCC logs an operator's tag change itself once the tag is flagged
                // GmpRelevant - that is the whole mechanism, no scripting involved. Only done for
                // tags we just created: re-reading the flag on all 624 command tags every run would
                // reintroduce exactly the round-trip cost the early return above exists to avoid,
                // and an existing tag already carries the flag (nothing here ever clears it).
                // SetGmpTags.exe is the one-shot tool for flagging an already-built project.
                if (isNewTag && IsOperatorCommandTag(tagName)) {
                    try {
                        tag.GmpRelevant = true;
                        // No confirmation dialog - an operator closing a valve in a hurry must not
                        // be blocked by a prompt. The record is written either way.
                        tag.ConfirmationType = HmiConfirmationType.None;
                    } catch (Exception exg) { Console.WriteLine("  [GMP ERR] " + tagName + ": " + exg.Message); }
                }

                // Connection and PlcName FIRST. A brand-new tag has no connection assigned, and
                // TIA rejects a PlcTag address on a tag that isn't attached to a PLC connection
                // yet — which is why newly created tags always failed their address on the run
                // that created them and had to be repaired by a second --fix-tags pass. Setting
                // the connection up front removes the two-pass dance entirely.
                SetStr(tag, "Connection", HMI_CONNECTION);
                SetStr(tag, "PlcName", "PLC_1");

                // Try all known property names for the PLC address field
                bool addressSet = false;
                string lastErr = "no writable address property found";
                foreach (var propName in new string[]{ "LogicalAddress", "PlcTag", "Address", "TagAddress" }) {
                    try {
                        var pp = tag.GetType().GetProperty(propName);
                        if (pp != null && pp.CanWrite) { pp.SetValue(tag, plcAddress, null); addressSet = true; break; }
                    } catch (Exception aex) {
                        // Keep the real reason. Swallowing it silently is what made this take a
                        // diagnostic detour: the WARN said "could not set" but never said why.
                        lastErr = propName + ": " + Root(aex);
                    }
                }
                if (!addressSet)
                    Console.WriteLine("  [WARN] Could not set address for " + tagName +
                                      " (" + plcAddress + ") -> " + lastErr);

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
                // Do NOT set Connection or address - internal tag has no PLC binding.
                // WinCC Unified's actual DataType value for text is "WString", not "String" -
                // confirmed live: passing "String" here silently fails (caught below) and leaves
                // the tag at its Create()-time default (Int), which is why EditNameBuffer/
                // EditLocBuffer briefly only accepted numeric input.
                string realDataType = dataType == "String" ? "WString" : dataType;
                try {
                    var dtProp = tag.GetType().GetProperty("DataType");
                    if (dtProp != null && dtProp.CanWrite) { try { dtProp.SetValue(tag, realDataType, null); } catch {} }
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
                    BuildAuditLogScreen(scLogin);
                    Console.WriteLine("  Screen_Login rebuilt as the AUDIT LOG screen.");
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
