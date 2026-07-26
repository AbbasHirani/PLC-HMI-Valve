# Project Overview & Engineering Specification

## 1. Project Objective
Build a PLC-based simulation system to remotely control and monitor motorized valves. The system must support runtime dynamic valve instantiation from the HMI (WinCC Unified) without requiring PLC software updates or program redeployments (compile/download cycles) in TIA Portal V20.

Per valve instance, the system needs to support:
* **Remote Commands:** Open and Close control.
* **Feedbacks:** Open Limit Switch, Closed Limit Switch, Healthy Status, and Local/Remote Mode indication.
* **Alarms:** Active Fault and Travel Timeout (valve failed to reach limit switch within designated time).
* **Dynamic Tag Bindings:** Handled completely in simulation with zero hardware dependencies.

---

## 2. Current System Status
* **Hardware Target:** Siemens S7-1200 PLC, ET200SP Remote I/O, and WinCC Unified HMI.
* **Current HMI Project Layout:** `Screen_1` contains:
  * `Button_1`
  * `Button_2`
  * `Faceplate container_1` (linked to `V0.0.2\Valve_Faceplate`)
  * `IO field_1`
* **Connectivity:** A C# Openness console application successfully attaches to the running TIA Portal V20 instance and can recursively query devices, screens, and specific properties (dimensions, attributes) and read the UDT interface parameters of the HMI faceplate instance.
* **Online State:** Project is currently verified in **Offline** configuration state.

---

## 3. Full System Architecture (How Dynamic Valve Addition Works)

To allow operators to dynamically add valves at runtime, we cannot compile PLC block logic dynamically. Therefore, we split the logic into a pre-allocated PLC UDT pool and dynamic HMI screen window/multiplexing.

```mermaid
graph TD
    subgraph HMI (WinCC Unified)
        A[HMI Screen Window Grid] -->|Binds index i| B[Scrollable Multiplexed Containers]
        C[Add Valve Screen] -->|Writes config to| D[PLC DB_Valves.Valves[i]]
    end
    subgraph PLC (S7-1200)
        D --> E[Valves: Array[1..100] of UDT_Valve]
        F[Dynamic I/O Mapper Block] -->|Reads configured indices| E
        E --> G[Cyclic Logic Loop: Valve FB]
    end
```

### PLC Architecture (The Dynamic Array UDT Pool)
1. **User Defined Type (UDT_Valve_Config):**
   * `Enabled` (Bool)
   * `Name` (String[20])
   * `OpenFb_Channel` (Int) - mapped ET200SP remote I/O input channel index
   * `ClosedFb_Channel` (Int)
   * `Healthy_Channel` (Int)
   * `LocalMode_Channel` (Int)
   * `OpenCmd_Channel` (Int)
   * `CloseCmd_Channel` (Int)
   * `TravelTimeout` (Time)
2. **User Defined Type (UDT_Valve_Status):**
   * `Open` (Bool)
   * `Closed` (Bool)
   * `Fault` (Bool)
   * `TimeoutAlarm` (Bool)
   * `LocalMode` (Bool)
3. **Valves Data Block (DB_Valves):**
   * `Valves` : `Array[1..100] of UDT_Valve` (which contains Config and Status structs).
4. **Dynamic I/O Mapping FB (Run-time Mapping):**
   * Read the physical ET200SP remote I/O input arrays into a shared DB buffer.
   * A mapping block runs a `FOR` loop: it maps the digital state of the inputs to `Valves[i].Status.OpenFB` using the index configured in `Valves[i].Config.OpenFb_Channel`.
5. **Cyclic Logic FB (FB_ValveControl):**
   * Executes inside a `FOR i := 1 TO 100` loop. If `Valves[i].Config.Enabled` is true, it runs the opening/closing valve sequence, tracks the travel timers, and triggers alarms if thresholds are exceeded.

### HMI Architecture (The UI and Parameter Bindings)
To display these valves, we cannot write JavaScript in WinCC Unified runtime to instantiate new graphical objects (this is a hard platform limitation). We must design the HMI in one of two ways:
* **Option A (Multiplexed Viewport - Recommended for Operator Runtime):** A screen window containing a fixed array of pre-instantiated faceplates (e.g., 20 valve containers in a grid). When a valve is enabled in the PLC, its container on HMI becomes visible, and its tags are dynamically multiplexed to `DB_Valves.Valves[i]`.
* **Option B (Programmatic Creation via Openness - Engineering Automation):** An external C# script automatically creates, positions, and binds actual buttons, ellipses, and input fields on a screen within TIA Portal. Useful for mass-generating valve screens *before* compiling the project, rather than during runtime.

---

## 4. HMI UI Automation Capability Analysis

To ensure no false assumptions are made, we performed reflection on `Siemens.Engineering.dll` to check what controls and shapes are programmatically instantiable via TIA Portal Openness.

### What is Programmatically Possible via the API
* **Create UI Controls on Screens:** We can call `screen.ScreenItems.Create<T>(string name)` on `HmiScreenItemBaseComposition`.
* **Standard Siemens UI Elements Supported:**
  * **Widgets:** `HmiButton`, `HmiIOField`, `HmiLabel`, `HmiSymbolicIOField`, `HmiToggleSwitch`, `HmiTouchArea`.
  * **Shapes:** `HmiEllipse`, `HmiRectangle`, `HmiCircle`, `HmiLine`, `HmiText`, `HmiGraphicView`.
  * **Containers:** `HmiFaceplateContainer`, `HmiScreenWindow`.
* **Formatting and Positioning:** We can read/write properties like `Left`, `Top`, `Width`, `Height`, and style attributes such as background colors or texts.

### What MUST be Done Manually (Platform Limitations)
1. **Designing Internal Faceplate Type Layouts:**
   * While we can programmatically place a `HmiFaceplateContainer` on a screen and link it to an existing faceplate type (e.g. `V0.0.2\Valve_Faceplate`), we **cannot** programmatically design or modify the internal visual layout (graphics, internal buttons/lines) of a Faceplate Type itself via Openness. Faceplate Types are library objects and must be drawn manually using TIA Portal's graphics editor.
   * **Action:** You must design the graphical layout of `Valve_Faceplate` manually once in the TIA Portal Library. Once created, we can automate the instantiation and tag bindings of this faceplate container as many times as needed.
2. **Setting Complex Dynamizations (Scripts):**
   * Binding simple tags to HMI properties is supported in Openness, but creating complex runtime JavaScript dynamizations inside screen item events programmatically is highly restricted. Script events should be defined inside the Faceplate Type definition manually.

---

## 5. Siemens Standard HMI UI Aesthetics
We will configure the UI elements to adhere strictly to Siemens standard representation guidelines (similar to the Siemens APL - Advanced Process Library style):
* **Colors:**
  * **Background:** Light Gray (RGB 220, 220, 220) or standard Dark Theme option.
  * **Valves / States:**
    * *Closed (Inactive):* Solid Gray (RGB 128, 128, 128)
    * *Open (Active):* Solid Green (RGB 0, 200, 0)
    * *Transitional / Moving:* Flashing or Solid Yellow (RGB 255, 230, 0)
    * *Faulted / Alarm:* Flashing Red (RGB 255, 0, 0)
  * **Buttons:** Standard Siemens rectangular 3D style with clear text labels ("OPEN", "CLOSE").
  * **Status Ellipses:** Four small indicators aligned to show:
    1. *LS_Open* (Limit Switch Open)
    2. *LS_Closed* (Limit Switch Closed)
    3. *Healthy* (Device status ok)
    4. *Local* (Local manual operation mode active)
