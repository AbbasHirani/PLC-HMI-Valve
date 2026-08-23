using System; using System.Linq; using System.Reflection;
class Program {
  const string API=@"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20";
  static void Main(){
    AppDomain.CurrentDomain.AssemblyResolve += (s,e)=>{
      var n=new AssemblyName(e.Name).Name+".dll";
      foreach(var d in new[]{API,@"C:\Program Files\Siemens\Automation\Portal V20\Bin"}){
        var f=System.IO.Path.Combine(d,n); if(System.IO.File.Exists(f)) return Assembly.LoadFrom(f);} return null;};
    var asm=Assembly.LoadFrom(System.IO.Path.Combine(API,"Siemens.Engineering.dll"));
    Type[] all; try{all=asm.GetTypes();}catch(ReflectionTypeLoadException r){all=r.Types.Where(x=>x!=null).ToArray();}
    foreach(var tn in new[]{"HmiCustomWebControlContainer","HmiCustomControlInterface"}){
      var t=all.FirstOrDefault(x=>{try{return x.Name==tn;}catch{return false;}});
      Console.WriteLine("\n===== "+tn+" =====");
      if(t==null){Console.WriteLine("  not found");continue;}
      foreach(var p in t.GetProperties(BindingFlags.Public|BindingFlags.Instance))
        Console.WriteLine(string.Format("  {0,-28} : {1,-34} {2}",p.Name,p.PropertyType.Name,p.CanWrite?"(rw)":"(ro)"));
    }
  }
}
