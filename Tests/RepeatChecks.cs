using System;
using System.IO;
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

static class RepeatChecks
{
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static int passed;
 static object Field(Player player,string name)=>typeof(Player).GetField(name,Private)!.GetValue(player)!;
 static void Set(Player player,string name,object? value)=>typeof(Player).GetField(name,Private)!.SetValue(player,value);
 static object? Call(Player player,string name,params object?[] args)=>typeof(Player).GetMethod(name,Private)!.Invoke(player,args);
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
 static async Task WaitFor(Func<bool> condition,string message){for(int i=0;i<40;i++){if(condition())return;await Task.Delay(50);}throw new Exception(message);}
 static void Pump(int milliseconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 static void Complete(Task task){var clock=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(clock.Elapsed.TotalSeconds>30)throw new Exception("Repeat checks timed out");Pump(20);}task.GetAwaiter().GetResult();}

 [STAThread] static int Main(string[] args)
 {
  try{SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());Complete(Run(args[0]));Console.WriteLine("Repeat checks: "+passed+" PASS");return 0;}
  catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }

 static async Task Run(string folder)
 {
  Directory.CreateDirectory(folder);
  var app=(Application)Activator.CreateInstance(typeof(Player).Assembly.GetType("MiniPlayer.PlayerApplication")!,true)!;
  var player=new Player();Set(player,"settingsPath",Path.Combine(folder,"settings.json"));Set(player,"busy",true);
  ((System.Windows.Forms.NotifyIcon)Field(player,"tray")).Visible=false;
  try{
   var button=(Button)Field(player,"repeat");
   Check(!button.IsEnabled,"No source disables repeat");
   using var provider=new MediaPlayer();provider.CommandManager.IsEnabled=false;provider.Volume=0;
   var controls=provider.SystemMediaTransportControls;
   controls.IsEnabled=true;controls.IsPlayEnabled=true;controls.IsPauseEnabled=true;
   controls.AutoRepeatMode=MediaPlaybackAutoRepeatMode.None;
   int requests=0;
   controls.AutoRepeatModeChangeRequested+=(_,args)=>{
    Interlocked.Increment(ref requests);controls.AutoRepeatMode=args.RequestedAutoRepeatMode;
   };
   string marker="MiniPlayer repeat checks "+Guid.NewGuid().ToString("N");
   controls.DisplayUpdater.Type=MediaPlaybackType.Music;controls.DisplayUpdater.MusicProperties.Title=marker;controls.DisplayUpdater.Update();
   controls.PlaybackStatus=MediaPlaybackStatus.Paused;
   var manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
   GlobalSystemMediaTransportControlsSession? source=null;
   for(int i=0;i<40&&source==null;i++){
    foreach(var candidate in manager.GetSessions()){
     try{if((await candidate.TryGetMediaPropertiesAsync()).Title==marker){source=candidate;break;}}catch{}
    }
    if(source==null)await Task.Delay(50);
   }
   Check(source!=null,"Controlled silent source published");Set(player,"session",source);Call(player,"ResetRepeat");
   await WaitFor(()=>source!.GetPlaybackInfo().Controls.IsRepeatEnabled,"Controlled source did not advertise repeat support");
   Call(player,"SynchronizeRepeat",source!.GetPlaybackInfo());
   Check(button.IsEnabled,"Supported source enables repeat");
   Check((MediaPlaybackAutoRepeatMode)Field(player,"repeatMode")==MediaPlaybackAutoRepeatMode.None,"Initial source repeat off");
   foreach(var expected in new[]{MediaPlaybackAutoRepeatMode.List,MediaPlaybackAutoRepeatMode.Track,MediaPlaybackAutoRepeatMode.None}){
    int before=requests;
    await (Task)Call(player,"CycleRepeatAsync")!;
    await WaitFor(()=>source.GetPlaybackInfo().AutoRepeatMode==expected,"Source did not apply repeat "+expected);
    Call(player,"SynchronizeRepeat",source.GetPlaybackInfo());
    Check(requests==before+1,"Exactly one repeat request for "+expected);
    Check((MediaPlaybackAutoRepeatMode)Field(player,"repeatMode")==expected,"Source and player agree on "+expected);
    Check(button.IsEnabled&&!(bool)Field(player,"repeatPending"),"Repeat button restored after "+expected);
   }
   controls.AutoRepeatMode=MediaPlaybackAutoRepeatMode.Track;
   await WaitFor(()=>source.GetPlaybackInfo().AutoRepeatMode==MediaPlaybackAutoRepeatMode.Track,"External repeat update missing");
   Call(player,"SynchronizeRepeat",source.GetPlaybackInfo());
   Check((MediaPlaybackAutoRepeatMode)Field(player,"repeatMode")==MediaPlaybackAutoRepeatMode.Track,"External repeat change synchronizes");

   // A completion from a previously selected source must not restore its UI state.
   Task pending=(Task)Call(player,"CycleRepeatAsync")!;
   Set(player,"session",null);Call(player,"ResetRepeat");await pending;
   Check(!button.IsEnabled&&!(bool)Field(player,"repeatPending"),"Old source completion leaves new source state untouched");
   Check((MediaPlaybackAutoRepeatMode)Field(player,"repeatMode")==MediaPlaybackAutoRepeatMode.None,"Source reset clears repeat mode");
   int count=requests;await (Task)Call(player,"CycleRepeatAsync")!;await Task.Delay(100);
   Check(requests==count,"No repeat request without selected source");
  }finally{Set(player,"closing",true);player.Close();}
 }
}
