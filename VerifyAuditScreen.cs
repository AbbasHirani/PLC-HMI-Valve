using System; using System.Linq;
using Siemens.Engineering; using Siemens.Engineering.HW; using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW; using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
class Program {
  static void Main(){
    var proj = TiaPortal.GetProcesses()[0].Attach().Projects[0];
    Device dev=null; foreach(var d in proj.Devices) if(d.Name.IndexOf("HMI",StringComparison.OrdinalIgnoreCase)>=0) dev=d;
    var hmi=Find(dev);
    var sc=hmi.Screens.Find("Screen_Login");
    if(sc==null){Console.WriteLine("[ERROR] Screen_Login missing");return;}
    Console.WriteLine("Screen_Login items:");
    foreach(var it in sc.ScreenItems){
      string extra="";
      var cw = it as HmiCustomWebControlContainer;
      if(cw!=null) extra = "   ContainedType=" + cw.ContainedType + "  Auth=" + cw.Authorization
                          + "  " + cw.Left + "," + cw.Top + " " + cw.Width + "x" + cw.Height;
      Console.WriteLine("   " + it.GetType().Name + "  " + it.Name + extra);
    }
  }
  static HmiSoftware Find(Device d){foreach(DeviceItem i in d.DeviceItems){var r=F2(i); if(r!=null)return r;}return null;}
  static HmiSoftware F2(DeviceItem it){var c=it.GetService<SoftwareContainer>(); if(c!=null&&c.Software is HmiSoftware)return c.Software as HmiSoftware; foreach(DeviceItem s in it.DeviceItems){var r=F2(s); if(r!=null)return r;} return null;}
}
