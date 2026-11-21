using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WidgetLauncher;

static class LauncherUninstallChecks
{
 [STAThread]static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.Combine(root,"Catalog"));File.WriteAllText(Path.Combine(root,"Catalog","widgets.json"),"[]");
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var window=new LauncherWindow(root,true,"settings");
  try{
   string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(root).ToUpperInvariant())))[..16];
   using(var attention=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\WidgetLauncher.Uninstall."+hash+".Attention")){
    var request=typeof(LauncherWindow).GetMethod("RequestLauncherUninstall",BindingFlags.NonPublic|BindingFlags.Instance)!;
    for(int i=0;i<3;i++){
     ((Task)request.Invoke(window,null)!).GetAwaiter().GetResult();
     if(!attention.WaitOne(1000))throw new Exception("Launcher did not signal existing confirmation");
    }
    if(typeof(LauncherWindow).GetField("uninstallProcess",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!=null)throw new Exception("Launcher started an extra uninstaller process");
   }
   Console.WriteLine("PASS Launcher repeated delete requests signal existing confirmation without launching another process");
   if(File.Exists(Path.Combine(root,"Uninstall.exe")))throw new Exception("Fixture unexpectedly contained real uninstaller");
   Console.WriteLine("PASS Existing confirmation can be activated without accessing executable or deleting files");
  }finally{window.Close();app.Shutdown();}
  return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
