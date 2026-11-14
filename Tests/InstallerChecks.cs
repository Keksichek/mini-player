using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using WidgetLauncherSetup;

static class InstallerChecks
{
 static string scratch;
 static int checks;
 static readonly Action<int,string> Progress=(percent,message)=>{};
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static void Reject(string label,Action action){bool failed=false;try{action();}catch(Exception ex){if(!(ex is IOException||ex is InvalidDataException||ex is ArgumentException||ex is UnauthorizedAccessException||ex is InvalidOperationException))throw;failed=true;}Check(failed,label);}
 static MemoryStream Package(string version,params string[] extra)
 {
  var stream=new MemoryStream();
  using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
  {
   foreach(var name in new[]{"Launcher.exe","Launcher.dll","Launcher.runtimeconfig.json","Uninstall.exe","Runtime/dotnet.exe","Catalog/widgets.json","Packages/mini-player.zip"}.Concat(extra))
   {
    var entry=zip.CreateEntry(name);
    using(var writer=new StreamWriter(entry.Open()))writer.Write(name=="Catalog/widgets.json"?"[{\"id\":\"mini-player\"}]":version);
   }
  }
  stream.Position=0;return stream;
 }
 static void Install(string root,string version,params string[] extra){using(var package=Package(version,extra))InstallEngine.Install(package,root,true,Progress);}
 static string NewRoot(string name){return Path.Combine(scratch,name);}
 static void Write(string path,string text){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,text);}
 public static int Main(string[] args)
 {
  try
  {
   scratch=Path.GetFullPath(args[0]);Directory.CreateDirectory(scratch);
   var root=NewRoot("Папка лаунчера [тест] с пробелами");Install(root,"v1");
   Check(File.ReadAllText(Path.Combine(root,"Launcher.dll"))=="v1","Install copies launcher to chosen Unicode folder");
   var marker=InstallEngine.ReadMarker(root);Check(marker.TestInstallation&&marker.InstallPath==root,"Installation ownership marker matches exact root");
   Check(Directory.Exists(Path.Combine(root,"Widgets"))&&Directory.Exists(Path.Combine(root,"Data")),"Widgets and data folders live under launcher root");
   string settings=Path.Combine(root,"Data","mini-player","settings.json"),widget=Path.Combine(root,"Widgets","mini-player","App","MiniPlayer.dll");
   Write(settings,"settings");Write(widget,"widget");Write(Path.Combine(root,"personal.txt"),"personal");
   Install(root,"v2");
   Check(File.ReadAllText(Path.Combine(root,"Launcher.dll"))=="v2","Updating replaces launcher files");
   Check(File.ReadAllText(settings)=="settings"&&File.ReadAllText(widget)=="widget","Updating preserves installed widget and settings");
   using(var held=new FileStream(Path.Combine(root,"Launcher.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))Reject("Locked file aborts update",()=>Install(root,"v3"));
   Check(File.ReadAllText(Path.Combine(root,"Launcher.exe"))=="v2"&&File.ReadAllText(Path.Combine(root,"Launcher.dll"))=="v2","Failed update restores files already replaced");
   Check(InstallEngine.ReadMarker(root).InstallPath==root&&File.ReadAllText(settings)=="settings","Failed update preserves marker and settings");
   Write(Path.Combine(root,"extra.txt"),"user file");Reject("Update refuses collision with unowned file",()=>Install(root,"v3","extra.txt"));
   Check(File.ReadAllText(Path.Combine(root,"extra.txt"))=="user file","User file survives rejected update");

   var occupied=NewRoot("unowned");Write(Path.Combine(occupied,"personal.txt"),"keep");
   Reject("Existing unrelated folder is refused",()=>Install(occupied,"v1"));Check(File.ReadAllText(Path.Combine(occupied,"personal.txt"))=="keep","Unrelated folder remains intact");
   Reject("Drive root is refused",()=>InstallEngine.ValidateDestination(Path.GetPathRoot(root)));
   Reject("User home root is refused",()=>InstallEngine.ValidateDestination(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
   Reject("Relative installation folder is refused",()=>InstallEngine.ValidateDestination("relative"));

   foreach(var entry in new[]{"../outside.txt","C:/outside.txt","/outside.txt","Runtime/file:stream","Data/mini-player/settings.json","./Data/mini-player/settings.json","x/../Data/mini-player/settings.json","Runtime/../Launcher.dll","Runtime/folder./file","LAUNCHER.DLL"})
   {
    var path=NewRoot("invalid-"+Guid.NewGuid().ToString("N"));
    Reject("Reject package entry "+entry,()=>Install(path,"bad",entry));
    Check(!File.Exists(Path.Combine(path,InstallEngine.MarkerName)),"Malformed package cannot create installed marker");
   }
   var missing=NewRoot("incomplete");using(var data=new MemoryStream())
   {
    using(var zip=new ZipArchive(data,ZipArchiveMode.Create,true)){using(var writer=new StreamWriter(zip.CreateEntry("Launcher.exe").Open()))writer.Write("incomplete");}
    data.Position=0;Reject("Missing payload component prevents installation",()=>InstallEngine.Install(data,missing,true,Progress));
   }
   var link=Path.Combine(scratch,"junction-root");Check(Directory.Exists(link),"Junction test fixture exists");
   Reject("Installer refuses junction destination",()=>Install(link,"bad"));
   Check(File.ReadAllText(Path.Combine(scratch,"junction-target","keep.txt"))=="keep","Junction target remains intact");

   var markerPath=Path.Combine(root,InstallEngine.MarkerName);string original=File.ReadAllText(markerPath);var json=new JavaScriptSerializer();
   marker=InstallEngine.ReadMarker(root);marker.TestInstallation=false;File.WriteAllText(markerPath,json.Serialize(marker));
   Reject("Test removal cannot remove real installation",()=>InstallEngine.Remove(root,true));File.WriteAllText(markerPath,original);
   Check(File.Exists(Path.Combine(root,"Launcher.exe")),"Mode mismatch leaves installation intact");
   marker=InstallEngine.ReadMarker(root);marker.Files=marker.Files.Concat(new[]{"../outside.txt"}).ToArray();File.WriteAllText(markerPath,json.Serialize(marker));
   Reject("Removal refuses paths outside manifest root",()=>InstallEngine.Remove(root,true));File.WriteAllText(markerPath,original);
   Check(File.Exists(Path.Combine(root,"Launcher.exe"))&&File.Exists(settings),"Malformed removal manifest deletes nothing");
   Write(Path.Combine(root,"Widgets","unrelated","keep.txt"),"other");
   InstallEngine.Remove(root,true);
   Check(!File.Exists(Path.Combine(root,"Launcher.exe"))&&!File.Exists(markerPath),"Uninstall removes owned launcher files and marker");
   Check(!File.Exists(settings)&&!File.Exists(widget),"Uninstall removes catalog widget and its settings");
   Check(File.ReadAllText(Path.Combine(root,"personal.txt"))=="personal"&&File.ReadAllText(Path.Combine(root,"extra.txt"))=="user file","Uninstall preserves unrelated root files");
   Check(File.ReadAllText(Path.Combine(root,"Widgets","unrelated","keep.txt"))=="other","Uninstall preserves unrelated widget folder");
   Console.WriteLine("TOTAL "+checks+" PASS");return 0;
  }
  catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
