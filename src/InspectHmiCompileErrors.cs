using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;

namespace InspectHmiCompileErrors
{
    class Program
    {
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int idx = args.Name.IndexOf(',');
            string name = idx == -1 ? args.Name : args.Name.Substring(0, idx);
            string[] bases = {
                @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20",
                @"C:\Program Files\Siemens\Automation\Portal V20\Bin\PublicAPI",
                @"C:\Program Files\Siemens\Automation\Portal V20\Bin"
            };
            foreach (var b in bases) { string p = Path.Combine(b, name + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            try { Run(); }
            catch (Exception ex) { Console.WriteLine("[ERROR] " + ex); }
            Console.WriteLine("\nPress Enter to exit..."); try { Console.ReadLine(); } catch {}
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("No TIA Portal running."); return; }
            Console.WriteLine("Attaching to TIA Portal...");
            TiaPortal portal  = procs[0].Attach();
            Project   project = portal.Projects[0];

            Device hmiDevice = null;
            foreach (var d in project.Devices) if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) { hmiDevice = d; break; }
            if (hmiDevice == null) { Console.WriteLine("HMI Device not found."); return; }

            object compilable = GetCompilableObj(hmiDevice);
            if (compilable == null) { Console.WriteLine("ICompilable service not found on HMI device."); return; }

            Console.WriteLine("Compiling HMI via Openness...");
            MethodInfo compileM = compilable.GetType().GetMethod("Compile", new Type[0]);
            if (compileM == null) { Console.WriteLine("Compile method not found on service."); return; }

            object result = compileM.Invoke(compilable, null);
            if (result == null) { Console.WriteLine("Compile returned null."); return; }

            string state = GetPropStr(result, "State");
            string errorCount = GetPropStr(result, "ErrorCount");
            string warningCount = GetPropStr(result, "WarningCount");

            Console.WriteLine("Compile State: " + state);
            Console.WriteLine("Error Count: "   + errorCount);
            Console.WriteLine("Warning Count: " + warningCount);

            string outFile = @"C:\Users\Admin\Documents\Automation\valveDemo2\hmi_compile_errors.txt";
            using (var w = new StreamWriter(outFile))
            {
                w.WriteLine("=== HMI COMPILE ERRORS ===");
                w.WriteLine("Time: " + DateTime.Now);
                w.WriteLine("State: " + state);
                w.WriteLine("Error Count: " + errorCount);
                w.WriteLine("Warning Count: " + warningCount);
                w.WriteLine();

                object msgs = GetPropObj(result, "Messages");
                if (msgs is IEnumerable)
                {
                    DumpMessages((IEnumerable)msgs, w, 0);
                }
            }

            Console.WriteLine("\nCompile report written to " + outFile);
        }

        static object GetCompilableObj(Device device)
        {
            object s = GetServiceReflection(device, "Siemens.Engineering.Compiler.ICompilable");
            if (s != null) return s;

            foreach (DeviceItem item in device.DeviceItems)
            {
                s = GetServiceReflection(item, "Siemens.Engineering.Compiler.ICompilable");
                if (s != null) return s;
                object sub = GetCompilableInItemObj(item);
                if (sub != null) return sub;
            }
            return null;
        }

        static object GetCompilableInItemObj(DeviceItem item)
        {
            foreach (DeviceItem sub in item.DeviceItems)
            {
                object s = GetServiceReflection(sub, "Siemens.Engineering.Compiler.ICompilable");
                if (s != null) return s;
                object child = GetCompilableInItemObj(sub);
                if (child != null) return child;
            }
            return null;
        }

        static object GetServiceReflection(object target, string serviceTypeName)
        {
            try {
                Type targetType = target.GetType();
                foreach (var m in targetType.GetMethods()) {
                    if (m.Name == "GetService" && m.IsGenericMethod && m.GetParameters().Length == 0) {
                        Type serviceType = typeof(TiaPortal).Assembly.GetType(serviceTypeName);
                        if (serviceType == null) {
                            foreach (var ass in AppDomain.CurrentDomain.GetAssemblies()) {
                                serviceType = ass.GetType(serviceTypeName);
                                if (serviceType != null) break;
                            }
                        }
                        if (serviceType != null) {
                            var gMethod = m.MakeGenericMethod(serviceType);
                            return gMethod.Invoke(target, null);
                        }
                    }
                }
            } catch {}
            return null;
        }

        static void DumpMessages(IEnumerable messages, StreamWriter w, int indent)
        {
            string ind = new string(' ', indent * 2);
            foreach (object msg in messages)
            {
                string severity = GetPropStr(msg, "Severity");
                string path     = GetPropStr(msg, "Path");
                string desc     = GetPropStr(msg, "Description");
                w.WriteLine(string.Format("{0}[{1}] Path: {2} | Description: {3}", ind, severity, path, desc));
                Console.WriteLine(string.Format("{0}[{1}] {2}", ind, severity, desc));

                object childMsgs = GetPropObj(msg, "Messages");
                if (childMsgs is IEnumerable)
                {
                    DumpMessages((IEnumerable)childMsgs, w, indent + 1);
                }
            }
        }

        static string GetPropStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }

        static object GetPropObj(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? p.GetValue(obj, null) : null; } catch { return null; } }

        static HmiSoftware FindHmiSoftware(Device device)
        { foreach (DeviceItem it in device.DeviceItems) { var r = FindHmiSoftwareInItem(it); if (r != null) return r; } return null; }
        static HmiSoftware FindHmiSoftwareInItem(DeviceItem it)
        { var c = it.GetService<SoftwareContainer>(); if (c != null && c.Software is HmiSoftware) return c.Software as HmiSoftware; foreach (DeviceItem sub in it.DeviceItems) { var r = FindHmiSoftwareInItem(sub); if (r != null) return r; } return null; }
    }
}
