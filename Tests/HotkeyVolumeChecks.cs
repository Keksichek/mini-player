using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

static class HotkeyVolumeChecks
{
 const BindingFlags Instance=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
 const BindingFlags Static=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
 static Type type=null!;static object player=null!;static int count;
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
 static object? Field(string name)=>type.GetField(name,Instance)!.GetValue(player);
 static void Set(string name,object? value)=>type.GetField(name,Instance)!.SetValue(player,value);
 static object? Call(string name,params object?[] args)=>type.GetMethod(name,Instance)!.Invoke(player,args);
 static void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
 static void Layout(FrameworkElement surface,double width,double height){surface.Measure(new Size(width,height));surface.Arrange(new Rect(0,0,width,height));surface.UpdateLayout();}
 static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 static void Complete(Task task){while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();}
 [STAThread]static int Main(string[] args){
  Application? application=null;
  try{
   string data=Path.GetFullPath(args[0]);Directory.CreateDirectory(data);
   var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"MiniPlayer.dll"));type=assembly.GetType("MiniPlayer.Player",true)!;
   application=(Application)Activator.CreateInstance(assembly.GetType("MiniPlayer.PlayerApplication",true)!,true)!;
   player=Activator.CreateInstance(type)!;Set("settingsPath",Path.Combine(data,"settings.json"));Set("busy",true);
   ((System.Windows.Forms.NotifyIcon)Field("tray")!).Visible=false;
   File.WriteAllText(Path.Combine(data,"settings.json"),"{\"X\":80,\"Y\":80,\"Pin\":false,\"Auto\":false,\"Source\":\"\"}");
   File.WriteAllText(Path.Combine(data,"hotkeys.json"),"{}");
   var actions=type.GetNestedType("HotkeyAction",BindingFlags.NonPublic)!;
   Check(Convert.ToInt32(Enum.Parse(actions,"Pin"))==4&&Convert.ToInt32(Enum.Parse(actions,"Unpin"))==5,"Existing hotkey IDs remain unchanged");
   var themes=(string[])type.GetField("Themes",Static)!.GetValue(null)!;
   var backing=(Slider)Field("volumeSlider")!;Set("volumeSync",true);backing.IsEnabled=true;
   foreach(string theme in themes){
    File.WriteAllText(Path.Combine(data,"appearance.json"),"{\"Theme\":\""+theme+"\",\"Accent\":\"#B9A1FF\"}");Call("LoadAppearance");
    var window=(Window)player;Layout((FrameworkElement)window.Content,window.Width,window.Height);
    var face=(Canvas)Field("deviceFace")!;var slider=(Slider)Field("faceVolume")!;
    var bounds=new Rect(slider.TranslatePoint(new Point(),face),slider.RenderSize);
    Check(bounds.Left>=0&&bounds.Right<=face.Width+.01&&bounds.Top>=0&&bounds.Bottom<=face.Height+.01,theme+" volume is inside the player");
    var menu=face.ContextMenu!;Check(menu.Items.OfType<MenuItem>().All(item=>item.Header is string)&&menu.Items.Count==3,theme+" context menu has no lock or pin action");
    backing.IsEnabled=true;backing.Value=17;Check(Math.Abs(slider.Value-17)<.001,theme+" source-volume updates reach player slider");
    int changes=0;RoutedPropertyChangedEventHandler<double> changed=(_,_)=>changes++;backing.ValueChanged+=changed;
    try{slider.Value=38;Check(Math.Abs(backing.Value-38)<.001&&changes==1,theme+" player edit updates canonical slider once");}finally{backing.ValueChanged-=changed;}
    backing.IsEnabled=false;Check(!slider.IsEnabled,theme+" source unavailability disables the player slider");
   }
   Call("ResetVolumeSource");Check(!backing.IsEnabled&&(string)Field("audioTarget")! == "","Source change clears stale audio target");
   var audio=assembly.GetType("MiniPlayer.Audio",true)!;var entryType=audio.GetNestedType("Entry",BindingFlags.Public)!;
   var entries=Array.CreateInstance(entryType,3);int entryIndex=0;
   foreach(string name in new[]{"vlc","chrome","Spotify"})entries.SetValue(Activator.CreateInstance(entryType,new object[]{name,50d}),entryIndex++);
   foreach(var pair in new[]{("vlc.exe","vlc"),("C:\\Apps\\VLC\\vlc.exe","vlc"),("SPOTIFY.EXE","Spotify"),("SpotifyAB.SpotifyMusic_123!Spotify","Spotify"),("Google.Chrome","chrome"),("notvlclient.exe",(string?)null),("notspotify.exe",(string?)null),("",(string?)null)})
    Check((string?)audio.GetMethod("Match",Static)!.Invoke(null,new object[]{pair.Item1,entries})==pair.Item2,"Audio source match is exact for "+pair.Item1);
   var native=new WindowInteropHelper((Window)player);native.EnsureHandle();
   var bindingType=type.GetNestedType("HotkeyBinding",BindingFlags.NonPublic)!;
   var modifiers=System.Windows.Input.ModifierKeys.Control|System.Windows.Input.ModifierKeys.Alt|System.Windows.Input.ModifierKeys.Shift;
   string[] newActions={"Hide","Show","LockMovement","UnlockMovement"};
   for(int i=0;i<newActions.Length;i++){
    var action=Enum.Parse(actions,newActions[i]);var key=(System.Windows.Input.Key)((int)System.Windows.Input.Key.F17+i);
    object binding=Activator.CreateInstance(bindingType,Instance,null,new object[]{modifiers,key},null)!;
    object?[] assign={action,binding,null};
    Check((bool)type.GetMethod("TryAssignHotkey",Instance)!.Invoke(player,assign)!,"Register native hotkey "+newActions[i]+": "+assign[2]);
   }
   // Idempotent direct operations before the native-message dispatch checks.
   Set("movementLocked",false);
   Complete((Task)Call("ExecuteHotkey",Enum.Parse(actions,"LockMovement"))!);Complete((Task)Call("ExecuteHotkey",Enum.Parse(actions,"LockMovement"))!);
   Check((bool)Field("movementLocked")!,"Repeated movement-lock action stays locked");
   Complete((Task)Call("ExecuteHotkey",Enum.Parse(actions,"UnlockMovement"))!);Complete((Task)Call("ExecuteHotkey",Enum.Parse(actions,"UnlockMovement"))!);
   Check(!(bool)Field("movementLocked")!,"Repeated unlock action stays movable");
   void Send(string name){var action=Enum.Parse(actions,name);var registered=(System.Collections.IDictionary)Field("registeredHotkeys")!;int id=(int)registered[action]!;Check(PostMessage(native.Handle,0x312,new IntPtr(id),IntPtr.Zero),"Post native action "+name);Pump();}
   Send("Show");Check(((Window)player).IsVisible,"Show hotkey reveals the player");
   Send("Hide");Check(!((Window)player).IsVisible&&(bool)Field("shownByUser")!,"Hide hotkey suppresses automatic reappearance");
   Send("LockMovement");Check((bool)Field("movementLocked")!,"Native lock hotkey locks movement");
   Send("UnlockMovement");Check(!(bool)Field("movementLocked")!,"Native unlock hotkey unlocks movement");
   Console.WriteLine("TOTAL "+count+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  finally{if(player!=null){try{Set("closing",true);((Window)player).Close();}catch{}}if(application!=null)application.Shutdown();}
 }
}
