using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WidgetLauncher;

internal static class LifecycleChecks
{
 static async Task<int> Main(string[] args)
 {
  WidgetManager? manager=null;Widget? widget=null;
  try{
   string root=Path.GetFullPath(args[0]);
   using(var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"launcher-install.json"))))
    if(!marker.RootElement.GetProperty("TestInstallation").GetBoolean())throw new Exception("Only a test installation may be used.");
   manager=new WidgetManager(root);widget=manager.ReadCatalog()[0];
   if(File.Exists(Path.Combine(manager.WidgetDirectory(widget),".widget-install.json")))await manager.RemoveAsync(widget);
   await manager.InstallAsync(widget);
   string data=manager.DataDirectory(widget);Directory.CreateDirectory(data);
   File.WriteAllText(Path.Combine(data,"settings.json"),"{\"X\":80,\"Y\":80,\"Pin\":false,\"Auto\":false,\"Source\":\"\"}");
   File.WriteAllText(Path.Combine(data,"appearance.json"),"{\"Theme\":\"Лаванда\",\"Accent\":\"#B9A1FF\"}");
   File.WriteAllText(Path.Combine(data,"hotkeys.json"),"{}");
   await manager.LaunchAsync(widget);
   for(int i=0;i<30&&!manager.IsRunning(widget);i++)await Task.Delay(100);
   if(!manager.IsRunning(widget))throw new Exception("Packaged player did not acquire its managed instance mutex.");
   Console.WriteLine("Private runtime -> installed player: PASS");
   await manager.LaunchAsync(widget);
   if(!EventWaitHandle.TryOpenExisting(manager.InstanceName(widget)+".Show",out var show))throw new Exception("Show event missing.");
   using(show)show.Set();
   Console.WriteLine("Second launch / show existing instance: PASS");
   await manager.StopAsync(widget);
   if(manager.IsRunning(widget))throw new Exception("Player failed to stop.");
   using(var settings=JsonDocument.Parse(File.ReadAllText(Path.Combine(data,"settings.json"))))
    if(!settings.RootElement.TryGetProperty("AudioTarget",out _))throw new Exception("Player did not save its live settings into the launcher Data directory.");
   Console.WriteLine("Graceful stop / managed settings save: PASS");
   await manager.RemoveAsync(widget);
   if(manager.IsInstalled(widget)||!File.Exists(Path.Combine(data,"settings.json")))throw new Exception("Widget removal did not preserve settings.");
   await manager.InstallAsync(widget);
   await manager.LaunchAsync(widget);await manager.StopAsync(widget);
   await manager.RemoveAsync(widget);
   Console.WriteLine("Remove -> reinstall -> launch -> stop -> remove: PASS");
   if(args.Length>1&&args[1]=="--keep-installed")await manager.InstallAsync(widget);
   return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  finally{if(manager!=null&&widget!=null){try{await manager.StopAsync(widget);}catch{}}}
 }
}
