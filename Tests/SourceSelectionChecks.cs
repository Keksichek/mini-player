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
  typeof(Player).Assembly.GetType("MiniPlayer.LaunchContext")!.GetProperty("DataDirectory",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,directory);
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
   var popup=new ContextMenu();Set(player,"sourceMenu",popup);Call(player,"FillSourcePicker");
   var selected=popup.Items.OfType<MenuItem>().Single(item=>item.IsChecked);
   Check(((TextBlock)selected.Header).Text.Contains(marker),"Quick picker marks the current Windows session");
   Set(player,"busy",true);picker.SelectedIndex=-1;selected.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
   Check(picker.SelectedIndex==index && Equals(Field(player,"session"),owned),"Quick selection also changes the source in settings");
   Set(player,"sourceMenu",null);Set(player,"busy",false);
   var behavior=new StackPanel();Call(player,"AddPausePreference",behavior);
   var modes=behavior.Children.OfType<ComboBox>().Single();
   Check(modes.SelectedIndex==0,"Existing installations keep their normal appearance on pause");
   modes.SelectedIndex=1;await (Task)Call(player,"Refresh")!;
   Check(player.Opacity==.45,"Paused Windows source dims the player");
   Call(player,"ObservePause",false);Check(player.Opacity==1,"Playing again restores original appearance");
   modes.SelectedIndex=2;Call(player,"ObservePause",true);
   bool Suppresses()=>(bool)typeof(Player).GetProperty("SuppressPauseDisplay",Private)!.GetValue(player)!;
   Check(!Suppresses(),"A short pause keeps the widget visible");
   Set(player,"pauseStarted",Environment.TickCount64-5100);Check(Suppresses(),"A continuous pause activates hiding after five seconds");
   Call(player,"ManualPauseVisibility");Check(!Suppresses(),"Manual show overrides automatic hiding for this pause");
   Call(player,"ObservePause",false);Check(!Suppresses(),"Playback resets automatic hiding");
   Check(File.ReadAllText(Path.Combine(directory,"settings.json")).Contains("\"OnPause\":2"),"Pause behavior persists");
   modes.SelectedIndex=0;
   var artwork=(Image)Field(player,"cover");
   Check(artwork.Source==null,"A source can start with no album art");
   var imagePath=Path.GetFullPath(Path.Combine(directory,"album.png"));
   var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
   encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
    System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[]{0,40,200,255},4)));
   using(var output=File.Create(imagePath))encoder.Save(output);
   var imageFile=await Windows.Storage.StorageFile.GetFileFromPathAsync(imagePath);
   controls.DisplayUpdater.Thumbnail=Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(imageFile);
   controls.DisplayUpdater.Update();
   for(int i=0;i<45 && artwork.Source==null;i++){
    await (Task)Call(player,"Refresh")!;
    if(artwork.Source==null)await Task.Delay(100);
   }
   Check(artwork.Source!=null,"A late thumbnail is displayed without changing the title or artist");
   var loadedArtwork=artwork.Source;
   await (Task)Call(player,"Refresh")!;
   Check(ReferenceEquals(loadedArtwork,artwork.Source),"The next media poll reuses the decoded artwork");
   Set(player,"busy",true);picker.SelectedIndex=-1;Set(player,"busy",false);Set(player,"refreshSourceLabels",true);
   Task refresh=(Task)Call(player,"Refresh")!;
   if(!refresh.IsCompleted)Check(!(bool)Field(player,"refreshing"),"Metadata loading does not block manual selection");
   picker.SelectedIndex=index;
   await refresh;
   Check((await ((GlobalSystemMediaTransportControlsSession)Field(player,"session")).TryGetMediaPropertiesAsync()).Title==marker,"Manual selection survives a concurrent list refresh");
   await (Task)Call(player,"ReloadMediaSources")!;
   Check(picker.SelectedIndex>=0&&((string)picker.SelectedItem).Contains(marker,StringComparison.Ordinal),"Refresh keeps the selected application");
   var slider=(Slider)Field(player,"seek");
   var label=(TextBlock)Field(player,"timeLabel");
   slider.Maximum=7800;slider.Value=3907;
   Check(label.Text=="1:05:07 / 2:10:00","Seeking displays hours for long tracks");
   var clock=Field(player,"playbackClock");
   clock.GetType().GetMethod("Synchronize")!.Invoke(clock,new object?[]{90061d,DateTimeOffset.UtcNow,false,1d});
   Set(player,"timelineDuration",93600d);slider.Maximum=93600;
   Call(player,"RenderTimeline");
   Check(label.Text=="25:01:01 / 26:00:00","Live playback displays total hours across midnight");
   ((CheckBox)Field(player,"auto")).IsChecked=false;
   player.ShowActivated=false;player.Show();Pump();
   ((DispatcherTimer)Field(player,"timer")).Stop();
   modes.SelectedIndex=2;Call(player,"ObservePause",true);Set(player,"pauseStarted",Environment.TickCount64-5100);
   Call(player,"ObservePause",true);Check(!player.IsVisible,"A long pause hides the actual player window");
   Call(player,"ObservePause",false);Check(player.IsVisible,"Resumed playback shows the window hidden by the pause");
   Call(player,"HidePlayer");Call(player,"ObservePause",true);Call(player,"ObservePause",false);
   Check(!player.IsVisible,"Resumed playback respects a manually hidden window");
  }finally{Set(player,"closing",true);player.Close();app.Shutdown();}
 }
}
