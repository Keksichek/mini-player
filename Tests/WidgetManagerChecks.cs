using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WidgetLauncher;

static class WidgetManagerChecks
{
 static string scratch="";
 static int checks;
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static Widget Fixture(string package="mini-player.zip")=>new(){Id="mini-player",Name="Mini Player",Description="Test",Version="1.0",Package=package,Executable="App/MiniPlayer.exe",Available=true};
 static string Root(string name){var path=Path.Combine(scratch,name);Directory.CreateDirectory(Path.Combine(path,"Packages"));return path;}
 static void Package(string root,params (string Name,string Text,int Attributes)[] entries)
 {
  using var file=File.Create(Path.Combine(root,"Packages","mini-player.zip"));using var zip=new ZipArchive(file,ZipArchiveMode.Create);
  foreach(var entry in entries){var item=zip.CreateEntry(entry.Name);item.ExternalAttributes=entry.Attributes;using var text=new StreamWriter(item.Open());text.Write(entry.Text);}
 }
 static async Task Reject(string label,Func<Task> action)
 {
  bool rejected=false;try{await action();}catch(Exception ex) when(ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException){rejected=true;}
  Check(rejected,label);
 }
 static void RejectNow(string label,Action action)
 {
  bool rejected=false;try{action();}catch(Exception ex) when(ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException){rejected=true;}
  Check(rejected,label);
 }
 public static async Task<int> Main(string[] args)
 {
  try
  {
   scratch=Path.GetFullPath(args[0]);Directory.CreateDirectory(scratch);var widget=Fixture();
   var root=Root("Выбранная папка [тест] с пробелами");var manager=new WidgetManager(root);
   Package(root,("App/MiniPlayer.exe","exe",0),("App/MiniPlayer.dll","dll",0),("App/Fonts/Onest.ttf","font",0));
   await manager.InstallAsync(widget);
   Check(manager.IsInstalled(widget),"Install succeeds in selected Unicode directory");
   Check(File.ReadAllText(Path.Combine(root,"Widgets","mini-player","App","MiniPlayer.dll"))=="dll","Payload resides under launcher Widgets directory");
   Check(Directory.GetDirectories(Path.Combine(root,"Widgets"),".install-*").Length==0,"Successful install leaves no staging directory");
   await Reject("Existing widget is not overwritten",()=>manager.InstallAsync(widget));
   Directory.CreateDirectory(manager.DataDirectory(widget));File.WriteAllText(Path.Combine(manager.DataDirectory(widget),"settings.json"),"preserve");
   await manager.RemoveAsync(widget);
   Check(!Directory.Exists(manager.WidgetDirectory(widget)),"Remove deletes installed widget");
   Check(File.ReadAllText(Path.Combine(manager.DataDirectory(widget),"settings.json"))=="preserve","Remove preserves user settings");
   await manager.InstallAsync(widget);Check(manager.IsInstalled(widget),"Reinstall works after removal");
   string installedMarker=Path.Combine(manager.WidgetDirectory(widget),".widget-install.json"),savedMarker=File.ReadAllText(installedMarker);
   File.WriteAllText(installedMarker,JsonSerializer.Serialize(widget with {Id="other-widget"}));
   Check(!manager.IsInstalled(widget)&&!manager.HasInstallation(widget),"Foreign install marker is not treated as this widget");
   await Reject("Removal rejects mismatched install marker",()=>manager.RemoveAsync(widget));
   File.WriteAllText(installedMarker,savedMarker);
   using(var locked=new FileStream(Path.Combine(manager.WidgetDirectory(widget),"App","Fonts","Onest.ttf"),FileMode.Open,FileAccess.Read,FileShare.Read))
    await Reject("Removal reports locked payload file",()=>manager.RemoveAsync(widget));
   Check(manager.HasInstallation(widget)&&File.Exists(installedMarker),"Interrupted removal preserves ownership for retry");
   await manager.RemoveAsync(widget);Check(!Directory.Exists(manager.WidgetDirectory(widget)),"Retry completes interrupted removal");
   await manager.InstallAsync(widget);File.Delete(manager.Executable(widget));
   Check(!manager.IsInstalled(widget)&&manager.HasInstallation(widget),"Missing executable remains recognized for repair");
   await manager.RemoveAsync(widget);await manager.InstallAsync(widget);
   Check(manager.IsInstalled(widget),"Damaged widget can be removed and reinstalled");

   foreach(var entry in new[]{"../escape.txt","App/../../escape.txt","/absolute.txt","C:/escape.txt","App/file:stream","App/folder./file","App/../file"})
   {
    var badRoot=Root("bad-"+Guid.NewGuid().ToString("N"));var badManager=new WidgetManager(badRoot);
    Package(badRoot,("App/MiniPlayer.exe","exe",0),(entry,"unsafe",0));
    await Reject("Reject ZIP path "+entry,()=>badManager.InstallAsync(widget));
    Check(!Directory.Exists(badManager.WidgetDirectory(widget)),"Invalid ZIP never becomes installed");
    Check(!Directory.Exists(Path.Combine(badRoot,"Widgets"))||Directory.GetFileSystemEntries(Path.Combine(badRoot,"Widgets")).Length==0,"Invalid ZIP rolls back staging");
   }
   var duplicate=Root("duplicates");Package(duplicate,("App/MiniPlayer.exe","exe",0),("app/MINIPLAYER.EXE","duplicate",0));
   await Reject("Reject case-insensitive duplicate ZIP entries",()=>new WidgetManager(duplicate).InstallAsync(widget));
   var symlink=Root("zip-symlink");Package(symlink,("App/MiniPlayer.exe","exe",0),("App/link","../../outside",0xA1FF<<16));
   await Reject("Reject Unix symlink in ZIP",()=>new WidgetManager(symlink).InstallAsync(widget));
   var missing=Root("missing-executable");Package(missing,("App/readme.txt","text",0));
   await Reject("Missing executable does not install",()=>new WidgetManager(missing).InstallAsync(widget));
   await Reject("Upcoming widget cannot install",()=>manager.InstallAsync(widget with {Id="upcoming",Available=false}));
   var outside=Root("outside");File.WriteAllText(Path.Combine(outside,"keep.txt"),"keep");
   RejectNow("Delete rejects sibling of ownership root",()=>WidgetManager.DeleteOwnedTree(outside,Path.Combine(root,"Widgets")));
   Check(File.ReadAllText(Path.Combine(outside,"keep.txt"))=="keep","Outside sentinel survives rejected deletion");
   var unowned=Path.Combine(root,"Widgets","unowned");Directory.CreateDirectory(unowned);File.WriteAllText(Path.Combine(unowned,"keep.txt"),"keep");
   await Reject("Remove refuses folder without install marker",()=>manager.RemoveAsync(widget with {Id="unowned"}));
   Check(File.Exists(Path.Combine(unowned,"keep.txt")),"Unowned folder remains intact");
   Check(WidgetManager.InstanceNameForData(Path.Combine(root,"Data","mini-player"))==WidgetManager.InstanceNameForData(Path.Combine(root,"Data","mini-player").ToUpperInvariant()+Path.DirectorySeparatorChar),"Instance key ignores case and trailing slash");
   Directory.CreateDirectory(Path.Combine(root,"Catalog"));File.WriteAllText(Path.Combine(root,"Catalog","widgets.json"),JsonSerializer.Serialize(new[]{widget,widget}));
   RejectNow("Catalog rejects duplicate widget IDs",()=>manager.ReadCatalog());
   var link=Path.Combine(scratch,"junction-root");
   if(Directory.Exists(link))
   {
    RejectNow("Filesystem junction rejected",()=>WidgetManager.RejectLinks(link));
    await Reject("Install cannot traverse junction root",()=>new WidgetManager(link).InstallAsync(widget));
    Check(File.ReadAllText(Path.Combine(scratch,"junction-target","keep.txt"))=="keep","Junction target remains untouched");
   }
   else throw new Exception("Junction fixture is missing");
   Console.WriteLine("TOTAL "+checks+" PASS");return 0;
  }
  catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
