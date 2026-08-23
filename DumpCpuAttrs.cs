using System; using System.Linq; using Siemens.Engineering; using Siemens.Engineering.HW;
class Program {
  static void Main(){
    var proj = TiaPortal.GetProcesses()[0].Attach().Projects[0];
    foreach (Device d in proj.Devices) {
      if (d.Name.IndexOf("S7-1200",StringComparison.OrdinalIgnoreCase)<0 &&
          d.Name.IndexOf("HMI",StringComparison.OrdinalIgnoreCase)<0) continue;
      foreach (DeviceItem it in d.DeviceItems) Walk(d.Name, it, 1);
    }
  }
  static void Walk(string dev, DeviceItem item, int depth){
    if (depth>5) return;
    try {
      var eo=(IEngineeringObject)item;
      var names=eo.GetAttributeInfos().Select(i=>{try{return i.Name;}catch{return null;}})
                  .Where(x=>x!=null).OrderBy(x=>x).ToList();
      if (names.Count>0)
        Console.WriteLine("["+dev+" / "+item.Name+"]  ("+names.Count+" attrs)\n   "+string.Join(", ", names));
    } catch {}
    foreach (DeviceItem sub in item.DeviceItems) Walk(dev, sub, depth+1);
  }
}
