using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using WidgetLauncherSetup;
using File=WidgetLauncherSetup.HubFile;
using Directory=WidgetLauncherSetup.HubDirectory;

internal static class RelocationChecks
{
 static int count;static string root;
 static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static void Reject(Action action,string name){try{action();}catch(IOException){Check(true,name);return;}catch(UnauthorizedAccessException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
 static string Child(string parent,string relative){return InstallEngine.Child(parent,relative);}
 static void Put(string parent,string relative,string value){string path=Child(parent,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,value,new UTF8Encoding(false));}
 static string Fixture(string name){
  string folder=Child(root,name);Directory.CreateDirectory(folder);var files=new[]{"Launcher.exe","Launcher.dll","Uninstall.exe","Relocate.exe","Runtime\\dotnet.exe","Catalog\\widgets.json"};
  foreach(string file in files)Put(folder,file,file=="Catalog\\widgets.json"?"[]":"fixture "+file);
  Put(folder,"Widgets\\mini-player\\App\\MiniPlayer.dll","widget binary");Put(folder,"Widgets\\mini-player\\.widget-install.json","{\"Id\":\"mini-player\",\"Executable\":\"App/MiniPlayer.exe\"}");
  Put(folder,"Data\\mini-player\\settings.json","{\"Auto\":false,\"MovementLocked\":true}");Put(folder,"Data\\mini-player\\hotkeys.json","{\"4\":{\"Key\":1}}");
  Put(folder,"Data\\hub-settings.json","{\"AutoStartMode\":4,\"SelectedWidgets\":[\"mini-player\"],\"LightMode\":true,\"AllowOffscreen\":false}");Put(folder,"Мои заметки.txt","Личный файл в папке хаба");Directory.CreateDirectory(Child(folder,"Пустая папка"));
  var marker=new InstallMarker{ProductId=InstallEngine.ProductId,InstallPath=folder,InstallId=InstallEngine.Hash(folder),Files=files,WidgetIds=new[]{"mini-player"},TestInstallation=true,Version="test"};File.WriteAllText(Child(folder,InstallEngine.MarkerName),new JavaScriptSerializer().Serialize(marker));return folder;
 }
 static RelocationResult Move(string source,string parent,Action<int,string> progress){return RelocationEngine.Relocate(source,parent,true,progress);}
 static void Ignore(int n,string s){}
 static int Main(string[] args){
  if(args.Length==3&&args[1]=="--resume-widgets"&&Path.GetFileName(args[0])=="Launcher.dll"&&args[2]=="mini-player,clock"){
   File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[0]),"resume-observed.txt"),args[0]+"\n"+args[2]);return 0;
  }
  try{InstallEngine.ConfigureRuntime();root=InstallEngine.Normalize(args[0]);Directory.CreateDirectory(root);
   Check(RelocateProgram.QuoteArgument(@"D:\")=="\"D:\\\\\"","Handoff quoting preserves a selected drive root");
   string first=Fixture("Начальная папка [проверка]");string destination=Child(root,"Другая папка [кириллица]");var moved=Move(first,destination,Ignore);string target=Child(destination,InstallEngine.ProductName);
   Check(moved.Destination==target,"Selected parent receives one named Hub folder");Check(!Directory.Exists(first),"Verified source files and empty directories removed");
   Check(InstallEngine.ReadMarker(target).InstallPath==target&&InstallEngine.ReadMarker(target).InstallId==InstallEngine.Hash(target),"Install marker points to relocated root");
   Check(File.ReadAllText(Child(target,"Widgets\\mini-player\\App\\MiniPlayer.dll"))=="widget binary","Installed widget files move with launcher");
   Check(File.ReadAllText(Child(target,"Data\\mini-player\\settings.json")).Contains("MovementLocked"),"Widget settings retained");
   Check(File.ReadAllText(Child(target,"Data\\mini-player\\hotkeys.json")).Contains("Key"),"Hotkey data retained");Check(File.ReadAllText(Child(target,"Data\\hub-settings.json")).Contains("LightMode"),"Hub theme, startup and boundaries settings retained");
   Check(File.ReadAllText(Child(target,"Мои заметки.txt"))=="Личный файл в папке хаба","Personal files copied without loss");Check(Directory.Exists(Child(target,"Пустая папка")),"Empty folders preserved");Check(moved.Warning=="","Clean move reports no leftovers");
   string explicitSource=Fixture("Explicit");string explicitTarget=Child(Child(root,"Explicit parent"),InstallEngine.ProductName);Directory.CreateDirectory(explicitTarget);Move(explicitSource,explicitTarget,Ignore);Check(!Directory.Exists(Child(explicitTarget,InstallEngine.ProductName)),"Selecting final empty Hub folder avoids nested copy");
   string same=Fixture("Same");Reject(delegate{Move(same,same,Ignore);},"Nested destination rejected");Check(File.Exists(Child(same,"Launcher.dll")),"Rejected nested move keeps source intact");
   string sameNamed=Fixture("Same named parent\\"+InstallEngine.ProductName);Reject(delegate{Move(sameNamed,Path.GetDirectoryName(sameNamed),Ignore);},"Same destination rejected");
   string occupied=Child(Child(root,"Occupied"),InstallEngine.ProductName);Put(occupied,"user.txt","Do not touch");string occupiedSource=Fixture("Occupied source");Reject(delegate{Move(occupiedSource,Path.GetDirectoryName(occupied),Ignore);},"Occupied destination rejected");Check(File.ReadAllText(Child(occupied,"user.txt"))=="Do not touch"&&File.Exists(Child(occupiedSource,"Launcher.dll")),"Occupied destination and source preserved");
   string changing=Fixture("Changing");string changingParent=Child(root,"Changing target");Reject(delegate{Move(changing,changingParent,delegate(int n,string s){if(n==74)Put(changing,"Data\\mini-player\\settings.json","newer data");});},"Source writes during copy abort safely");Check(File.ReadAllText(Child(changing,"Data\\mini-player\\settings.json"))=="newer data"&&!Directory.Exists(Child(changingParent,InstallEngine.ProductName)),"Latest source data survives interrupted relocation");
   string growing=Fixture("Growing");Reject(delegate{Move(growing,Child(root,"Growing target"),delegate(int n,string s){if(n==74)Put(growing,"new-file.txt","created while moving");});},"New source files during copy abort safely");Check(File.Exists(Child(growing,"new-file.txt")),"Concurrent new file retained");
   string resumeRoot=null;int restartCount=0;
   Action<string> resume=delegate(string directory){resumeRoot=directory;restartCount++;};
   Reject(delegate{RelocationEngine.Relocate(growing,Child(root,"Resume target"),true,delegate(int n,string s){if(n==74)throw new IOException("Copy interrupted");},resume);},"Interrupted copy reports failure");
   Check(restartCount==1&&resumeRoot==growing,"Failure after shutdown restarts the original installation once");
   Reject(delegate{RelocationEngine.Relocate(growing,growing,true,Ignore,resume);},"Invalid destination is rejected before shutdown");
   Check(restartCount==1,"Failure before shutdown does not start an extra launcher");
   string launchProbe=Fixture("Проверка запуска"),observed=Child(launchProbe,"resume-observed.txt");
   File.Copy(typeof(RelocationChecks).Assembly.Location,Child(launchProbe,@"Runtime\dotnet.exe"),true);
   RelocateProgram.ResumeLauncher(launchProbe,new[]{"mini-player","clock"});
   for(int i=0;i<100&&!File.Exists(observed);i++)System.Threading.Thread.Sleep(50);
   Check(File.Exists(observed)&&File.ReadAllText(observed)==Child(launchProbe,"Launcher.dll")+"\nmini-player,clock","Restart passes the canonical launcher path and previously running widget IDs to the installed runtime");
   string rollback=Fixture("Commit rollback"),rollbackParent=Child(root,"Commit target");Reject(delegate{Move(rollback,rollbackParent,delegate(int n,string s){if(n==85){Put(Child(rollbackParent,InstallEngine.ProductName),"external-new.txt","created after commit");throw new IOException("Injected registration failure");}});},"Post-commit registration failure rolls back copied files");Check(File.Exists(Child(rollback,"Launcher.dll"))&&File.ReadAllText(Child(Child(rollbackParent,InstallEngine.ProductName),"external-new.txt"))=="created after commit","Rollback preserves source and concurrent destination files");
   string readOnly=Fixture("Readonly");File.SetAttributes(Child(readOnly,"Мои заметки.txt"),FileAttributes.ReadOnly);var readonlyResult=Move(readOnly,Child(root,"Readonly target"),Ignore);Check(!Directory.Exists(readOnly)&&(File.GetAttributes(Child(readonlyResult.Destination,"Мои заметки.txt"))&FileAttributes.ReadOnly)!=0,"Read-only files move while their destination attributes are retained");
   string locked=Fixture("Locked");using(var stream=new FileStream(HubPath.Disk(Child(locked,"Launcher.dll")),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){Reject(delegate{Move(locked,Child(root,"Locked target"),Ignore);},"Locked source aborts without removal");}Check(File.Exists(Child(locked,InstallEngine.MarkerName)),"Failed move keeps original marker");
   string late=Fixture("Late change");var lateResult=Move(late,Child(root,"Late target"),delegate(int n,string s){if(n==92)Put(late,"Мои заметки.txt","changed after commit");});Check(lateResult.Warning.Length>0&&File.ReadAllText(Child(late,"Мои заметки.txt"))=="changed after commit","Post-commit changed files remain in old folder with a warning");
   Check(File.ReadAllText(Child(lateResult.Destination,"Мои заметки.txt"))=="Личный файл в папке хаба","Verified destination copy remains complete");
   string longSource=Fixture("Long source");string longParent=Child(root,String.Join("\\",Enumerable.Repeat(new string('д',55),5)));var longResult=Move(longSource,longParent,Ignore);Check(longResult.Destination.Length>260&&File.Exists(Child(longResult.Destination,"Data\\mini-player\\settings.json")),"Long destination paths and Unicode filenames work");
   string fake=Fixture("Wrong type");Reject(delegate{RelocationEngine.Destination(fake,Child(root,"Wrong target"),false);},"Real mode refuses test installation");
   string remove=Fixture("Remove settings");Put(remove,"Data\\autostart-error.txt","owned startup log");InstallEngine.Remove(remove,true);Check(!File.Exists(Child(remove,"Data\\hub-settings.json")),"Uninstall removes owned hub preferences");Check(!File.Exists(Child(remove,"Data\\autostart-error.txt")),"Uninstall removes owned startup error log");Check(File.Exists(Child(remove,"Мои заметки.txt")),"Uninstall still preserves unrelated files");
   Check(!Directory.GetDirectories(changingParent).Concat(Directory.GetDirectories(Child(root,"Growing target"))).Concat(Directory.GetDirectories(rollbackParent)).Any(x=>Path.GetFileName(x).StartsWith(".vibe-relocate-",StringComparison.Ordinal)),"Failed copies leave no staging folders behind");
   Console.WriteLine("TOTAL "+count+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
