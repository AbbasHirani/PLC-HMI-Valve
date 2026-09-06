using System;
using System.IO;
using System.Collections;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;

namespace CompileAllVerbose
{
    class Program
    {
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            int idx = args.Name.IndexOf(',');
            string name = idx == -1 ? args.Name : args.Name.Substring(0, idx);
            string[] bases = {
                // D:\Siemens first - the project moved machines 2026-08-31 and the old
                // C:\Program Files install is gone. Same fix as compile_tool.ps1.
                @"D:\Siemens\Portal V20\PublicAPI\V20",
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
            Console.WriteLine("Press Enter to exit...");
            try { Console.ReadLine(); } catch {}
        }

        static void Run()
        {
            var procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) { Console.WriteLine("No TIA Portal running."); return; }
            Console.WriteLine("Attaching to TIA Portal...");
            TiaPortal portal = procs[0].Attach();
            Project project = portal.Projects[0];

            Console.WriteLine("=========================================");
            Console.WriteLine("COMPILING PLC AND HMI USING OPENNESS API");
            Console.WriteLine("=========================================");

            Device plcDevice = null;
            Device hmiDevice = null;

            foreach (var d in project.Devices)
            {
                if (d.Name.IndexOf("PLC", StringComparison.OrdinalIgnoreCase) >= 0 || d.Name.IndexOf("S7-1200", StringComparison.OrdinalIgnoreCase) >= 0) plcDevice = d;
                if (d.Name.IndexOf("HMI", StringComparison.OrdinalIgnoreCase) >= 0) hmiDevice = d;
            }

            if (plcDevice != null)
            {
                Console.WriteLine("\n[1/2] Compiling PLC Device: " + plcDevice.Name);
                CompileDevice(plcDevice);
            }
            else
            {
                Console.WriteLine("\n[1/2] PLC Device not found!");
            }

            if (hmiDevice != null)
            {
                Console.WriteLine("\n[2/2] Compiling HMI Device: " + hmiDevice.Name);
                CompileDevice(hmiDevice);
            }
            else
            {
                Console.WriteLine("\n[2/2] HMI Device not found!");
            }
        }

        static void CompileDevice(Device device)
        {
            object compilable = GetCompilableObj(device);
            if (compilable == null)
            {
                Console.WriteLine("  [ERROR] ICompilable service not found on device.");
                return;
            }

            MethodInfo compileM = compilable.GetType().GetMethod("Compile", new Type[0]);
            if (compileM == null)
            {
                Console.WriteLine("  [ERROR] Compile method not found on service.");
                return;
            }

            object result = compileM.Invoke(compilable, null);
            if (result == null)
            {
                Console.WriteLine("  [ERROR] Compile returned null.");
                return;
            }

            string state        = GetPropStr(result, "State");
            string errorCount   = GetPropStr(result, "ErrorCount");
            string warningCount = GetPropStr(result, "WarningCount");

            Console.WriteLine("  Compile State:   " + state);
            Console.WriteLine("  Error Count:     " + errorCount);
            Console.WriteLine("  Warning Count:   " + warningCount);

            object msgs = GetPropObj(result, "Messages");
            if (msgs is IEnumerable)
            {
                DumpMessages((IEnumerable)msgs, 2);
            }
        }

        static object GetCompilableObj(Device device)
        {
            object s = GetServiceReflection(device, "Siemens.Engineering.Compiler.ICompilable");
            if (s != null) return s;

            foreach (DeviceItem item in device.DeviceItems)
            {
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

        static void DumpMessages(IEnumerable messages, int indent)
        {
            string ind = new string(' ', indent * 2);
            int count = 0;
            foreach (object msg in messages)
            {
                if (count > 40) break; // Dump at most 40 errors to avoid flooding
                string state = GetPropStr(msg, "State");
                string desc  = GetPropStr(msg, "Description");
                if (state == "Error" || state == "Warning")
                {
                    string path = GetPropStr(msg, "Path");
                    if (string.IsNullOrEmpty(path)) {
                        object pathObj = GetPropObj(msg, "Path");
                        path = pathObj != null ? pathObj.ToString() : "";
                    }
                    Console.WriteLine(string.Format("{0}[{1}] path='{2}' desc='{3}'", ind, state, path, desc));
                    count++;
                }
                object childMsgs = GetPropObj(msg, "Messages");
                if (childMsgs is IEnumerable)
                {
                    DumpMessages((IEnumerable)childMsgs, indent + 1);
                }
            }
        }

        static string GetPropStr(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? (p.GetValue(obj, null) ?? "").ToString() : ""; } catch { return ""; } }

        static object GetPropObj(object obj, string name)
        { try { var p = obj.GetType().GetProperty(name); return p != null ? p.GetValue(obj, null) : null; } catch { return null; } }
    }
}
