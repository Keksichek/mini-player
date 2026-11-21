using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using WidgetLauncherSetup;
using File=WidgetLauncherSetup.HubFile;
using Directory=WidgetLauncherSetup.HubDirectory;

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
   InstallEngine.ConfigureRuntime();
   scratch=Path.GetFullPath(args[0]);Directory.CreateDirectory(scratch);
   var parent=NewRoot("Папка для приложений [hub]");
   string target=InstallEngine.ResolveDestination(parent);
   Check(target==Path.Combine(parent,"VibeWidgetsHub alpha"),"Selected parent gets the dedicated VibeWidgetsHub alpha folder");
   Check(!Directory.Exists(parent),"Resolving installation folder does not create files");
   Check(InstallEngine.ResolveDestination(target)==target,"Selecting hub folder itself does not nest another copy");
   Write(Path.Combine(parent,"keep.txt"),"keep");Install(target,"hub");
   Check(File.Exists(Path.Combine(target,"Launcher.exe")),"Launcher installs inside the automatically created hub folder");
   Check(InstallEngine.ResolveDestination(parent)==target&&InstallEngine.ResolveDestination(target)==target,"Repeated installation resolves to the existing hub folder");
   var workshop=Path.Combine(target,"Data","Customization","mini-player");
   string presetId=Guid.NewGuid().ToString("N");
   Write(Path.Combine(workshop,"active.json"),"{}");Write(Path.Combine(workshop,"draft.json"),"{}");
   Write(Path.Combine(workshop,"Presets",presetId+".json"),"{}");
   Write(Path.Combine(workshop,"Media",new string('a',64)+".png"),"test image");
   InstallEngine.Remove(target,true);Check(File.ReadAllText(Path.Combine(parent,"keep.txt"))=="keep","Removing hub preserves its selected parent and unrelated files");
   Check(!Directory.Exists(target),"Complete uninstall removes presets, active selection, draft and imported backgrounds");
   Install(InstallEngine.ResolveDestination(parent),"hub");InstallEngine.Remove(target,true);
   Write(Path.Combine(workshop,"Presets",presetId+".json"),"legacy preset");
   Install(InstallEngine.ResolveDestination(parent),"hub");
   Check(File.ReadAllText(Path.Combine(workshop,"Presets",presetId+".json"))=="legacy preset","Reinstall recovers the known preset remainder from an older uninstaller");
   string note=Path.Combine(workshop,"Presets","my-notes.json");Write(note,"personal");
   InstallEngine.Remove(target,true);Check(File.ReadAllText(note)=="personal","Unrelated files inside preset folders survive uninstall");
   string afterRollback=Path.Combine(NewRoot("Retained recovery files"),InstallEngine.ProductName);
   string recoveryFile=Path.Combine(afterRollback,".update-preserved-"+Guid.NewGuid().ToString("N"),"Data","personal-backup.txt");
   Write(recoveryFile,"retained data");Install(InstallEngine.ResolveDestination(afterRollback),"hub");InstallEngine.Remove(afterRollback,true);
   Check(File.ReadAllText(recoveryFile)=="retained data","A retained rollback backup permits reinstall and survives the next uninstall");
   Write(Path.Combine(afterRollback,"Data","Customization","mini-player","active.json"),"{}");
   Install(InstallEngine.ResolveDestination(afterRollback),"hub");InstallEngine.Remove(afterRollback,true);
   Check(File.ReadAllText(recoveryFile)=="retained data","Legacy workshop residues and a valid backup can coexist on reinstall");
   Write(Path.Combine(afterRollback,"personal.txt"),"keep");
   Reject("Known backup cannot authorize adoption of unrelated top-level files",()=>InstallEngine.ResolveDestination(afterRollback));
   Check(InstallEngine.ResolveDestination(Path.GetPathRoot(scratch))==Path.Combine(Path.GetPathRoot(scratch),InstallEngine.ProductName),"Selecting a drive root creates a dedicated subfolder");
   Reject("Drive-relative selection is refused",()=>InstallEngine.ResolveDestination("C:relative"));
   Reject("Relative parent selection is refused",()=>InstallEngine.ResolveDestination("relative"));
   Reject("Network parent selection is refused",()=>InstallEngine.ResolveDestination(@"\\server\folder"));
   string longParent=scratch;while(longParent.Length<240)longParent=Path.Combine(longParent,"Папка длинного пути");
   string longRoot=InstallEngine.ResolveDestination(longParent);Install(longRoot,"long");
   Check(File.Exists(Path.Combine(longRoot,"Runtime","dotnet.exe")),"Installer supports selected paths longer than 260 characters");
   InstallEngine.Remove(longRoot,true);Check(!Directory.Exists(longRoot),"Uninstaller supports long selected paths");
   var root=NewRoot("Папка лаунчера [тест] с пробелами");Install(root,"v1");
   Check(File.ReadAllText(Path.Combine(root,"Launcher.dll"))=="v1","Install copies launcher to chosen Unicode folder");
   var marker=InstallEngine.ReadMarker(root);Check(marker.TestInstallation&&marker.InstallPath==root,"Installation ownership marker matches exact root");
   Check(InstallEngine.ResolveDestination(root)==root,"Updating a previous custom installation keeps its existing location");
   Check(Directory.Exists(Path.Combine(root,"Widgets"))&&Directory.Exists(Path.Combine(root,"Data")),"Widgets and data folders live under launcher root");
   string settings=Path.Combine(root,"Data","mini-player","settings.json"),widget=Path.Combine(root,"Widgets","mini-player","App","MiniPlayer.dll");
   Write(settings,"settings");Write(widget,"widget");Write(Path.Combine(root,"personal.txt"),"personal");
   Install(root,"v2");
   Check(File.ReadAllText(Path.Combine(root,"Launcher.dll"))=="v2","Updating replaces launcher files");
   Check(File.ReadAllText(settings)=="settings"&&File.ReadAllText(widget)=="widget","Updating preserves installed widget and settings");
   using(var held=new FileStream(Path.Combine(root,"Launcher.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))Reject("Locked file aborts update",()=>Install(root,"v3"));
   Check(File.ReadAllText(Path.Combine(root,"Launcher.exe"))=="v2"&&File.ReadAllText(Path.Combine(root,"Launcher.dll"))=="v2","Failed update restores files already replaced");
   string interrupted=NewRoot("rollback-busy");Install(interrupted,"original");
   FileStream busyReplacement=null;
   try{
    using(var payload=Package("replacement")){
     Reject("Rollback reports a busy replacement",delegate{InstallEngine.Install(payload,interrupted,true,delegate(int n,string label){
      if(n<=72||n>=92||!File.Exists(Path.Combine(interrupted,"Launcher.dll"))||File.ReadAllText(Path.Combine(interrupted,"Launcher.dll"))!="replacement")return;
      busyReplacement=new FileStream(Path.Combine(interrupted,"Launcher.exe"),FileMode.Open,FileAccess.Read,FileShare.Read);
      throw new IOException("Injected installer failure after replacement");
     });});
    }
    Check(busyReplacement!=null,"Installer failure occurs with newly replaced launcher locked");
    var preserved=Directory.GetDirectories(interrupted).Single(dir=>Path.GetFileName(dir).StartsWith(".install-",StringComparison.Ordinal));
    Check(File.ReadAllText(Path.Combine(preserved,"old","Launcher.exe"))=="original","A failed rollback retains the original locked component backup");
    Check(File.ReadAllText(Path.Combine(interrupted,"Launcher.dll"))=="original","Rollback restores other components despite a locked counterpart");
   }finally{if(busyReplacement!=null)busyReplacement.Dispose();}
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
   Reject("Installer refuses junction parent selection",()=>InstallEngine.ResolveDestination(link));
   Check(File.ReadAllText(Path.Combine(scratch,"junction-target","keep.txt"))=="keep","Junction target remains intact");

   var markerPath=Path.Combine(root,InstallEngine.MarkerName);string original=File.ReadAllText(markerPath);var json=new JavaScriptSerializer();
   marker=InstallEngine.ReadMarker(root);marker.TestInstallation=false;File.WriteAllText(markerPath,json.Serialize(marker));
   Reject("Test removal cannot remove real installation",()=>InstallEngine.Remove(root,true));File.WriteAllText(markerPath,original);
   Check(File.Exists(Path.Combine(root,"Launcher.exe")),"Mode mismatch leaves installation intact");
   marker=InstallEngine.ReadMarker(root);marker.Files=marker.Files.Concat(new[]{"../outside.txt"}).ToArray();File.WriteAllText(markerPath,json.Serialize(marker));
   Reject("Removal refuses paths outside manifest root",()=>InstallEngine.Remove(root,true));File.WriteAllText(markerPath,original);
   Check(File.Exists(Path.Combine(root,"Launcher.exe"))&&File.Exists(settings),"Malformed removal manifest deletes nothing");
   Write(Path.Combine(root,"Widgets","unrelated","keep.txt"),"other");
   Write(Path.Combine(root,"Widgets","clock",".widget-install.json"),"{\"Id\":\"clock\",\"Executable\":\"App/Clock.exe\"}");
   Write(Path.Combine(root,"Data","clock","settings.json"),"{}");
   InstallEngine.Remove(root,true);
   Check(!Directory.Exists(Path.Combine(root,"Widgets","clock"))&&!Directory.Exists(Path.Combine(root,"Data","clock")),"Uninstall discovers an installed widget omitted by an older catalog update");
   Check(!File.Exists(Path.Combine(root,"Launcher.exe"))&&!File.Exists(markerPath),"Uninstall removes owned launcher files and marker");
   Check(!File.Exists(settings)&&!File.Exists(widget),"Uninstall removes catalog widget and its settings");
   Check(File.ReadAllText(Path.Combine(root,"personal.txt"))=="personal"&&File.ReadAllText(Path.Combine(root,"extra.txt"))=="user file","Uninstall preserves unrelated root files");
   Check(File.ReadAllText(Path.Combine(root,"Widgets","unrelated","keep.txt"))=="other","Uninstall preserves unrelated widget folder");
   Console.WriteLine("TOTAL "+checks+" PASS");return 0;
  }
  catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
