using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

static class PlayerIntegrationChecks
{
 static Type context=null!;
 static int checks;
 const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static object? Get(string property)=>context.GetProperty(property,Static)!.GetValue(null);
 static object? Invoke(string method,params object[] args)=>context.GetMethod(method,Static)!.Invoke(null,args);
 static void Configure(params string[] args)=>Invoke("Configure",new object[]{args});
 static void Reset(){context.GetProperty("LauncherRoot",Static)!.SetValue(null,null);context.GetProperty("DataDirectory",Static)!.SetValue(null,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MiniPlayer"));}
 static void Reject(string label,Action action)
 {
  bool rejected=false;try{action();}catch(TargetInvocationException ex) when(ex.InnerException is ArgumentException or InvalidOperationException or IOException){rejected=true;}
  Check(rejected,label);
 }
 [STAThread] public static int Main(string[] args)
 {
  try
  {
   string root=Path.GetFullPath(args[0]),data=Path.Combine(root,"Data","mini-player"),marker=Path.Combine(root,"launcher-install.json");
   var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"MiniPlayer.dll"));context=assembly.GetType("MiniPlayer.LaunchContext",true)!;
   File.WriteAllText(marker,"{\"ProductId\":\"MiniPlayer.WidgetLauncher\"}");
   Configure("--launcher-root",root,"--data-dir",data,"--settings");
   Check((string?)Get("LauncherRoot")==root&&(string?)Get("DataDirectory")==data,"Managed player uses launcher Data directory");
   string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(data)).ToUpperInvariant())))[..16];
   Check((string?)Get("InstanceName")=="Local\\MiniPlayer."+hash,"Player instance name matches launcher protocol");
   Check((string?)Invoke("PathId",data.ToUpperInvariant()+"\\")==hash,"Instance hash normalizes case and trailing separators");
   Reset();Configure();Check((string?)Get("LauncherRoot")==root,"Directly opening installed player detects launcher context");
   Reset();Reject("Data directory without launcher root is rejected",()=>Configure("--data-dir",data));
   Reject("Missing argument is rejected",()=>Configure("--launcher-root"));
   Reject("Mismatched launcher root is rejected",()=>Configure("--launcher-root",Path.Combine(root,"other")));
   Reject("External settings directory is rejected",()=>Configure("--launcher-root",root,"--data-dir",Path.Combine(root,"outside")));
   File.WriteAllText(marker,"{\"ProductId\":\"OtherProduct\"}");Reject("Unrelated launcher marker is rejected",()=>Configure("--launcher-root",root));
   File.Delete(marker);Reset();Configure();
   Check(Get("LauncherRoot")==null&&(string?)Get("InstanceName")=="Local\\MiniPlayer","Standalone launch retains legacy identity");
   Check((string?)Get("DataDirectory")==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MiniPlayer"),"Standalone settings path stays compatible");
   Reject("Explicit managed launch requires install marker",()=>Configure("--launcher-root",root));
   File.WriteAllText(marker,"{\"ProductId\":\"MiniPlayer.WidgetLauncher\"}");Configure("--launcher-root",root);
   var playerType=assembly.GetType("MiniPlayer.Player",true)!;
   var player=RuntimeHelpers.GetUninitializedObject(playerType);
   playerType.GetMethod("ConfirmUninstall",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(player,null);
   string request=Path.Combine(root,"launcher-request.txt");
   for(int i=0;i<50&&!File.Exists(request);i++)Thread.Sleep(50);
   Check(File.Exists(request)&&File.ReadAllText(request)=="--remove-widget|mini-player","Managed uninstall delegates widget removal to launcher");
   Check(File.Exists(marker)&&File.Exists(Path.Combine(AppContext.BaseDirectory,"MiniPlayer.dll")),"Delegated uninstall does not delete player or launcher files");
   Directory.CreateDirectory(Path.Combine(root,"Data"));Directory.Move(Path.Combine(root,"data-junction-fixture"),data);
   Reset();Reject("Managed data directory cannot be a junction",()=>Configure("--launcher-root",root));
   Check(File.ReadAllText(Path.Combine(root,"outside-target","keep.txt"))=="keep","Rejected redirected data leaves outside target untouched");
   Console.WriteLine("TOTAL "+checks+" PASS");return 0;
  }
  catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
