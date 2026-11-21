using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using WidgetLauncherSetup;
using VibeWidgetsUpdates;

static class UpdateEngineChecks
{
 static string scratch,key;static int checks;
 static JavaScriptSerializer Json=new JavaScriptSerializer();
 static string P(string root,string relative){return InstallEngine.Child(root,relative);}
 static void Put(string root,string relative,string text){string path=P(root,relative);System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));System.IO.File.WriteAllText(path,text,new UTF8Encoding(false));}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Reject(Action action,string name){try{action();}catch(Exception){Check(true,name);return;}throw new Exception("Expected failure: "+name);}
 static string Hash(string file){using(var stream=System.IO.File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
 static string Catalog(){return Json.Serialize(new[]{new{Id="mini-player",Name="Mini Player",Executable="App/MiniPlayer.exe",Package="mini-player.zip",Available=true,Version="0.1"}});}
 static void Payload(string folder,string version){
  foreach(string name in new[]{"Launcher.exe","Launcher.runtimeconfig.json","Update.exe","Uninstall.exe","Runtime/dotnet.exe"})Put(folder,name,version+":"+name);
  foreach(string name in new[]{"Launcher.exe","Update.exe"})System.IO.File.Copy(version=="old"?typeof(ZipArchive).Assembly.Location:System.Reflection.Assembly.GetExecutingAssembly().Location,P(folder,name),true);
  System.IO.File.Copy(version=="old"?typeof(ZipArchive).Assembly.Location:System.Reflection.Assembly.GetExecutingAssembly().Location,P(folder,"Launcher.dll"));
  Put(folder,"Catalog/widgets.json",Catalog());string widget=P(folder,"_widget");Put(widget,"App/MiniPlayer.exe",version+":playerexe");System.IO.File.Copy(version=="old"?typeof(ZipArchive).Assembly.Location:System.Reflection.Assembly.GetExecutingAssembly().Location,P(widget,"App/MiniPlayer.dll"));
  System.IO.Directory.CreateDirectory(P(folder,"Packages"));ZipFile.CreateFromDirectory(widget,P(folder,"Packages/mini-player.zip"));System.IO.Directory.Delete(widget,true);
 }
 static string Fixture(string name){
  string folder=P(scratch,name);System.IO.Directory.CreateDirectory(folder);Payload(folder,"old");
  var files=System.IO.Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Select(x=>x.Substring(folder.Length+1)).ToArray();
  ZipFile.ExtractToDirectory(P(folder,"Packages/mini-player.zip"),P(folder,"Widgets/mini-player"));
  Put(folder,"Widgets/mini-player/.widget-install.json",Json.Serialize(new{Id="mini-player",Name="Mini Player",Executable="App/MiniPlayer.exe",Package="mini-player.zip",Available=true,Version="0.1"}));
  Put(folder,"Widgets/mini-player/Мои заметки.txt","Keep this widget note");Put(folder,"Мои заметки.txt","Keep this root note");Put(folder,"Data/mini-player/settings.json","{\"Pin\":false,\"Auto\":false}");Put(folder,"Data/hub-settings.json","{\"LightMode\":true,\"CloseMode\":1}");
  Put(folder,InstallEngine.MarkerName,Json.Serialize(new InstallMarker{ProductId=InstallEngine.ProductId,InstallPath=folder,InstallId=InstallEngine.Hash(folder),Files=files,WidgetIds=new[]{"mini-player"},TestInstallation=true,Version="old"}));return folder;
 }
 static string Feed(string name,long build,Action<string> alter){
  string feed=P(scratch,name),payload=P(feed,"_payload");System.IO.Directory.CreateDirectory(feed);Payload(payload,"new");if(alter!=null)alter(payload);
  string package=P(feed,"payload.zip");ZipFile.CreateFromDirectory(payload,package);
  var manifest=new UpdateManifest{ProductId=UpdateProtocol.ProductId,Channel=UpdateProtocol.Channel,BuildNumber=build,Version="new",Package="payload.zip",Sha256=Hash(package),Size=new FileInfo(package).Length,Notes="Local fixture"};
  byte[] bytes=new UTF8Encoding(false).GetBytes(Json.Serialize(manifest));System.IO.File.WriteAllBytes(P(feed,"update.json"),bytes);
  using(var rsa=RSA.Create()){rsa.FromXmlString(System.IO.File.ReadAllText(key));System.IO.File.WriteAllText(P(feed,"update.sig"),Convert.ToBase64String(rsa.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)));}return feed;
 }
 static UpdateResult Apply(string root,string feed,Func<string,string[],bool> health,Action<int,string> progress){return UpdateEngine.Apply(root,feed,true,new[]{"mini-player"},health,progress??delegate{});}
 static bool Healthy(string root,string[] ids){return true;}
 static string[] Data(string root){return System.IO.Directory.GetFiles(P(root,"Data"),"*",SearchOption.AllDirectories).OrderBy(x=>x).Select(x=>x.Substring(root.Length+1)+":"+Hash(x)).ToArray();}
 static int Main(string[] args){
  InstallEngine.ConfigureRuntime();
  if(args.Length==3&&args[0]=="--crash-child"){Apply(args[1],args[2],Healthy,delegate(int percent,string text){if(percent==50)Environment.Exit(77);});return 1;}
  try{
   scratch=Path.GetFullPath(args[0]);key=Path.GetFullPath(args[1]);System.IO.Directory.CreateDirectory(scratch);
   string feed=Feed("Signed feed",10,null),root=Fixture("Successful Unicode хаб"),oldData=String.Join("\n",Data(root));
   var result=Apply(root,feed,Healthy,null);
   Check(result.Updated&&!result.RolledBack&&UpdateEngine.CurrentBuild(root)==10,"Healthy update advances the independent build number");
   Check(Hash(P(root,"Launcher.dll"))==Hash(System.Reflection.Assembly.GetExecutingAssembly().Location),"Launcher files are replaced");
   Check(Hash(P(root,"Widgets/mini-player/App/MiniPlayer.dll"))==Hash(System.Reflection.Assembly.GetExecutingAssembly().Location),"Already installed widgets are updated without reinstalling manually");
   Check(String.Join("\n",Data(root))==oldData,"Healthy update preserves every configuration byte");
   Check(System.IO.File.ReadAllText(P(root,"Мои заметки.txt"))=="Keep this root note"&&System.IO.File.ReadAllText(P(root,"Widgets/mini-player/Мои заметки.txt"))=="Keep this widget note","Root and widget user files are preserved");
   Check(!UpdateEngine.Pending(root)&&!System.IO.Directory.Exists(P(root,UpdateEngine.TransactionName)),"Successful health confirmation cleans the transaction");
   Reject(delegate{Apply(root,feed,Healthy,null);},"Same or lower build cannot be installed");
   string expanded=Feed("Expanded catalog",11,delegate(string payload){
    string catalog=Catalog();
    Put(payload,"Catalog/widgets.json",catalog.Substring(0,catalog.Length-1)+",{\"Id\":\"clock\",\"Name\":\"Clock\",\"Executable\":\"App/Clock.exe\",\"Package\":\"clock.zip\",\"Available\":true,\"Version\":\"0.1\"}]");
    using(var archive=ZipFile.Open(P(payload,"Packages/clock.zip"),ZipArchiveMode.Create))
    using(var image=archive.CreateEntry("App/Clock.exe").Open())image.WriteByte(42);
   });
   string expandedRoot=Fixture("New catalog widget");Apply(expandedRoot,expanded,Healthy,null);
   Check(InstallEngine.ReadMarker(expandedRoot).WidgetIds.Contains("clock"),"Update records a new catalog widget before its first installation");
   ZipFile.ExtractToDirectory(P(expandedRoot,"Packages/clock.zip"),P(expandedRoot,"Widgets/clock"));
   Put(expandedRoot,"Widgets/clock/.widget-install.json","{\"Id\":\"clock\",\"Executable\":\"App/Clock.exe\"}");
   Put(expandedRoot,"Data/clock/settings.json","{}");InstallEngine.Remove(expandedRoot,true);
   Check(!System.IO.Directory.Exists(P(expandedRoot,"Widgets/clock"))&&!System.IO.Directory.Exists(P(expandedRoot,"Data/clock")),"Complete uninstall also removes a widget installed after a catalog update");
   string bad=Fixture("Failed health"),baseline=String.Join("\n",Data(bad));var failed=Apply(bad,feed,delegate(string r,string[] ids){Put(r,"Data/mini-player/settings.json","candidate schema");Put(r,"Data/new-setting.txt","new data");return false;},null);
   Check(failed.RolledBack&&!failed.Updated&&UpdateEngine.CurrentBuild(bad)==0,"Unhealthy update rolls back its version state");
   Check(Hash(P(bad,"Launcher.dll"))==Hash(typeof(ZipArchive).Assembly.Location)&&Hash(P(bad,"Widgets/mini-player/App/MiniPlayer.dll"))==Hash(typeof(ZipArchive).Assembly.Location),"Failed health restores launcher and widget binaries");
   Check(String.Join("\n",Data(bad))==baseline,"Failed health restores the exact pre-update settings");
   var preserved=System.IO.Directory.GetDirectories(bad,".update-preserved-*");Check(preserved.Any(folder=>System.IO.File.Exists(P(folder,"Data/new-setting.txt"))),"Candidate changes to settings are retained separately instead of destroyed");
   string partial=Fixture("Partial replacement"),partialHash=Hash(P(partial,"Launcher.dll"));var part=Apply(partial,feed,Healthy,delegate(int n,string text){if(n==50)throw new IOException("Injected partial replacement failure");});
   Check(part.RolledBack&&Hash(P(partial,"Launcher.dll"))==partialHash&&!UpdateEngine.Pending(partial),"Failure after partial replacement rolls back completely");
   string cleanup=Fixture("Cleanup failure");var cleaned=Apply(cleanup,feed,Healthy,delegate(int n,string text){if(n==96)throw new IOException("Injected cleanup failure");});
   Check(cleaned.Updated&&!cleaned.RolledBack&&UpdateEngine.Pending(cleanup)&&Hash(P(cleanup,"Launcher.dll"))==Hash(System.Reflection.Assembly.GetExecutingAssembly().Location),"Cleanup failure after commit never restores the old version");
   var recovered=UpdateEngine.Recover(cleanup);Check(recovered.Updated&&!UpdateEngine.Pending(cleanup)&&recovered.ResumeIds.SequenceEqual(new[]{"mini-player"}),"Committed recovery retries cleanup and retains resume IDs");
   string extra=Fixture("Concurrent staging file");Apply(extra,feed,Healthy,delegate(int n,string text){if(n==96)Put(extra,UpdateEngine.TransactionName+"/unrelated.txt","keep unexpected staging file");});
   Check(System.IO.Directory.GetDirectories(extra,".update-preserved-*").Any(folder=>System.IO.File.Exists(P(folder,"unrelated.txt"))),"Cleanup preserves unexpected files placed in the transaction");
   string conflict=Fixture("Foreign file conflict"),foreign=Feed("Foreign collision feed",11,delegate(string folder){Put(folder,"Мои заметки.txt","overwrite attempt");});
   Reject(delegate{Apply(conflict,foreign,Healthy,null);},"Update refuses to replace an unowned root file");Check(System.IO.File.ReadAllText(P(conflict,"Мои заметки.txt"))=="Keep this root note","Rejected update leaves unowned files intact");
   string corrupt=Fixture("Corrupt state");Put(corrupt,UpdateEngine.StateName,"{\"BuildNumber\":\"wrong\"}");Reject(delegate{Apply(corrupt,feed,Healthy,null);},"Corrupt version state cannot bypass downgrade protection");
   string schema=Fixture("Data overwrite"),schemaFeed=Feed("Data overwrite feed",12,delegate(string folder){Put(folder,"Data/mini-player/settings.json","bad");});
   Reject(delegate{Apply(schema,schemaFeed,Healthy,null);},"Signed package is still forbidden from replacing Data");Check(System.IO.File.ReadAllText(P(schema,"Data/mini-player/settings.json"))=="{\"Pin\":false,\"Auto\":false}","Rejected Data package preserves live settings");
   string tamper=Fixture("Tampered package"),tamperedFeed=Feed("Tampered feed",13,null);System.IO.File.AppendAllText(P(tamperedFeed,"payload.zip"),"changed");Reject(delegate{Apply(tamper,tamperedFeed,Healthy,null);},"Changed signed payload is rejected before modifying the installation");
   string crash=Fixture("Interrupted replacement");using(var child=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--crash-child "+UpdateEngine.Quote(crash)+" "+UpdateEngine.Quote(feed)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){if(!child.WaitForExit(15000))throw new Exception("Crash fixture timed out");Check(child.ExitCode==77&&UpdateEngine.Pending(crash),"Interruption leaves a durable recovery journal");}
   // Simulate losing the main marker in the narrow marker replacement window.
   System.IO.File.Delete(P(crash,InstallEngine.MarkerName));var restored=UpdateEngine.Recover(crash);
   Check(restored.RolledBack&&Hash(P(crash,"Launcher.dll"))==Hash(typeof(ZipArchive).Assembly.Location)&&!UpdateEngine.Pending(crash),"Interrupted update recovers even with the root marker temporarily missing");
   Check(InstallEngine.ReadMarker(crash).TestInstallation,"Recovery retains the original installation identity");
   string wrong=Fixture("Wrong mode");Reject(delegate{UpdateEngine.Apply(wrong,feed,false,new string[0],Healthy,delegate{});},"Normal updater refuses a test installation");
   string missing=Fixture("Missing widget DLL"),missingFeed=Feed("Missing widget DLL feed",14,delegate(string folder){using(var zip=ZipFile.Open(P(folder,"Packages/mini-player.zip"),ZipArchiveMode.Update))zip.GetEntry("App/MiniPlayer.dll").Delete();});
   Reject(delegate{Apply(missing,missingFeed,Healthy,null);},"A stopped widget cannot be updated to a package missing its DLL");
   string invalid=Fixture("Invalid widget DLL"),invalidFeed=Feed("Invalid widget DLL feed",15,delegate(string folder){using(var zip=ZipFile.Open(P(folder,"Packages/mini-player.zip"),ZipArchiveMode.Update)){zip.GetEntry("App/MiniPlayer.dll").Delete();using(var writer=new StreamWriter(zip.CreateEntry("App/MiniPlayer.dll").Open()))writer.Write("invalid metadata");}});
   Reject(delegate{Apply(invalid,invalidFeed,Healthy,null);},"Corrupt widget assembly metadata is rejected before replacing files");
   string coreBad=Fixture("Invalid launcher DLL"),coreFeed=Feed("Invalid launcher DLL feed",16,delegate(string folder){System.IO.File.WriteAllText(P(folder,"Launcher.dll"),"invalid metadata");});
   Reject(delegate{Apply(coreBad,coreFeed,Healthy,null);},"Corrupt launcher assembly metadata is rejected before replacing files");
   string bootstrapBad=Fixture("Invalid bootstrap"),bootstrapFeed=Feed("Invalid bootstrap feed",17,delegate(string folder){System.IO.File.WriteAllText(P(folder,"Launcher.exe"),"invalid bootstrap");});
   Reject(delegate{Apply(bootstrapBad,bootstrapFeed,Healthy,null);},"Corrupt native entry shim cannot pass managed launcher health");
   string updaterBad=Fixture("Invalid updater"),updaterFeed=Feed("Invalid updater feed",18,delegate(string folder){System.IO.File.WriteAllText(P(folder,"Update.exe"),"invalid updater");});
   Reject(delegate{Apply(updaterBad,updaterFeed,Healthy,null);},"Corrupt future updater is rejected before replacing files");
   Console.WriteLine("TOTAL "+checks+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
