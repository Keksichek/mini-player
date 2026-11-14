using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace MiniPlayer;

internal static class LaunchContext
{
 internal static string DataDirectory { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MiniPlayer");
 internal static string? LauncherRoot { get; private set; }
 internal static string InstanceName => LauncherRoot==null ? "Local\\MiniPlayer" : "Local\\MiniPlayer."+PathId(DataDirectory);
 internal static string PathId(string path)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(path).ToUpperInvariant())))[..16];
 internal static string Normalize(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

 internal static void Configure(string[] args)
 {
  string? root=null,data=null;
  for(int i=0;i<args.Length;i++){
   if(args[i] is "--launcher-root" or "--data-dir"){
    string key=args[i];if(++i>=args.Length)throw new ArgumentException("Не указана папка запуска.");
    if(key=="--launcher-root")root=Normalize(args[i]);else data=Normalize(args[i]);
   }
  }
  // Directly opening a managed widget must keep the same settings and uninstall behavior.
  if(root==null&&data==null){
   var app=new DirectoryInfo(AppContext.BaseDirectory);
   var candidate=app.Parent?.Parent?.Parent;
   if(app.Name.Equals("App",StringComparison.OrdinalIgnoreCase)&&app.Parent?.Name=="mini-player"&&app.Parent.Parent?.Name=="Widgets"&&candidate!=null&&File.Exists(Path.Combine(candidate.FullName,"launcher-install.json")))root=candidate.FullName;
  }
  if(root==null){if(data!=null)throw new ArgumentException("Папку данных задаёт лаунчер.");return;}
  string expectedData=Path.Combine(root,"Data","mini-player");
  string expectedApp=Path.Combine(root,"Widgets","mini-player","App");
  if(!Normalize(AppContext.BaseDirectory).Equals(expectedApp,StringComparison.OrdinalIgnoreCase)||
     (data!=null&&!data.Equals(expectedData,StringComparison.OrdinalIgnoreCase)))
   throw new InvalidOperationException("Папка виджета не соответствует папке лаунчера.");
  using var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"launcher-install.json")));
  if(marker.RootElement.GetProperty("ProductId").GetString()!="MiniPlayer.WidgetLauncher")throw new InvalidOperationException("Не удалось проверить лаунчер.");
  RejectLinks(expectedData);RejectLinks(expectedApp);
  LauncherRoot=root;DataDirectory=expectedData;
 }

 internal static void RejectLinks(string path)
 {
  for(var directory=new DirectoryInfo(path);directory!=null;directory=directory.Parent)
   if(directory.Exists&&(directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Выберите обычную папку без ссылок на другие каталоги.");
 }

 internal static void ImportPreviousSettings()
 {
  if(LauncherRoot==null)return;
  Directory.CreateDirectory(DataDirectory);
  string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
  foreach(string folder in new[]{"MiniPlayer","YandexMiniPlayer"})foreach(string name in new[]{"settings.json","appearance.json","hotkeys.json"}){
   string source=Path.Combine(local,folder,name),destination=Path.Combine(DataDirectory,name);
   if(!File.Exists(source)||File.Exists(destination))continue;
   try{File.Copy(source,destination,false);}catch(IOException){}catch(UnauthorizedAccessException){}
  }
 }
}

public sealed partial class Player
{
 static void RunInteractive(string[] args)
 {
  try{
   LaunchContext.Configure(args);
   using var mutex=new Mutex(true,LaunchContext.InstanceName,out bool created);
   string action=Array.IndexOf(args,"--settings")>=0?".Settings":Array.IndexOf(args,"--customize")>=0?".Customize":".Show";
   if(!created){
    // The first process may still be creating its event handles.
    for(int i=0;i<30;i++){
     if(EventWaitHandle.TryOpenExisting(LaunchContext.InstanceName+action,out var signal)){using(signal)signal.Set();break;}
     Thread.Sleep(50);
    }
    return;
   }
   LaunchContext.ImportPreviousSettings();
   var application=new PlayerApplication();var player=new Player();
   var events=new List<EventWaitHandle>();var waits=new List<RegisteredWaitHandle>();
   void Listen(string suffix,Action callback){
    var signal=new EventWaitHandle(false,EventResetMode.AutoReset,LaunchContext.InstanceName+suffix);events.Add(signal);
    waits.Add(ThreadPool.RegisterWaitForSingleObject(signal,(_,_)=>{
     if(!application.Dispatcher.HasShutdownStarted)application.Dispatcher.BeginInvoke(callback);
    },null,Timeout.Infinite,false));
   }
   Listen(".Show",()=>player.ReturnToScreen());
   Listen(".Settings",()=>{player.ReturnToScreen();player.OpenSettings();});
   Listen(".Customize",()=>{player.ReturnToScreen();player.Customize();});
   Listen(".Stop",()=>{player.closing=true;application.Shutdown();});
   player.Loaded+=(_,_)=>{
    if(action==".Settings")player.OpenSettings();else if(action==".Customize")player.Customize();
    if(Array.IndexOf(args,"--show-existing")>=0)player.ReturnToScreen();
   };
   try{application.Run(player);}finally{
    foreach(var wait in waits)wait.Unregister(null!);foreach(var signal in events)signal.Dispose();
    mutex.ReleaseMutex();
   }
  }catch(Exception ex){MessageBox.Show("Не удалось запустить Mini Player.\n\n"+ex.Message,"Mini Player",MessageBoxButton.OK,MessageBoxImage.Error);Environment.ExitCode=1;}
 }
}
