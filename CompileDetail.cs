using System;
using System.IO;
using System.Collections;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;

class Program {
    static void Main() {
        var portal = TiaPortal.GetProcesses()[0].Attach();
        var project = portal.Projects[0];
        Device hmiDevice = null;
        foreach (var d in project.Devices)
            if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) hmiDevice = d;
        if (hmiDevice == null) { Console.WriteLine("HMI device not found."); return; }

        Console.WriteLine("Compiling HMI Device: " + hmiDevice.Name);
        object compilable = GetCompilableObj(hmiDevice);
        var compileM = compilable.GetType().GetMethod("Compile", new Type[0]);
        object result = compileM.Invoke(compilable, null);

        Console.WriteLine("  State:   " + GetPropStr(result, "State"));
        Console.WriteLine("  Errors:  " + GetPropStr(result, "ErrorCount"));
        Console.WriteLine("  Warnings:" + GetPropStr(result, "WarningCount"));

        var msgs = GetPropObj(result, "Messages");
        if (msgs is IEnumerable) DumpMessages((IEnumerable)msgs, 2);
    }

    static object GetCompilableObj(Device device) {
        object s = GetServiceReflection(device, "Siemens.Engineering.Compiler.ICompilable");
        if (s != null) return s;
        foreach (DeviceItem item in device.DeviceItems) {
            var sub = GetCompilableInItemObj(item);
            if (sub != null) return sub;
        }
        return null;
    }
    static object GetCompilableInItemObj(DeviceItem item) {
        foreach (DeviceItem sub in item.DeviceItems) {
            object s = GetServiceReflection(sub, "Siemens.Engineering.Compiler.ICompilable");
            if (s != null) return s;
            var child = GetCompilableInItemObj(sub);
            if (child != null) return child;
        }
        return null;
    }
    static object GetServiceReflection(object target, string serviceTypeName) {
        try {
            foreach (var m in target.GetType().GetMethods()) {
                if (m.Name == "GetService" && m.IsGenericMethod && m.GetParameters().Length == 0) {
                    Type st = typeof(TiaPortal).Assembly.GetType(serviceTypeName);
                    if (st == null) foreach (var ass in AppDomain.CurrentDomain.GetAssemblies()) { st = ass.GetType(serviceTypeName); if (st != null) break; }
                    if (st != null) return m.MakeGenericMethod(st).Invoke(target, null);
                }
            }
        } catch {}
        return null;
    }
    static void DumpMessages(IEnumerable messages, int indent) {
        string ind = new string(' ', indent * 2);
        int count = 0;
        foreach (object msg in messages) {
            if (count > 40) break;
            string state = GetPropStr(msg, "State");
            if (state == "Error" || state == "Warning") {
                Console.WriteLine(ind + "[" + state + "] " + GetPropStr(msg, "Description"));
                if (state == "Error") {
                    foreach (var p in msg.GetType().GetProperties()) {
                        if (p.Name == "Messages" || p.Name == "Description" || p.Name == "State") continue;
                        string v = GetPropStr(msg, p.Name);
                        if (!string.IsNullOrEmpty(v)) Console.WriteLine(ind + "        " + p.Name + " = " + v);
                    }
                }
                count++;
            }
            var child = GetPropObj(msg, "Messages");
            if (child is IEnumerable) DumpMessages((IEnumerable)child, indent + 1);
        }
    }
    static string GetPropStr(object obj, string name) { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }
    static object GetPropObj(object obj, string name) { try { var p = obj.GetType().GetProperty(name); return p != null ? p.GetValue(obj, null) : null; } catch { return null; } }
}
