using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MiniPlayer;
using Windows.Media;
using Windows.Media.Control;
using Windows.Media.Playback;

static class SourceSelectionChecks
{
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static int checks;
 static object Field(Player p,string name)=>typeof(Player).GetField(name,Private)!.GetValue(p)!;
 static void Set(Player p,string name,object? value)=>typeof(Player).GetField(name,Private)!.SetValue(p,value);
 static object? Call(Player p,string name,params object?[] args)=>typeof(Player).GetMethod(name,Private)!.Invoke(p,args);
 static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 [STAThread] static int Main(string[] args){
  try{
   SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
   var task=Run(args[0]);var timeout=System.Diagnostics.Stopwatch.StartNew();
   while(!task.IsCompleted){if(timeout.Elapsed.TotalSeconds>30)throw new Exception("Source selection checks timed out");Pump();}
   task.GetAwaiter().GetResult();Console.WriteLine("Source selection checks: "+checks+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
 static async Task Run(string directory){
  Directory.CreateDirectory(directory);
  var app=(Application)Activator.CreateInstance(typeof(Player).Assembly.GetType("MiniPlayer.PlayerApplication")!,true)!;
  var player=new Player();Set(player,"settingsPath",Path.Combine(directory,"settings.json"));Set(player,"busy",true);
  ((System.Windows.Forms.NotifyIcon)Field(player,"tray")).Visible=false;
  try{
   Call(player,"OpenSettings",true);
   var picker=(ComboBox)Field(player,"sources");
   Check(picker.IsEnabled,"Empty source picker is interactive");
   using var provider=new MediaPlayer();provider.CommandManager.IsEnabled=false;provider.Volume=0;
   var controls=provider.SystemMediaTransportControls;controls.IsEnabled=true;controls.IsPlayEnabled=true;controls.IsPauseEnabled=true;
   string marker="MiniPlayer source selection "+Guid.NewGuid().ToString("N");
   controls.DisplayUpdater.Type=MediaPlaybackType.Music;controls.DisplayUpdater.MusicProperties.Title=marker;controls.DisplayUpdater.Update();controls.PlaybackStatus=MediaPlaybackStatus.Paused;
   var manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
   GlobalSystemMediaTransportControlsSession? owned=null;
   for(int i=0;i<50&&owned==null;i++){
    foreach(var candidate in manager.GetSessions())try{if((await candidate.TryGetMediaPropertiesAsync()).Title==marker){owned=candidate;break;}}catch{}
    if(owned==null)await Task.Delay(50);
   }
   Check(owned!=null,"Own silent source is published");
   Set(player,"manager",manager);Set(player,"busy",false);
   await (Task)Call(player,"ReloadMediaSources")!;
   int index=picker.Items.Cast<string>().ToList().FindIndex(x=>x.Contains(marker,StringComparison.Ordinal));
   Check(index>=0,"Source appears in selectable application list");
   Set(player,"busy",true);picker.SelectedIndex=-1;Set(player,"busy",false);
   picker.SelectedIndex=index;
   while((bool)Field(player,"busy"))await Task.Delay(10);
   Check((await ((GlobalSystemMediaTransportControlsSession)Field(player,"session")).TryGetMediaPropertiesAsync()).Title==marker,"Manual selection changes the player source");
   Check(File.ReadAllText(Path.Combine(directory,"settings.json")).Contains(owned!.SourceAppUserModelId,StringComparison.Ordinal),"Manual source selection is saved");
   Set(player,"busy",true);picker.SelectedIndex=-1;Set(player,"busy",false);Set(player,"refreshSourceLabels",true);
   Task refresh=(Task)Call(player,"Refresh")!;
   if(!refresh.IsCompleted)Check(!(bool)Field(player,"refreshing"),"Metadata loading does not block manual selection");
   picker.SelectedIndex=index;
   await refresh;
   Check((await ((GlobalSystemMediaTransportControlsSession)Field(player,"session")).TryGetMediaPropertiesAsync()).Title==marker,"Manual selection survives a concurrent list refresh");
   await (Task)Call(player,"ReloadMediaSources")!;
   Check(picker.SelectedIndex>=0&&((string)picker.SelectedItem).Contains(marker,StringComparison.Ordinal),"Refresh keeps the selected application");
  }finally{Set(player,"closing",true);player.Close();app.Shutdown();}
 }
}
