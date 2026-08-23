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
    Console.WriteLine("=== properties mentioning storage / database / medium ===");
    foreach(var t in all.OrderBy(x=>{try{return x.FullName;}catch{return "";}})){
      string ns; try{ ns=t.Namespace; }catch{ continue; }
      if(ns==null||!ns.Contains("HmiUnified")) continue;
      PropertyInfo[] ps; try{ ps=t.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);}catch{continue;}
      foreach(var p in ps){
        var n=p.Name.ToLowerInvariant();
        if(n.Contains("storage")||n.Contains("database")||n.Contains("medium"))
          Console.WriteLine(string.Format("  {0,-34}.{1,-28} : {2,-22} {3}", t.Name, p.Name, p.PropertyType.Name, p.CanWrite?"(rw)":"(ro)"));
      }
    }
  }
}
