using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class InterfaceThemeChecks
{
 const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int count;
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr handle,int attribute,out int value,int size);
 static bool NativeMode(Window window,bool dark){var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;return handle!=IntPtr.Zero&&DwmGetWindowAttribute(handle,20,out int value,4)==0&&value==(dark?1:0);}
 static void ShowInvisible(Window window){window.ShowActivated=false;window.ShowInTaskbar=false;window.Opacity=0;window.Show();window.UpdateLayout();}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static IEnumerable<DependencyObject> Walk(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(root,i)))yield return child;}
 static void Layout(Window window){var content=(FrameworkElement)window.Content;content.Measure(new Size(window.Width,window.Height));content.Arrange(new Rect(0,0,window.Width,window.Height));content.UpdateLayout();}
 static void Snapshot(Window window,string file){Layout(window);var png=new PngBitmapEncoder();var bitmap=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);bitmap.Render((Visual)window.Content);png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(file);png.Save(output);}
 static bool DarkText(Window window){var fg=((SolidColorBrush)window.Foreground).Color;var bg=((SolidColorBrush)window.Background).Color;return fg.R+fg.G+fg.B<bg.R+bg.G+bg.B;}
 [STAThread]static int Main(string[] args){
  Application? application=null;Window? launcher=null;object? player=null;Type? playerType=null;
  try{
   string scratch=Path.GetFullPath(args[0]),hub=Path.Combine(scratch,"Hub");Directory.CreateDirectory(Path.Combine(hub,"Data","mini-player"));Directory.CreateDirectory(Path.Combine(hub,"Catalog"));File.WriteAllText(Path.Combine(hub,"Catalog","widgets.json"),"[]");
   var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"MiniPlayer.dll"));playerType=assembly.GetType("MiniPlayer.Player",true)!;var context=assembly.GetType("MiniPlayer.LaunchContext",true)!;
   context.GetProperty("LauncherRoot",Static)!.SetValue(null,hub);context.GetProperty("DataDirectory",Static)!.SetValue(null,Path.Combine(hub,"Data","mini-player"));
   string prefs=Path.Combine(hub,"Data","hub-settings.json");File.WriteAllText(prefs,"{\"LightMode\":false,\"AllowOffscreen\":true}");
   application=(Application)Activator.CreateInstance(assembly.GetType("MiniPlayer.PlayerApplication",true)!,true)!;player=Activator.CreateInstance(playerType)!;
   object? Field(string name)=>playerType.GetField(name,Instance)!.GetValue(player);
   void Call(string name,params object[] values)=>playerType.GetMethod(name,Instance)!.Invoke(player,values);
   ((System.Windows.Forms.NotifyIcon)Field("tray")!).Visible=false;
   Call("OpenSettings",true);Call("Customize",true);
   var settings=(Window)Field("settingsWindow")!;var editor=(Window)Field("customizeWindow")!;Layout(settings);Layout(editor);
   ShowInvisible(settings);ShowInvisible(editor);
   Check(!DarkText(settings)&&!DarkText(editor),"Both widget editors initially use saved night mode");
   var tabs=Walk((FrameworkElement)editor.Content).OfType<Button>();tabs.Single(x=>x.Content as string=="Размер и детали").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(editor);
   var scale=Walk((FrameworkElement)editor.Content).OfType<Slider>().Single(x=>System.Windows.Automation.AutomationProperties.GetName(x)=="Масштаб");scale.Value=1.15;
   var appearance=Field("appearance");Snapshot(editor,Path.Combine(scratch,"widget-details-night.png"));Snapshot(settings,Path.Combine(scratch,"widget-settings-night.png"));
   void CaptureTabs(string mode){
    foreach(var tab in new[]{("Темы","themes"),("Цвета","colors")}){
     Walk((FrameworkElement)editor.Content).OfType<Button>().Single(x=>x.Content as string==tab.Item1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Snapshot(editor,Path.Combine(scratch,"widget-"+tab.Item2+"-"+mode+".png"));
    }
    Walk((FrameworkElement)editor.Content).OfType<Button>().Single(x=>x.Content as string=="Размер и детали").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(editor);
   }
   CaptureTabs("night");
   File.WriteAllText(prefs,"{\"LightMode\":true,\"AllowOffscreen\":false}");Call("RefreshHubInterface",true);
   Check(DarkText(settings)&&DarkText(editor),"Open widget windows change to day mode together");
   Check(NativeMode(settings,false)&&NativeMode(editor,false),"Open native widget window frames change to day mode");
   Check(ReferenceEquals(settings,Field("settingsWindow"))&&ReferenceEquals(editor,Field("customizeWindow")),"Live mode change keeps existing editors");
   Check(Equals(appearance,Field("appearance"))&&Math.Abs(scale.Value-1.15)<.001,"Live mode change preserves unsaved appearance and slider state");
   Check(!(bool)playerType.GetProperty("HubAllowsOffscreen",Instance)!.GetValue(player)!,"Widget reads the shared offscreen setting");
   Snapshot(editor,Path.Combine(scratch,"widget-details-day.png"));Snapshot(settings,Path.Combine(scratch,"widget-settings-day.png"));
   CaptureTabs("day");
   File.WriteAllText(prefs,"{");Call("RefreshHubInterface",true);Check(DarkText(settings),"Partial settings write preserves the last valid mode");
   File.WriteAllText(prefs,"[]");Call("RefreshHubInterface",true);Check(DarkText(settings),"Invalid settings schema preserves the last valid mode");
   File.WriteAllText(prefs,"{\"LightMode\":true,\"AllowOffscreen\":false}");
   var launcherAssembly=Assembly.LoadFrom(Path.Combine(args[1],"Launcher.dll"));var launcherType=launcherAssembly.GetType("WidgetLauncher.LauncherWindow",true)!;
   launcher=(Window)Activator.CreateInstance(launcherType,new object[]{hub,true,"settings"})!;Layout(launcher);ShowInvisible(launcher);
   Check(DarkText(launcher),"Launcher restores saved day mode");Snapshot(launcher,Path.Combine(scratch,"launcher-settings-day.png"));
   var icon=(Button)launcherType.GetField("themeToggle",Instance)!.GetValue(launcher)!;icon.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Call("RefreshHubInterface",true);
   Check(!DarkText(launcher)&&!DarkText(settings)&&!DarkText(editor),"One launcher icon switches hub and open widget editors to night mode");
   Check(NativeMode(launcher,true)&&NativeMode(settings,true)&&NativeMode(editor,true),"All native window frames change to night mode");
   using(var saved=JsonDocument.Parse(File.ReadAllText(prefs)))Check(!saved.RootElement.GetProperty("LightMode").GetBoolean()&&!saved.RootElement.GetProperty("AllowOffscreen").GetBoolean(),"Theme toggle saves its choice and preserves the offscreen setting");
   Snapshot(launcher,Path.Combine(scratch,"launcher-settings-night.png"));
   icon.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Call("RefreshHubInterface",true);
   Check(DarkText(launcher)&&DarkText(settings)&&DarkText(editor)&&Equals(appearance,Field("appearance")),"Repeated switch returns all interfaces to day mode without changing widget appearance");
   editor.Close();Check(Field("customizeWindow")==null,"Appearance cancel remains functional after live theme switches");
   Console.WriteLine("TOTAL "+count+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  finally{try{launcher?.Close();}catch{}if(player!=null&&playerType!=null){try{playerType.GetField("closing",Instance)!.SetValue(player,true);((Window)player).Close();}catch{}try{((IDisposable)playerType.GetField("tray",Instance)!.GetValue(player)!).Dispose();}catch{}}application?.Shutdown();}
 }
}
