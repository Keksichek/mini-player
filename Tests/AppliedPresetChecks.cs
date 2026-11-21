using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class AppliedPresetChecks
{
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
 static int count;
 static object player=null!;static Type type=null!;static string root="";
 static object? Get(string n)=>type.GetField(n,Flags)!.GetValue(player);
 static void Set(string n,object value)=>type.GetField(n,Flags)!.SetValue(player,value);
 static object? Call(string n,params object[] args)=>type.GetMethod(n,Flags)!.Invoke(player,args);
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static IEnumerable<DependencyObject> Walk(DependencyObject node){yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,i)))yield return child;}
 static Dictionary<string,FrameworkElement> Elements()=>((IReadOnlyDictionary<string,FrameworkElement>)Get("customSurface")!.GetType().GetProperty("Elements")!.GetValue(Get("customSurface"))!).ToDictionary(x=>x.Key,x=>x.Value);
 static void Layout(){var w=(Window)player;var surface=(FrameworkElement)w.Content;surface.Measure(new Size(w.Width,w.Height));surface.Arrange(new Rect(0,0,w.Width,w.Height));surface.UpdateLayout();}
 static string Active=>Path.Combine(root,"Data","Customization","mini-player","active.json");
 static void WritePreset(int font=16,string shape="rounded"){Directory.CreateDirectory(Path.GetDirectoryName(Active)!);File.WriteAllText(Active,JsonSerializer.Serialize(new{Id="",Name="Own integration preset",Theme="Лаванда",Width=420,Height=260,FontSize=font,Shape=shape,Opacity=.83,TextColor="#ABCDEF",BackgroundColor="#172331"}));}
 static void CheckAck(bool success){using var ack=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Data","mini-player","preset-applied.json")));Check(ack.RootElement.GetProperty("Success").GetBoolean()==success&&ack.RootElement.GetProperty("Sha256").GetString()==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Active))).ToLowerInvariant(),"Acknowledgement matches the exact applied bytes and result");}
 [STAThread]static int Main(string[] args){Application? app=null;try{
  root=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.Combine(root,"Data","mini-player"));
  var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"MiniPlayer.dll"));type=assembly.GetType("MiniPlayer.Player",true)!;
  var context=assembly.GetType("MiniPlayer.LaunchContext",true)!;context.GetProperty("LauncherRoot",Static)!.SetValue(null,root);context.GetProperty("DataDirectory",Static)!.SetValue(null,Path.Combine(root,"Data","mini-player"));
  app=(Application)Activator.CreateInstance(assembly.GetType("MiniPlayer.PlayerApplication",true)!,true)!;app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  player=Activator.CreateInstance(type)!;((System.Windows.Forms.NotifyIcon)Get("tray")!).Visible=false;Call("LoadAppearance");
  WritePreset();Check((bool)Call("RefreshCustomPreset",true)!,"Live apply accepts the launcher preset");Layout();CheckAck(true);
  var w=(Window)player;Check(w.Width==420&&w.Height==260&&Get("customSurface")==Get("deviceFace"),"Actual player renders the shared surface at exact preset dimensions");
  var elements=Elements();Check(elements.Keys.OrderBy(x=>x).SequenceEqual(new[]{"cover","title","artist","previous","play","next","repeat","pin","lock","clickthrough","hide","menu","progress","time","volume"}.OrderBy(x=>x)),"All editable player elements exist in the live surface");
  ((TextBlock)Get("title")!).Text="Live title";((TextBlock)Get("artist")!).Text="Live artist";((TextBlock)Get("timeLabel")!).Text="1:20 / 5:00";
  Check(((TextBlock)elements["title"]).Text=="Live title"&&((TextBlock)elements["artist"]).Text=="Live artist"&&((TextBlock)elements["time"]).Text=="1:20 / 5:00","Track metadata and clock bind to actual live source controls");
  var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,255,255},4);((Image)Get("cover")!).Source=bitmap;Check(ReferenceEquals(((Image)elements["cover"]).Source,bitmap),"Actual album art reaches the shared surface");
  var seek=(Slider)Get("seek")!;seek.Maximum=300;seek.IsEnabled=true;seek.Value=80;Check(((Slider)elements["progress"]).Value==80,"Live timeline flows to custom progress control");((Slider)elements["progress"]).Value=90;Check(seek.Value==90,"Custom progress changes reach the seek source");
  var volume=(Slider)Get("volumeSlider")!;volume.IsEnabled=true;volume.Value=34;Check(((Slider)elements["volume"]).Value==34,"Live source volume flows to custom slider");((Slider)elements["volume"]).Value=61;Check(volume.Value==61,"Custom volume changes reach existing audio control");
  ((Button)Get("play")!).IsEnabled=false;Check(!elements["play"].IsEnabled,"Unavailable media commands remain disabled");
  bool pin=w.Topmost;((Button)elements["pin"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(w.Topmost!=pin,"Custom pin button controls actual window topmost state");
  ((Button)elements["lock"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check((bool)Get("movementLocked")!,"Custom movement lock controls actual dragging permission");
  ((Button)elements["clickthrough"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check((bool)Get("clickThrough")!,"Custom button enables click-through");((System.Windows.Forms.ToolStripMenuItem)Get("restoreMouseItem")!).PerformClick();Check(!(bool)Get("clickThrough")!,"Tray restores custom player mouse input without an assigned hotkey");
  ((Button)elements["pin"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check((bool)Get("movementLocked")!,"Pin remains independent of movement lock in custom layout");
  Check(((Canvas)Get("deviceFace")!).ContextMenu!.Items.OfType<MenuItem>().Select(x=>x.Header as string).SequenceEqual(new[]{"Настройки","Кастомизация","Скрыть"}),"Right-click settings and customization stay accessible");
  var previousSurface=Get("customSurface");Call("RefreshCustomPreset",false);Check(ReferenceEquals(previousSurface,Get("customSurface")),"Unchanged preset polling does not rebuild the surface");
  var sizes=elements.ToDictionary(x=>x.Key,x=>(x.Value.Width,x.Value.Height,Canvas.GetLeft(x.Value),Canvas.GetTop(x.Value)));
  WritePreset(60);Check((bool)Call("RefreshCustomPreset",true)!,"Font size 60 applies to the live player");Layout();elements=Elements();
  Check(((TextBlock)elements["title"]).FontSize==60,"Live title uses exact requested font size");Check(elements.All(x=>sizes[x.Key]==(x.Value.Width,x.Value.Height,Canvas.GetLeft(x.Value),Canvas.GetTop(x.Value))),"Changing font size leaves all element rectangles unchanged");
  foreach(string shape in new[]{"star","square","circle","diamond","hexagon","heart"}){WritePreset(16,shape);Check((bool)Call("RefreshCustomPreset",true)!,"Live player accepts shape: "+shape);Layout();}
  previousSurface=Get("customSurface");File.WriteAllText(Active,"{");Check(!(bool)Call("RefreshCustomPreset",true)!&&ReferenceEquals(previousSurface,Get("customSurface")),"Corrupt incoming preset leaves previous live layout intact");CheckAck(false);
  WritePreset();Call("RefreshCustomPreset",true);Call("Customize",true);var editor=(Window)Get("customizeWindow")!;editor.Close();Check(Get("customSurface")!=null&&File.Exists(Active),"Cancelling standard appearance retains active custom preset");
  Call("CommitStandardAppearance");Call("ApplyAppearance");Check(Get("customSurface")==null&&!File.Exists(Active)&&Get("deviceFace")!=null,"Returning to standard appearance removes only the active selection and restores classic player");
  Console.WriteLine("TOTAL "+count+" PASS");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{if(player!=null){try{Set("closing",true);((Window)player).Close();}catch{}try{((System.Windows.Forms.NotifyIcon)Get("tray")!).Dispose();}catch{}}app?.Shutdown();}}
}
