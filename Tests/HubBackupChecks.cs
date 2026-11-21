using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WidgetLauncher;

static class HubBackupChecks {
 static int checks;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);checks++;}
 static void Reject(Action action,string label){
  try{action();}catch(Exception ex)when(ex is InvalidDataException or IOException or JsonException){Check(true,label);return;}
  throw new Exception(label);
 }
 static string Data(WidgetManager hub,string file)=>Path.Combine(hub.Root,"Data",file);
 static void Put(WidgetManager hub,string name,string value){
  var file=Data(hub,name);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,value);
 }
 static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
 static void Restore(WidgetManager target,BackupPreview preview)=>HubBackupRestore.RestoreAsync(target,preview).GetAwaiter().GetResult();
 static WidgetManager TestHub(string path){
  using var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"launcher-install.json")));
  if(!marker.RootElement.GetProperty("TestInstallation").GetBoolean()||!string.Equals(marker.RootElement.GetProperty("InstallPath").GetString(),path,StringComparison.OrdinalIgnoreCase))throw new Exception("Isolated test installation required");
  return new WidgetManager(path);
 }
 static byte[] AnimatedGif(){
  using var output=new MemoryStream();var gif=new GifBitmapEncoder();
  foreach(byte color in new byte[]{60,190})gif.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2,1,96,96,PixelFormats.Gray8,null,new byte[]{color,color},2)));
  gif.Save(output);return output.ToArray();
 }
 static IEnumerable<DependencyObject> Visuals(DependencyObject parent){
  yield return parent;
  for(int index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
   foreach(var child in Visuals(VisualTreeHelper.GetChild(parent,index)))yield return child;
 }
 static void CheckReview(WidgetManager target,Widget widget,string archive){
  var priorContext=SynchronizationContext.Current;
  _=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  var dispatcher=Dispatcher.CurrentDispatcher;
  SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
  var window=new LauncherWindow(target.Root,false,"settings");
  var surface=(FrameworkElement)window.Content;
  var size=new Size(window.Width,window.Height);
  List<DependencyObject> Controls(){
   surface.Measure(size);surface.Arrange(new Rect(size));surface.UpdateLayout();
   return Visuals(surface).ToList();
  }
  const string restore="Восстановить из копии";
  Button? ButtonNamed(string title)=>Controls().OfType<Button>().SingleOrDefault(button=>button.Content as string==title);
  string[] Snapshot()=>Directory.GetFiles(Data(target,""),"*",SearchOption.AllDirectories).OrderBy(path=>path)
   .Select(path=>path+":"+Hash(path)).ToArray();
  try{
   var before=Snapshot();
   Check(ButtonNamed("Выбрать копию…")?.IsEnabled==true&&ButtonNamed(restore)==null,"Settings only offer choosing a backup before review");
   var review=typeof(LauncherWindow).GetMethod("ReviewHubBackup",BindingFlags.Instance|BindingFlags.NonPublic)
    ??throw new MissingMethodException("ReviewHubBackup");
   review.Invoke(window,new object[]{archive});
   var frame=new DispatcherFrame();var timeout=DateTime.UtcNow.AddSeconds(10);
   var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};
   timer.Tick+=(_,_)=>{if(ButtonNamed(restore)!=null||DateTime.UtcNow>=timeout)frame.Continue=false;};
   timer.Start();try{Dispatcher.PushFrame(frame);}finally{timer.Stop();}
   string summary=string.Join("\n",Controls().OfType<TextBlock>().Select(t=>t.Text));
   Check(ButtonNamed(restore)?.IsEnabled==true&&summary.Contains(Path.GetFileName(archive))
    &&new[]{widget.Name,"Пресетов: 1","Фонов: 1","В избранном: 1","горячие клавиши","привязки к источникам"}.All(summary.Contains),
    "Choosing an archive displays its real widget, preset, GIF, favorite and settings before confirmation");
   ButtonNamed("Отмена")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Check(ButtonNamed(restore)==null&&!Controls().OfType<TextBlock>().Any(text=>text.Text==Path.GetFileName(archive))
    &&before.SequenceEqual(Snapshot())&&!target.IsInstalled(widget),"Cancel dismisses the summary and leaves every destination file and widget unchanged");
  }finally{
   window.RequestShutdown();
   SynchronizationContext.SetSynchronizationContext(priorContext);
  }
 }
 static void RejectInvalidSetting(string valid,string copy,string name,string invalidJson){
  File.Copy(valid,copy,true);
  using(var zip=ZipFile.Open(copy,ZipArchiveMode.Update)){
   var description=zip.GetEntry("backup.json")!;
   BackupManifest manifest;
   using(var input=description.Open())manifest=JsonSerializer.Deserialize<BackupManifest>(input)!;
   description.Delete();
   string entryName="Data/"+name;
   zip.GetEntry(entryName)!.Delete();
   byte[] bytes=Encoding.UTF8.GetBytes(invalidJson);
   using(var stream=zip.CreateEntry(entryName).Open())stream.Write(bytes);
   manifest.Files[name]=Convert.ToHexString(SHA256.HashData(bytes));
   using var updated=zip.CreateEntry("backup.json").Open();
   JsonSerializer.Serialize(updated,manifest);
  }
  Reject(()=>HubBackupArchive.ReadArchive(copy),"Archive review detects a wrong JSON type even with a valid checksum in "+name);
 }
 static int[] PlayerProcessIds(WidgetManager manager){
  var ids=new List<int>();
  string runtime=Path.Combine(manager.Root,"Runtime","dotnet.exe");
  foreach(var process in Process.GetProcessesByName("dotnet"))using(process){
   try{if(string.Equals(process.MainModule?.FileName,runtime,StringComparison.OrdinalIgnoreCase))ids.Add(process.Id);}
   catch(InvalidOperationException){} // A process can exit during enumeration.
   catch(Win32Exception){} // Other users' processes are outside this fixture.
  }
  return ids.ToArray();
 }
 static void CheckRunningPlayer(WidgetManager target,Widget widget,BackupPreview preview){
  target.InstallAsync(widget).GetAwaiter().GetResult();
  Put(target,"mini-player/settings.json","{\"Pin\":true,\"MovementLocked\":true,\"Auto\":false,\"Source\":\"\"}");
  void Stop()=>target.StopAsync(widget).GetAwaiter().GetResult();
  string config=Path.ChangeExtension(target.Executable(widget),".runtimeconfig.json"),held=config+".held";
  string stage=Path.Combine(target.Root,".hub-restore");
  try{
   target.Launch(widget);
   int initialId=PlayerProcessIds(target).Single();
   using var initial=Process.GetProcessById(initialId);
   Restore(target,preview);
   bool exited=initial.WaitForExit(5000);
   int resumed=PlayerProcessIds(target).Single();
   Check(exited&&initialId!=resumed&&target.IsRunning(widget),"A real running Mini Player exits and a single new instance starts after restore");
   Check(HubBackupRestore.RecoverIfNeeded(target.Root)&&PlayerProcessIds(target).SequenceEqual(new[]{resumed}),
    "A later hub startup keeps that same player instance");
   // Only the resumed player can replace this sentinel with its own in-memory settings on close.
   Put(target,"mini-player/settings.json","{\"Pin\":true,\"MovementLocked\":true,\"Sentinel\":true}");
   Stop();
   using(var saved=JsonDocument.Parse(File.ReadAllText(Data(target,"mini-player/settings.json")))){
    var settings=saved.RootElement;
    Check(!settings.TryGetProperty("Sentinel",out _)&&!settings.GetProperty("Pin").GetBoolean()
     &&!settings.GetProperty("MovementLocked").GetBoolean(),"The resumed player loaded the restored pin and movement preferences");
   }
   target.Launch(widget);
   File.Move(config,held);
   try{Restore(target,preview);throw new Exception("Expected a restart failure.");}
   catch(HubBackupRestore.CommittedException){
    Check(!target.IsRunning(widget)&&!Directory.Exists(stage)&&HubBackupRestore.RecoverIfNeeded(target.Root)
     &&Hash(Data(target,"mini-player/settings.json"))==preview.Manifest.Files["mini-player/settings.json"],
     "When a widget cannot restart, restored settings remain and the next hub startup proceeds");
   }
   // Crash after committing the data, before resuming the old running widget.
   Directory.CreateDirectory(stage);
   File.WriteAllText(Path.Combine(stage,"restore.json"),JsonSerializer.Serialize(new{Version=1,Install=Array.Empty<string>(),Running=new[]{widget.Id}}));
   File.WriteAllText(Path.Combine(stage,"committed"),"1");
   string? warning=null;
   Check(HubBackupRestore.RecoverIfNeeded(target.Root,text=>warning=text)&&!string.IsNullOrWhiteSpace(warning)
    &&!Directory.Exists(stage),"Startup recovery reports an unavailable widget and allows the hub to open on its first attempt");
   var hub=new LauncherWindow(target.Root,false,"settings");
   try{
    hub.ShowStartupWarning(warning!);
    var surface=(FrameworkElement)hub.Content;var size=new Size(hub.Width,hub.Height);
    surface.Measure(size);surface.Arrange(new Rect(size));surface.UpdateLayout();
    Check(Visuals(surface).OfType<TextBlock>().Any(block=>block.Text==warning&&block.Visibility==Visibility.Visible),
     "The recovered hub displays the widget restart warning in its actual settings window");
   }finally{hub.RequestShutdown();}
  }finally{
   if(File.Exists(held))File.Move(held,config);
   Stop();
  }
 }
 [STAThread] public static int Main(string[] args){
  try{Run(args);Console.WriteLine("TOTAL "+checks+" PASS");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  finally{Application.Current?.Shutdown();}
 }
 static void Run(string[] args){
  string scratch=args[0];var from=TestHub(args[1]);var to=TestHub(args[2]);
  var widget=from.ReadCatalog().Single(w=>w.Id=="mini-player");
  from.InstallAsync(widget).GetAwaiter().GetResult();
  Check(!to.IsInstalled(widget),"Target starts without the widget");
  var origin=new PresetStore(from.Root);var destination=new PresetStore(to.Root);
  var input=PresetThemes.Create("Лаванда");input.Name="Backup fixture";
  byte[] gif=AnimatedGif();input.MediaPath=origin.StoreMedia(gif,".gif");
  origin.Save(input);origin.Apply(input);origin.SaveDraft(input);origin.SetFavorite(input.Id,true);
  Put(from,"hub-settings.json","{\"AllowOffscreen\":false,\"LightMode\":true,\"CloseMode\":2}");
  Put(from,"mini-player/settings.json","{\"X\":16,\"Y\":24,\"Pin\":false,\"MovementLocked\":false,\"Source\":\"Fixture.Media\",\"Anchor\":8,\"AnchorGap\":23,\"AnchorMonitor\":\"monitor-fixture\"}");
  Put(from,"mini-player/hotkeys.json","{\"Pause\":{\"Modifiers\":2,\"Key\":90}}");
  Put(from,"mini-player/appearance.json","{\"Theme\":\"Кофе\",\"Accent\":\"#F0AA60\"}");
  Put(from,"mini-player/source-presets.json",JsonSerializer.Serialize(new System.Collections.Generic.Dictionary<string,string>{{"Fixture.Media",input.Id}}));
  Put(from,"mini-player/source-preset-override.json",JsonSerializer.Serialize(new{PresetId=input.Id,PreviousId=(string?)null,General=input}));
  Put(from,"mini-player/only-on-source.txt","private");
  var old=PresetThemes.Create("Лес");destination.Save(old);destination.SetFavorite(old.Id,true);
  Put(to,"mini-player/personal.txt","keep local");
  Put(to,"mini-player/settings.json","{\"Source\":\"old\"}");
  string file=Path.Combine(scratch,"copy.vibebackup"),other=Path.Combine(scratch,"other.vibebackup");
  HubBackupArchive.Export(from,file);
  var preview=HubBackupArchive.ReadArchive(file);
  CheckReview(to,widget,file);
  string invalidArchive=Path.Combine(scratch,"invalid-types.vibebackup");
  RejectInvalidSetting(file,invalidArchive,"mini-player/settings.json","{\"AnchorGap\":\"large\"}");
  RejectInvalidSetting(file,invalidArchive,"mini-player/hotkeys.json","{\"Pause\":{\"Modifiers\":2,\"Key\":true}}");
  Check(preview.Manifest.Widgets.SequenceEqual(new[]{widget.Id})&&preview.Manifest.Files.ContainsKey("Customization/mini-player/Favorites/"+input.Id+".json"),"Preview lists installed widget and favorite marker");
  using(var zip=ZipFile.OpenRead(file))Check(zip.Entries.All(e=>e.FullName=="backup.json"||e.FullName.StartsWith("Data/"))&&zip.GetEntry("Data/mini-player/only-on-source.txt")==null,"Backup contains portable user data only");
  var expected=preview.Manifest.Files.ToDictionary(p=>p.Key,p=>Hash(Data(from,p.Key)));
  HubBackupArchive.Export(to,other);
  string good=Path.Combine(scratch,"original.vibebackup");File.Copy(file,good);
  File.Copy(other,file,true);
  Reject(()=>Restore(to,preview),"Replacing the file after preview is rejected");
  Check(!to.IsInstalled(widget)&&destination.LoadSaved(old.Id)!=null&&File.ReadAllText(Data(to,"mini-player/settings.json")).Contains("old"),"Changed archive leaves old settings and widget installation untouched");
  File.Copy(good,file,true);
  using(var zip=ZipFile.Open(file,ZipArchiveMode.Update)){
   const string name="Data/mini-player/settings.json";zip.GetEntry(name)!.Delete();
   using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write("{}");
  }
  Reject(()=>Restore(to,preview),"Tampering with data while retaining the original manifest is rejected");
  Check(!to.IsInstalled(widget)&&destination.LoadSaved(old.Id)!=null,"Invalid archive cannot replace or install user content");
  File.Copy(good,file,true);
  using(var locked=File.Open(Data(to,"mini-player/personal.txt"),FileMode.Open,FileAccess.Read,FileShare.None))
   Reject(()=>Restore(to,preview),"A file in use interrupts restore before replacing the user's data");
  Check(!to.IsInstalled(widget)&&destination.LoadSaved(old.Id)!=null&&File.ReadAllText(Data(to,"mini-player/personal.txt"))=="keep local",
   "Rollback undoes the new widget installation and keeps the previous library");
  Restore(to,preview);
  Check(to.IsInstalled(widget)&&Hash(to.Executable(widget))==Hash(from.Executable(widget)),"A missing widget is installed from the destination hub package");
  Check(expected.All(p=>File.Exists(Data(to,p.Key))&&Hash(Data(to,p.Key))==p.Value),"Every previewed setting, anchor, hotkey, source mapping and preset restores byte-for-byte");
  var saved=destination.ListSaved().Single();
  Check(saved.Favorite&&saved.Preset.Id==input.Id&&destination.LoadActive()?.Id==input.Id&&destination.LoadDraft()?.Id==input.Id,"Favorite, active preset and draft are usable in the second installation");
  Check(File.ReadAllBytes(destination.ResolveMedia(saved.Preset.MediaPath)).SequenceEqual(gif),"Both frames of the content-addressed GIF survive relocation");
  Check(destination.LoadSaved(old.Id)==null&&!File.Exists(Data(to,"Customization/mini-player/Favorites/"+old.Id+".json"))&&File.ReadAllText(Data(to,"mini-player/personal.txt"))=="keep local","Restore replaces previous managed presets and preserves local extras");
  Check(!File.Exists(Data(to,"mini-player/only-on-source.txt")),"Nonportable files from the other PC stay out of the target");

  string stage=Path.Combine(to.Root,".hub-restore"),data=Path.Combine(to.Root,"Data");
  string journal=JsonSerializer.Serialize(new{Version=1,Install=new[]{widget.Id},Running=Array.Empty<string>()});
  string previous=Path.Combine(stage,"previous");
  Directory.CreateDirectory(previous);
  File.WriteAllText(Path.Combine(previous,"old.txt"),"old");
  File.WriteAllText(Path.Combine(stage,"restore.json"),journal);
  File.WriteAllText(Path.Combine(stage,"committed"),"1");
  Check(HubBackupRestore.RecoverIfNeeded(to.Root)&&to.IsInstalled(widget)&&!Directory.Exists(stage)
   &&expected.All(p=>Hash(Data(to,p.Key))==p.Value),"Recovery after a committed restore keeps the new data and widget");
  Directory.CreateDirectory(stage);
  Directory.Move(data,previous);Put(to,"mini-player/settings.json","{\"incomplete\":true}");
  File.WriteAllText(Path.Combine(stage,"restore.json"),journal);
  File.WriteAllText(Path.Combine(stage,"switching"),"1");
  Check(HubBackupRestore.RecoverIfNeeded(to.Root)&&!to.IsInstalled(widget)&&!Directory.Exists(stage),"Interrupted restore rolls back widget installation and removes its journal");
  Check(expected.All(p=>Hash(Data(to,p.Key))==p.Value)&&File.ReadAllText(Data(to,"mini-player/personal.txt"))=="keep local","Crash rollback restores old settings, library, media and extras");
  Check(HubBackupRestore.RecoverIfNeeded(to.Root),"Recovery can run again safely");
  CheckRunningPlayer(to,widget,preview);
 }
}
