using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using WidgetLauncher;
class NativeNotesChecks
{
 static async Task<int> Main(string[] args){
  string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
  var manager=new WidgetManager(root);
  var widget=new Widget{Id="notes",Name="Заметки",Version="0.1.5",Package="notes.zip",Executable="Notes.exe",Available=true,LaunchKind="exe",SupportsSettings=false};
  Directory.CreateDirectory(Path.Combine(root,"Packages"));Directory.CreateDirectory(Path.Combine(root,"Catalog"));
  File.Copy(args[1],Path.Combine(root,"Packages","notes.zip"),true);
  File.WriteAllText(Path.Combine(root,"Catalog","widgets.json"),JsonSerializer.Serialize(new[]{widget}));
  File.WriteAllText(Path.Combine(root,"launcher-install.json"),"{\"ProductId\":\"MiniPlayer.WidgetLauncher\"}");
  try{
   if(manager.ReadCatalog()[0].LaunchKind!="exe")throw new Exception("Catalog type lost");
   await manager.InstallAsync(widget);
   Directory.CreateDirectory(manager.DataDirectory(widget));
   string data=Path.Combine(manager.DataDirectory(widget),"notes.json");
   string original="[{\"id\":\"fixture\",\"text\":\"Проверка сохранности\",\"done\":false}]";File.WriteAllText(data,original);
   await manager.LaunchAsync(widget);
   if(!manager.IsRunning(widget))throw new Exception("Notes did not acquire lifecycle mutex");
   Console.WriteLine("PASS native Notes.exe starts and signals Ready without dotnet runtime in hub");
   await manager.LaunchAsync(widget);Console.WriteLine("PASS existing Notes instance accepts Show");
   await manager.StopAsync(widget);
   if(File.Exists(Path.Combine(manager.WidgetDirectory(widget),"data","notes.json")))throw new Exception("Data written inside payload");
   if(File.ReadAllText(data)!=original)throw new Exception("Saved notes changed");
   await manager.RemoveAsync(widget);
   if(!File.Exists(data))throw new Exception("Uninstall lost notes");
   await manager.InstallAsync(widget);await manager.LaunchAsync(widget);await manager.StopAsync(widget);
   if(File.ReadAllText(data)!=original)throw new Exception("Reinstall changed notes");
   Console.WriteLine("PASS Stop, removal, reinstall and relaunch preserve saved notes");
   await manager.RemoveAsync(widget);
   try{WidgetManager.ValidateLaunchKind(widget with{LaunchKind="unknown"});throw new Exception("Unknown launch kind accepted");}catch(InvalidDataException){}
   Console.WriteLine("PASS unsupported launch kind rejected");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  finally{if(manager.IsRunning(widget))await manager.StopAsync(widget);}
 }
}
