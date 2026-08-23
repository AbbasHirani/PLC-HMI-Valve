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
    var t=all.FirstOrDefault(x=>{try{return x.Name=="HmiScreenItemBaseComposition";}catch{return false;}});
    if(t==null){Console.WriteLine("type not found");return;}
    Console.WriteLine("=== HmiScreenItemBaseComposition methods ===");
    foreach(var m in t.GetMethods(BindingFlags.Public|BindingFlags.Instance).OrderBy(x=>x.Name)){
      if(m.Name.StartsWith("get_")||m.Name.StartsWith("set_"))continue;
      var ps=string.Join(", ", m.GetParameters().Select(p=>p.ParameterType.Name+" "+p.Name));
      var gen=m.IsGenericMethodDefinition?"<"+string.Join(",",m.GetGenericArguments().Select(g=>g.Name))+">":"";
      Console.WriteLine("   "+m.Name+gen+"("+ps+")");
    }
  }
}
