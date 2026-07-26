# Project Tasks Roadmap

## 1. Finished Tasks (Checked & Verified)
* [x] Establish a C# console project with runtime resolving of Siemens Openness assembly dependencies.
* [x] Support connecting to a running TIA Portal instance or falling back to opening the `.ap20` project file directly.
* [x] Recursively navigate device collections to locate the Unified HMI software target and specific screens.
* [x] Scan and list HMI screen items on `Screen_1` with their concrete types, faceplate type names, and versions.
* [x] Use strongly-typed Openness `GetAttributeInfos()` and `GetAttribute()` methods to read live attribute values (Left, Top, Width, Height, ContainedType) directly from the TIA Portal database.
* [x] Dynamically read and parse HMI Faceplate Container interface parameter bindings and their dynamization details (static values vs. tag dynamizations).
* [x] Determine native XML/SimaticML export capabilities and confirm the API limitation (Unified individual HMI controls do not support direct native export).
* [x] Verify HMI device connection states (online/offline) via the `OnlineProvider` service on HMI runtime device items.

---

## 2. Finished Tasks (Checked & Verified)
* [x] Establish a C# console project with runtime resolving of Siemens Openness assembly dependencies.
* [x] Support connecting to a running TIA Portal instance or falling back to opening the `.ap20` project file directly.
* [x] Recursively navigate device collections to locate the Unified HMI software target and specific screens.
* [x] Scan and list HMI screen items on `Screen_1` with their concrete types, faceplate type names, and versions.
* [x] Use strongly-typed Openness `GetAttributeInfos()` and `GetAttribute()` methods to read live attribute values (Left, Top, Width, Height, ContainedType) directly from the TIA Portal database.
* [x] Dynamically read and parse HMI Faceplate Container interface parameter bindings and their dynamization details (static values vs. tag dynamizations).
* [x] Determine native XML/SimaticML export capabilities and confirm the API limitation (Unified individual HMI controls do not support direct native export).
* [x] Verify HMI device connection states (online/offline) via the `OnlineProvider` service on HMI runtime device items.
* [x] Programmatically instantiate HmiButton, HmiEllipse, and HmiIOField controls on Screen_1.
* [x] Formulate centric positioning calculations for HmiEllipse indicators.
* [x] Set up TagDynamization and Range Mapping tables dynamically to configure states (0->Gray, 1->Green/Yellow) on Unified HMI screen objects.
* [x] Implement the dynamic index selection field and dynamic tag bindings to support runtime valve multiplexing from the HMI screen.

---

## 3. Current Tasks
* [x] Verify PLC dynamic valve arrays (`Valves_DB.Valve` holding 88 `Valve_IO` instances) and cyclic logic blocks (`FB_ValveLogic`, `FB_ValveLoop`, `FB_HMI_MIirror`) and confirm successful runtime simulation mapping.
* [x] Compile S7-1200 blocks and WinCC Unified screen layouts to verify there are no compilation errors.

---

## 4. Upcoming Tasks
* [ ] Integrate PLC software/hardware simulations and compile runtime builds.

