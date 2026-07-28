# 88-VALVE MARINE SCADA CONTROL & DIAGNOSTIC SYSTEM
## Functional Design Specification (FDS) & Technical Engineering Manual

**Document Version:** 2.0 (FAT / Shipboard Commissioning Release)  
**Target Platform:** Siemens TIA Portal V20 / WinCC Unified MTP1500 (1920 × 1080 Resolution)  
**PLC Architecture:** Siemens S7-1200 / S7-1500 (Profinet / Remote I/O Distributed Nodes)  
**Applicable Standards:**  
- **DNV-GL / ABS / Lloyd's Register** Marine Automation Rules (Bilge, Ballast, and Cargo Valve Control)  
- **ISA-101 / EEMUA 201** Human-Machine Interface Design Standards  
- **ISA-18.2 / EEMUA 191** Alarm Management and Annunciation Standards  

---

## TABLE OF CONTENTS
1. [Executive Summary & System Architecture](#1-executive-summary--system-architecture)
2. [PLC Control & Data Structure Specification](#2-plc-control--data-structure-specification)
   - [2.1 Valve_IO User-Defined Type (UDT)](#21-valve_io-user-defined-type-udt)
   - [2.2 Global Data Block (`Valves_DB`)](#22-global-data-block-valves_db)
   - [2.3 Control Loop & Simulation Architecture (`FB_ValveLoop` & `FB_ValveLogic`)](#23-control-loop--simulation-architecture-fb_valveloop--fb_valvelogic)
3. [HMI Design Philosophy & Ergonomic Standards](#3-hmi-design-philosophy--ergonomic-standards)
   - [3.1 Dark Mode & High-Contrast Visual Hierarchy](#31-dark-mode--high-contrast-visual-hierarchy)
   - [3.2 Color-Coded State & Diagnostic Specification](#32-color-coded-state--diagnostic-specification)
   - [3.3 Persistent Header & Screen Navigation](#33-persistent-header--screen-navigation)
4. [Detailed HMI Screen Functional Specifications](#4-detailed-hmi-screen-functional-specifications)
   - [4.1 Screen 1: System Overview (`Screen_1`)](#41-screen-1-system-overview-screen_1)
   - [4.2 Screen 2: Alarm & Fault Diagnostics (`Screen_Alarms`)](#42-screen-2-alarm--fault-diagnostics-screen_alarms)
   - [4.3 Screen 3: Select-Before-Operate Faceplate Popup (`Screen_Popup`)](#43-screen-3-select-before-operate-faceplate-popup-screen_popup)
5. [Core Technical Implementations & Architecture Solutions](#5-core-technical-implementations--architecture-solutions)
   - [5.1 WinCC Unified 7-Argument Popup Signature](#51-wincc-unified-7-argument-popup-signature)
   - [5.2 Compiler Optimization (`AutomaticTags` & System Clock Heartbeat)](#52-compiler-optimization-automatictags--system-clock-heartbeat)
   - [5.3 Automated TIA Portal Openness Engineering Pipeline (`GenerateHmiLayout.cs`)](#53-automated-tia-portal-openness-engineering-pipeline-generatehmilayoutcs)
6. [Factory Acceptance Test (FAT) & Shipboard Commissioning Guide](#6-factory-acceptance-test-fat--shipboard-commissioning-guide)
   - [6.1 Why the Project is 100% FAT Ready](#61-why-the-project-is-100-fat-ready)
   - [6.2 Transitioning from Simulation to Physical Marine Actuators](#62-transitioning-from-simulation-to-physical-marine-actuators)
   - [6.3 Mandatory Marine Interlocks (Travel Timeout, Disagree & Communication Loss)](#63-mandatory-marine-interlocks-travel-timeout-disagree--communication-loss)
   - [6.4 Vessel Blackout Recovery & Non-Retentive Command Safety](#64-vessel-blackout-recovery--non-retentive-command-safety)

---

## 1. EXECUTIVE SUMMARY & SYSTEM ARCHITECTURE

The **88-Valve Marine SCADA Control & Diagnostic System** is an enterprise-grade automated supervisory and control solution designed for marine vessels (bilge, ballast, fuel oil, and cargo handling systems). The system operates across 88 remote-operated valves, providing operators with instantaneous status indication, Select-Before-Operate (SBO) command execution, and ISA-18.2 compliant fault diagnostics.

```
       +------------------------------------------------------------------+
       |                  HMI LAYER (WinCC Unified MTP1500)               |
       |  Screen_1 (Overview) | Screen_Alarms (Annunciator) | Screen_Popup|
       +---------------------------------+--------------------------------+
                                         |
                       Profinet / Tag Multiplexing Interface
                                         |
       +---------------------------------v--------------------------------+
       |                  PLC LAYER (Siemens S7-1200/1500)                |
       |  Valves_DB (Array[1..88] of Valve_IO) | FB_ValveLoop (88 Loops)  |
       +---------------------------------+--------------------------------+
                                         |
                        2 x DO (OPEN/CLOSE) | 4 x DI (FB/MODE/HEALTH)
                                         |
       +---------------------------------v--------------------------------+
       |                  FIELD HARDWARE / MARINE ACTUATORS               |
       |     Phoenix Interposing Relays  ->  Pleiger EHS Actuators       |
       +------------------------------------------------------------------+
```

### Key System Highlights:
- **Scale:** Modular control and monitoring for **88 valves** (`V-001` through `V-088`).
- **Safety Philosophy:** Strict Select-Before-Operate (SBO) workflow via a sleek, centered popup faceplate (`460 × 360` pixels), preventing accidental actuation from touch-screen jitter or inadvertent clicks.
- **Diagnostics:** Dedicated 88-channel Alarm Annunciator Board with live KPI summary banners, active fault tracking, and a Master Fault Reset interlock.
- **Ergonomics:** Designed in accordance with **ISA-101 dark-mode principles** to eliminate operator eye fatigue during night watches on the bridge or in the Engine Control Room (ECR).

---

## 2. PLC CONTROL & DATA STRUCTURE SPECIFICATION

The PLC software architecture is built on structured programming principles, utilizing a single User-Defined Type (UDT) replicated across a global array and processed by modular function blocks.

### 2.1 `Valve_IO` User-Defined Type (UDT)
Each valve object in the system is represented by the `Valve_IO` data structure, encapsulating all commands, feedback signals, interlocks, and travel timing:

| Member Name | Data Type | I/O Type | Description / Engineering Function |
| :--- | :--- | :--- | :--- |
| **`Configured`** | `BOOL` | Parameter | `TRUE` when the valve channel is commissioned and communicating. When `FALSE`, the channel is disabled and displays as grey (`UNCONFIGURED`). |
| **`OpenCmd`** | `BOOL` | HMI -> PLC | SBO latched command from HMI to initiate valve opening sequence. |
| **`CloseCmd`** | `BOOL` | HMI -> PLC | SBO latched command from HMI to initiate valve closing sequence. |
| **`LocalMode`** | `BOOL` | DI (Field) | `TRUE` when the field control station is in LOCAL mode. Remote HMI commands are interlocked and disabled. |
| **`Healthy`** | `BOOL` | DI / Internal | `TRUE` when actuator power, communication, and limit switches are nominal. |
| **`OpenFB`** | `BOOL` | DI (Field) | Physical OPEN limit switch feedback from the actuator. |
| **`ClosedFB`** | `BOOL` | DI (Field) | Physical CLOSED limit switch feedback from the actuator. |
| **`OpenValve`** | `BOOL` | DO (Field) | Energizes the OPEN solenoid / interposing relay (`DO_OPEN`). |
| **`CloseValve`** | `BOOL` | DO (Field) | Energizes the CLOSE solenoid / interposing relay (`DO_CLOSE`). |
| **`TimerOpen`** | `TON_TIME` | Internal | On-delay timer tracking OPEN travel duration (FAT simulation: 5.0s). |
| **`TimerClose`** | `TON_TIME` | Internal | On-delay timer tracking CLOSE travel duration (FAT simulation: 5.0s). |

### 2.2 Global Data Block (`Valves_DB`)
The PLC maintains all 88 valves within a single optimized Global Data Block (`Valves_DB`):
- **`V001` through `V088`**: Individual instances of `Valve_IO`.
- **`Valves_DB_Clock1Hz` (`BOOL`)**: A 1.0 Hz square-wave system heartbeat bit toggled by the PLC system clock, providing a static reference for HMI script dynamizations.

### 2.3 Control Loop & Simulation Architecture (`FB_ValveLoop` & `FB_ValveLogic`)
The PLC control cycle executes `FB_ValveLoop`, which iterates over the valve array and processes `FB_ValveLogic` for each channel:
1. **Permissive Interlocks:** Commands (`OpenCmd` / `CloseCmd`) are evaluated against interlocks. A command is rejected if `Configured = FALSE`, `Healthy = FALSE`, or `LocalMode = TRUE`.
2. **Output Latching:** When `OpenCmd` is received, `OpenValve (DO)` is latched high and `CloseValve (DO)` is dropped.
3. **5-Second Travel Simulation (FAT Mode):**
   - When `OpenValve` is energized, `TimerOpen (TON)` begins timing (`PT := T#5s`). During this 5-second interval, both `OpenFB` and `ClosedFB` are `FALSE`, placing the valve in the derived **`IN TRANSIT`** state.
   - Upon timer completion (`Q = TRUE`), `OpenFB` is asserted and `ClosedFB` is cleared.
4. **Fault Protection:** If both limit switches are asserted simultaneously (`OpenFB AND ClosedFB`), the block immediately flags a Feedback Disagree fault by setting `Healthy := FALSE`.

---

## 3. HMI DESIGN PHILOSOPHY & ERGONOMIC STANDARDS

### 3.1 Dark Mode & High-Contrast Visual Hierarchy
In marine control rooms, high-glare interfaces impair night vision and reduce visual contrast. The HMI is built on a structured dark palette:
- **Background Canvas (`#1C1C1E`)**: Prevents screen glare and provides maximum contrast for active annunciators.
- **Card & Banner Panels (`#2C2C2E`)**: Creates subtle elevation and separation between control elements.
- **Card Borders (`#3A3A3C`)**: Crisp 1-pixel borders define button geometry without visual clutter.

### 3.2 Color-Coded State & Diagnostic Specification
Every valve card, diagnostic tile, and status circle adheres strictly to a standardized 6-state color philosophy:

```
    +--------------------------------------------------------------------+
    |                     6-STATE COLOR SPECIFICATION                    |
    +-------------------+----------------------+-------------------------+
    | STATE NAME        | COLOR NAME / HEX     | LOGIC CONDITION         |
    +-------------------+----------------------+-------------------------+
    | OPEN              | Green (#30D158)      | OpenFB=1, ClosedFB=0    |
    | CLOSED            | Dark Grey (#3A3A3C)  | OpenFB=0, ClosedFB=0    |
    | IN TRANSIT        | Blue (#00A2FF)       | OpenFB=0, ClosedFB=0    |
    | LOCAL MODE        | Amber (#FF9F0A)      | LocalMode=1             |
    | SYSTEM FAULT      | Red (#FF0000)        | Healthy=0 OR Both=1     |
    | UNCONFIGURED      | Disabled (#8E8E93)   | Configured=0            |
    +-------------------+----------------------+-------------------------+
```

### 3.3 Persistent Header & Screen Navigation
A persistent 48-pixel high header bar spans the top of `Screen_1` and `Screen_Alarms`:
- **System Title:** Displays `"VALVE CONTROL & DIAGNOSTICS — 88-VALVE SCADA SYSTEM"`.
- **Navigation Controls:** Top-right navigation buttons (`Overview` and `⚠ Alarms`) allow instant, 1-click screen switching. The currently active screen button is highlighted in vibrant teal (`#00C7BE`) with a white label, while inactive buttons remain dark with muted text.

---

## 4. DETAILED HMI SCREEN FUNCTIONAL SPECIFICATIONS

### 4.1 Screen 1: System Overview (`Screen_1`)
`Screen_1` serves as the primary operator control desk for normal vessel operations.

```
+-------------------------------------------------------------------------------+
|  VALVE CONTROL & DIAGNOSTICS — 88-VALVE SCADA               [Overview] [Alarms] |
+-------------------------------------------------------------------------------+
| [1 OPEN]  [0 CLOSED]  [0 IN TRANSIT]  [0 FAULTS]  [0 LOCAL]  [87 UNCONFIGURED]|
+-------------------------------------------------------------------------------+
|  +------------+  +------------+  +------------+  +------------+  +---------+  |
|  |   V-001    |  |   V-002    |  |   V-003    |  |   V-004    |  |  V-005  |  |
|  |   OPEN     |  |  UNCFGD    |  |  UNCFGD    |  |  UNCFGD    |  | UNCFGD  |  |
|  +------------+  +------------+  +------------+  +------------+  +---------+  |
|  ( 11 Columns  x  8 Rows Grid  =  88 Interactive Valve Control Cards )        |
+-------------------------------------------------------------------------------+
```

1. **KPI Summary Row (`Y = 48..103 px`)**:
   - Contains 6 real-time counters displaying the entire plant state at a glance:
     - `OPEN VALVES` (Green text/dot)
     - `CLOSED VALVES` (Muted grey text/dot)
     - `IN TRANSIT` (Blue text/dot)
     - `SYSTEM FAULTS` (Red text/dot)
     - `LOCAL MODE` (Amber text/dot)
     - `UNCONFIGURED` (Grey text/dot)
2. **88 Interactive Valve Control Cards (`Y = 113..1070 px`)**:
   - Organized in an **11 column × 8 row** layout (`156 × 108` px per card, with 8px horizontal and 10px vertical margins).
   - **Visual Dynamization:**
     - **Border Color:** Changes dynamically to reflect valve state (Green for Open, Blue for In Transit, Red for Fault, Amber for Local).
     - **Status Indicator Dot (`12 × 12` px):** Positioned at the top-left of each card.
     - **State Text:** Centered two-line typography displaying the tag name (`V-001`) and current operational state (`OPEN`, `CLOSED`, `IN TRANSIT`, `FAULT`, `LOCAL`, or `UNCFGD`).
   - **Click Action:** Clicking any card executes the 7-argument WinCC Unified popup command, opening `Screen_Popup` directly in the center of the screen for that specific valve.

### 4.2 Screen 2: Alarm & Fault Diagnostics (`Screen_Alarms`)
`Screen_Alarms` is engineered to ISA-18.2 / EEMUA 191 alarm management standards, serving as the vessel's central annunciator board for rapid fault isolation.

```
+-------------------------------------------------------------------------------+
|  ALARM & FAULT DIAGNOSTICS — 88-VALVE SCADA                 [Overview] [Alarms] |
+-------------------------------------------------------------------------------+
| [TOTAL: 88]   [ACTIVE FAULTS: 0]   [HEALTHY: 88]   [UNCONFIGURED: 87] [RESET] |
+-------------------------------------------------------------------------------+
|  +------------+  +------------+  +------------+  +------------+  +---------+  |
|  |   V-001    |  |   V-002    |  |   V-003    |  |   V-004    |  |  V-005  |  |
|  |  HEALTHY   |  |  UNCFGD    |  |  UNCFGD    |  |  UNCFGD    |  | UNCFGD  |  |
|  +------------+  +------------+  +------------+  +------------+  +---------+  |
|  ( 11 Columns  x  8 Rows Grid  =  88 Diagnostic Annunciator Tiles )           |
+-------------------------------------------------------------------------------+
```

1. **Diagnostic KPI & Control Row (`Y = 48..103 px`)**:
   - **`Al_KPI_Total`**: Displays total plant channels (`TOTAL VALVES: 88`).
   - **`Al_KPI_Faults`**: Highlights active fault count in high-contrast red (`ACTIVE FAULTS: 0`).
   - **`Al_KPI_Healthy`**: Confirms operational channels in teal (`HEALTHY VALVES: 88`).
   - **`Al_KPI_Banner`**: Displays unconfigured count (`UNCONFIGURED: 87`).
   - **Master Fault Reset Button (`Btn_MasterReset` — `220 × 38` px)**:
     - Positioned at `Left = 1680, Top = 56`.
     - Styled in deep charcoal with a bold red border and red `RESET FAULTS` label.
     - **Function:** Executes a global loop across all 88 PLC tags (`V001_Healthy` through `V088_Healthy`), writing `TRUE` (`1`) to clear any latched faults or feedback disagree conditions across the vessel.
2. **88 Annunciator Diagnostic Cards (`Y = 113..1070 px`)**:
   - Replaces general control text with explicit diagnostic status:
     - Normal channels display **`HEALTHY`** (Green border/text).
     - Faulted channels display **`FAULT`** with a bold **2-pixel Crimson Red border**, immediately drawing the operator's eye to the faulted valve.
     - Unconfigured channels display **`UNCFGD`** in muted grey.
   - Clicking any diagnostic tile opens `Screen_Popup` for direct maintenance and troubleshooting.

### 4.3 Screen 3: Select-Before-Operate Faceplate Popup (`Screen_Popup`)
`Screen_Popup` is a compact, highly optimized `460 × 360` pixel dialog engineered to provide Select-Before-Operate (SBO) control without obscuring the background process view.

```
       +-------------------------------------------------------------+
       | VALVE V-001 — CONTROL PANEL                       [X CLOSE] |
       +-------------------------------------------------------------+
       | Status Card:  OPEN                         | Mode: AUTO     |
       +------------------------------+------------------------------+
       |                              |          +--------+          |
       |     [ ▲ OPEN VALVE ]         |          |  XXXX  |          |
       |     (Writes OpenCmd = 1)     |          | XXXXXX | (90px)   |
       |                              |          |  XXXX  |          |
       |     [ ▼ CLOSE VALVE ]        |          +--------+          |
       |     (Writes CloseCmd = 1)    |            VALVE OPEN        |
       +------------------------------+------------------------------+
       |                   [ ⚡ RESET FAULT ]                        |
       +-------------------------------------------------------------+
```

1. **Dead-Center Positioning (`730, 360`)**:
   - On a 1920 × 1080 screen, a `460 × 360` popup is centered precisely at:
     $$\text{Left} = \frac{1920 - 460}{2} = 730 \text{ px}, \quad \text{Top} = \frac{1080 - 360}{2} = 360 \text{ px}$$
2. **SBO Layout Geometry & Elements**:
   - **Header (`Y = 0..38 px`)**: Deep navy title bar displaying the selected valve name (`VALVE V-xxx — CONTROL PANEL`) with a dedicated `✕ CLOSE` button (`Pop_CloseX`) at the top right.
   - **Status Card (`Y = 46..86 px`)**: Full-width card displaying real-time state (`OPEN`, `CLOSED`, `IN TRANSIT`, `FAULT`, `LOCAL MODE`) and control mode (`AUTO (REMOTE)` vs `LOCAL MODE`).
   - **Command Controls (`Left Column, Y = 100..260 px`)**:
     - **▲ OPEN VALVE Button (`200 × 48` px)**: Distinct green border. Writes `1` to `Vxxx_OpenCmd`. Interlocked when in Local Mode or Fault.
     - **▼ CLOSE VALVE Button (`200 × 48` px)**: Dark border. Writes `1` to `Vxxx_CloseCmd`. Interlocked when in Local Mode or Fault.
   - **Visual Annunciator (`Right Column, X = 285 px`)**:
     - **Status Circle (`Pop_Dot` — `90 × 90` px)**: Centered vertically between `Y = 155..245`. Fills with green, dark grey, blue, amber, or red depending on real-time feedback.
     - **State Text Label (`Y = 255 px`)**: Bold textual reinforcement below the circle.
   - **Fault Reset Control (`Y = 292..338 px`)**:
     - **⚡ RESET FAULT Button (`430 × 46` px)**: Full-width button with a bold red border. Writes `1` to `Vxxx_Healthy` to clear local channel faults, leaving an ergonomic 22-pixel bottom margin.

---

## 5. CORE TECHNICAL IMPLEMENTATIONS & ARCHITECTURE SOLUTIONS

### 5.1 WinCC Unified 7-Argument Popup Signature
A critical technical achievement in this release is solving the WinCC Unified MTP1500 JavaScript runtime failure during `OpenScreenInPopup` execution.

#### Root Cause Analysis:
In Siemens WinCC Unified JavaScript API, `HMIRuntime.UI.SysFct.OpenScreenInPopup` accepts **exactly 7 arguments**:
```typescript
HMIRuntime.UI.SysFct.OpenScreenInPopup(
  PopupName: string,      // 1. Unique window identifier
  ScreenName: string,     // 2. Name of screen to render
  TagPrefix: string,      // 3. Optional tag multiplexing prefix / title
  Left: number,           // 4. Horizontal X coordinate in pixels
  Top: number,            // 5. Vertical Y coordinate in pixels
  ShowHeader: boolean,    // 6. Whether to render default WinCC title bar
  AllowMove: boolean      // 7. Whether operator can drag window
);
```
Previously, an 8th string argument (`"VALVE CONTROL PANEL"`) was inserted before the coordinates. Because the JavaScript runtime maps arguments by ordinal position, string `"VALVE CONTROL PANEL"` was passed into ordinal position #4 (`Left` X coordinate). The engine attempted to cast the string to an integer, resulting in `NaN` (Not-a-Number), which silently aborted screen creation and prevented the popup from opening.

#### Production Solution:
In [GenerateHmiLayout.cs](file:///c:/Users/Admin/Documents/Automation/valveDemo2/src/GenerateHmiLayout.cs#L900-L905), the script generator was updated to pass exactly 7 arguments, setting `ShowHeader = false` to suppress the default WinCC blue header and reveal our custom dark header bar:
```javascript
HMIRuntime.UI.SysFct.OpenScreenInPopup(
  "Popup_Valve",  // 1. Popup name
  "Screen_Popup", // 2. Screen name
  "",             // 3. Tag prefix (Empty string)
  730,            // 4. Left X coordinate (Dead center for 460px popup)
  360,            // 5. Top Y coordinate (Dead center for 360px popup)
  false,          // 6. ShowHeader = false (Uses our custom sleek header)
  false           // 7. AllowMove = false (Fixed centered modal)
);
```

### 5.2 Compiler Optimization (`AutomaticTags` & System Clock Heartbeat)
During initial HMI compilation in TIA Portal V20, 4 script compilation errors occurred across `Screen_Alarms`:
```text
Al_KPI_Faults  -> The configured tag is invalid.
Al_KPI_Healthy -> The configured tag is invalid.
Al_KPI_Banner  -> The configured tag is invalid. (2 times)
```

#### Root Cause Analysis:
In WinCC Unified, when a Script Dynamization uses Trigger Type **`AutomaticTags`**, the TIA Portal compiler performs static code analysis on the JavaScript source text, searching for literal strings inside `Tags("...")` calls to subscribe to runtime tag change events. Because the KPI summary banners used a dynamic loop concatenation (`Tags("V" + ("000" + i).slice(-3) + "_Healthy").Read()`), the static parser found no valid literal tag names, causing the compilation to fail.

#### Production Solution:
We injected a static literal read of the PLC 1.0 Hz square-wave system clock tag (`Valves_DB_Clock1Hz`) at the very top of each KPI summary script:
```javascript
function readTag(v) { return (v !== null && typeof v === "object" && "Value" in v) ? v.Value : v; }
Tags("Valves_DB_Clock1Hz").Read(); // 1. Satisfies static compiler parser
                                   // 2. Forces GUI evaluation every 1000 ms

let faults = 0;
for (let i = 1; i <= 88; i++) {
    let num = ("000" + i).slice(-3);
    let configured = readTag(Tags("V" + num + "_Configured").Read());
    let healthy = readTag(Tags("V" + num + "_Healthy").Read());
    let open = readTag(Tags("V" + num + "_OpenFB").Read());
    let closed = readTag(Tags("V" + num + "_ClosedFB").Read());
    if (configured && (!healthy || (open && closed))) {
        faults++;
    }
}
return "ACTIVE FAULTS:  " + faults;
```
This single architectural change achieves two goals simultaneously:
1. **Zero Compiler Errors:** TIA Portal V20 compiles `HMI_1` cleanly with **0 Errors**.
2. **Reliable Live Refresh:** As the PLC clock pulses every 1.0 second, WinCC Unified automatically re-runs the KPI summary loops, ensuring plant-wide fault and health counts never lag.

### 5.3 Automated TIA Portal Openness Engineering Pipeline (`GenerateHmiLayout.cs`)
To eliminate human error across 88 valves (which would require manually configuring over 600 tags, 176 screen buttons, and thousands of script lines), the entire HMI architecture is generated programmatically via **Siemens TIA Portal Openness API**.

The automation tool (`src\GenerateHmiLayout.cs` compiled to `src\GenerateHmiLayout.exe`) executes a 4-step engineering pipeline:
1. **Project Attachment:** Connects to the active TIA Portal V20 instance and verifies `valveDemo2`.
2. **PLC XML Verification:** Verifies/imports `Valves_DB` and `FB_ValveLoop` XML definitions.
3. **HMI Tag Generation (`[STEP 2]`)**: Automatically creates internal popup tags (`SelectedValve`, `Pop_Configured`, `Pop_OpenFB`, etc.) and iterates from 1 to 88 to generate 616 external HMI tags bound directly to `PLC_1`.
4. **Screen Rebuilding (`[STEP 1]`)**:
   - Deletes existing screens and creates fresh 1920 × 1080 screens (`Screen_1` and `Screen_Alarms`) and the `460 × 360` modal (`Screen_Popup`).
   - Mathematically calculates grid coordinates (`X = 20 + col * 172`, `Y = 113 + row * 118`) and injects optimized JavaScript dynamizations and click handlers for all 176 buttons.

---

## 6. FACTORY ACCEPTANCE TEST (FAT) & SHIPBOARD COMMISSIONING GUIDE

### 6.1 Why the Project is 100% FAT Ready
The current software package is completely ready for **Factory Acceptance Testing (FAT)** and customer demonstration without any modifications:
- **Zero Compilation Errors:** Complete validation in TIA Portal V20.
- **Complete End-to-End Simulation:** Because `FB_ValveLogic` incorporates 5-second travel timers (`TON_TIME`), an inspector can click any valve from `V-001` to `V-088`, issue an OPEN or CLOSE command via `Screen_Popup`, observe the blue `IN TRANSIT` state, and verify green `OPEN` or grey `CLOSED` feedback arrival.
- **Comprehensive Fault Simulation:** Inspectors can test Feedback Disagree alarms, trigger Local Mode override interlocks, verify Unconfigured grey-out states, and test global fault clearing via the Master Reset button on `Screen_Alarms`.

### 6.2 Transitioning from Simulation to Physical Marine Actuators
When deploying to the vessel's live S7-1200 / S7-1500 PLC connected to physical Phoenix interposing relays and **Pleiger EHS Hydraulic Actuators** (230 VAC power / 24 VDC signaling), perform the following commissioning step:
- **Bypass or Remove Simulation Block:** In `FB_ValveLoop`, replace the simulation call (`FB_ValveLogic`) with physical digital I/O mapping from the marine Remote I/O cabinets:
  - `Valve.OpenFB` $\leftarrow$ Field DI (Actuator OPEN limit switch / NAMUR sensor).
  - `Valve.ClosedFB` $\leftarrow$ Field DI (Actuator CLOSED limit switch / NAMUR sensor).
  - `Valve.LocalMode` $\leftarrow$ Field DI (Local control station switch).
  - `Valve.Healthy` $\leftarrow$ Field DI (Actuator circuit breaker / Pleiger EHS health contact).

### 6.3 Mandatory Marine Interlocks (Travel Timeout, Disagree & Communication Loss)
To comply with DNV-GL, ABS, and Lloyd's Register marine automation rules and eliminate single-points-of-failure, the commissioning engineer must ensure the PLC block enforces three critical safety interlocks:

```scl
// =============================================================================
// 1. TRAVEL TIMEOUT PROTECTION (Mandatory Marine Classification Rule)
// Protects hydraulic power units and actuator solenoid coils if a valve jam occurs.
// ==============================================================================
IF (Valve.OpenCmd AND NOT Valve.OpenFB) OR (Valve.CloseCmd AND NOT Valve.ClosedFB) THEN
    Valve.TravelTimer(IN := TRUE, PT := T#25s); // 25s max hydraulic travel limit
    IF Valve.TravelTimer.Q THEN
        Valve.TravelTimeout := TRUE; // 1. Flags TRAVEL TIMEOUT alarm on HMI
        Valve.Healthy := FALSE;      // 2. Trips general fault status
        Valve.OpenCmd := FALSE;      // 3. Immediately drop DO commands to protect
        Valve.CloseCmd := FALSE;     //    actuator motor & hydraulic pump
    END_IF;
ELSE
    Valve.TravelTimer(IN := FALSE, PT := T#25s);
END_IF;

// ==============================================================================
// 2. DUAL SENSOR FAULT / FEEDBACK DISAGREE (Already Implemented)
// Protects against sensor short-circuits or mechanical linkage disconnection.
// ==============================================================================
IF Valve.OpenFB AND Valve.ClosedFB THEN
    Valve.Healthy := FALSE; // Flags Crimson Red FAULT state & blocks SBO commands
END_IF;

// ==============================================================================
// 3. CABINET / FIELDBUS COMMUNICATION LOSS
// Binds Profinet / Remote-IO node diagnostic SFC (DeviceStates) to channel health.
// ==============================================================================
IF NOT Remote_IO_Cabinet_Online THEN
    Valve.Configured := FALSE; // Displays Grey "UNCONFIGURED" & disables SBO button
END_IF;
```

### 6.4 Vessel Blackout Recovery & Non-Retentive Command Safety
In marine shipboard systems, power continuity is subject to generator handovers and potential blackouts. To ensure vessel safety:
1. **Pulsed Solenoid Commands:** `DO_OPEN` and `DO_CLOSE` outputs to the Pleiger EHS actuators should be configured as pulses (e.g., 3-second pulse to latching hydraulic solenoids) or interlocked to drop immediately once `OpenFB` or `ClosedFB` is asserted.
2. **Non-Retentive Command Memory:** In `Valves_DB`, ensure that `OpenCmd` and `CloseCmd` are stored in **non-retentive (unlatched upon warm restart) memory**. If a vessel blackout occurs and the PLC reboots upon emergency generator restoration, the PLC will never inadvertently re-energize solenoid valves, preserving physical valve positions until an operator explicitly issues a new command from `Screen_Popup`.

---
**END OF TECHNICAL DOCUMENTATION**  
*88-Valve Marine SCADA Control & Diagnostic System — Siemens TIA Portal V20 / WinCC Unified MTP1500*
