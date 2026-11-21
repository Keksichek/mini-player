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
using System.Windows.Threading;

static class LauncherCloseChecks
{
 const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static Type type=null!;static string fixture="";static int checks;static Application app=null!;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static object? Field(object owner,string name)=>type.GetField(name,Members)!.GetValue(owner);
 static object? Call(object owner,string name,params object[] args)=>type.GetMethod(name,Members)!.Invoke(owner,args);
 static IEnumerable<DependencyObject> Walk(DependencyObject node){yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,i)))yield return child;}
 static Button Button(Window window,string name)=>Walk((DependencyObject)window.Content).OfType<Button>().Single(x=>x.Content as string==name||System.Windows.Automation.AutomationProperties.GetName(x)==name);
 static string PreferencesFile=>Path.Combine(fixture,"Data","hub-settings.json");
 static int SavedMode()=>JsonDocument.Parse(File.ReadAllText(PreferencesFile)).RootElement.GetProperty("CloseMode").GetInt32();
 static void PumpUntil(Func<bool> done){
  var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};int ticks=0;
  timer.Tick+=(_,_)=>{if(done()||++ticks>400){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);if(!done())throw new TimeoutException("Close flow did not finish.");
 }
 static Window NewWindow(int mode,bool light=false){
  File.WriteAllText(PreferencesFile,JsonSerializer.Serialize(new{CloseMode=mode,LightMode=light,AllowOffscreen=false,AutoStartMode=0,SelectedWidgets=new[]{"mini-player"}}));
  var window=(Window)Activator.CreateInstance(type,new object[]{fixture,false,"settings"})!;window.ShowActivated=false;window.ShowInTaskbar=false;window.Opacity=0;window.Show();window.UpdateLayout();return window;
 }
 static void Snapshot(Window dialog,string file){var content=(FrameworkElement)dialog.Content;content.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.Combine(fixture,file));png.Save(output);}
 static void Prompt(Window window,Action<Window> action){
  bool completed=false;Exception? error=null;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
  timer.Tick+=(_,_)=>{
   if(Field(window,"closeChoiceWindow") is not Window dialog)return;
   timer.Stop();try{dialog.Opacity=0;dialog.UpdateLayout();action(dialog);}catch(Exception ex){error=ex;dialog.Close();}finally{completed=true;}
  };
  timer.Start();window.Close();PumpUntil(()=>completed&&Field(window,"closeChoiceWindow")==null);timer.Stop();if(error!=null)throw error;
 }
 static void Choose(Window dialog,string action,bool remember){Walk((DependencyObject)dialog.Content).OfType<CheckBox>().Single().IsChecked=remember;Button(dialog,action).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));}
 [STAThread]static int Main(string[] args){
  var windows=new List<Window>();
  try{
   fixture=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.Combine(fixture,"Data"));Directory.CreateDirectory(Path.Combine(fixture,"Catalog"));
   File.WriteAllText(Path.Combine(fixture,"Catalog","widgets.json"),"[]");
   string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.TrimEnd('\\').ToUpperInvariant())))[..16];
   File.WriteAllText(Path.Combine(fixture,"launcher-install.json"),JsonSerializer.Serialize(new{ProductId="MiniPlayer.WidgetLauncher",InstallPath=fixture,InstallId=hash,TestInstallation=true,Files=Array.Empty<string>(),WidgetIds=Array.Empty<string>()}));
   var assembly=Assembly.LoadFrom(args[1]);type=assembly.GetType("WidgetLauncher.LauncherWindow",true)!;
   app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
   EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{if(sender is Window w&&w.Title.Contains("Закрытие")){w.Opacity=0;w.ShowInTaskbar=false;}}));
   var window=NewWindow(0);windows.Add(window);
   Prompt(window,dialog=>{Snapshot(dialog,"close-night.png");window.Close();Check(app.Windows.OfType<Window>().Count(x=>x.Title.Contains("Закрытие"))==1,"Repeated close keeps a single confirmation");Button(dialog,"Отмена закрытия").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));});
   Check(window.IsVisible&&SavedMode()==0,"Cancel keeps the launcher open without changing preferences");
   Prompt(window,dialog=>Choose(dialog,"В трей",false));
   Check(!window.IsVisible&&Field(window,"launcherTray") is System.Windows.Forms.NotifyIcon icon&&icon.Visible&&SavedMode()==0,"Tray without remembering hides the launcher and keeps asking enabled");
   Call(window,"BringForward");Check(window.IsVisible&&!((System.Windows.Forms.NotifyIcon)Field(window,"launcherTray")!).Visible,"Restore shows the same launcher and hides its tray icon");
   Prompt(window,dialog=>Choose(dialog,"В трей",true));Check(SavedMode()==1,"Remembering tray choice is persisted");
   Call(window,"BringForward");window.Close();PumpUntil(()=>!window.IsVisible);
   Check(Field(window,"closeChoiceWindow")==null&&Field(window,"launcherTray") is System.Windows.Forms.NotifyIcon,"Remembered tray choice bypasses the question");
   Call(window,"RequestShutdown");Check(Field(window,"launcherTray")==null&&!window.IsVisible,"External shutdown exits a hidden launcher and disposes its tray");
   window=NewWindow(1);windows.Add(window);
   Button(window,"Спрашивать каждый раз").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Check(SavedMode()==0,"Main settings restore the close question");
   Prompt(window,dialog=>Choose(dialog,"Закрыть полностью",false));Check(!window.IsVisible&&SavedMode()==0,"Exit without remembering terminates launcher without persisting a choice");
   window=NewWindow(0,true);windows.Add(window);
   Prompt(window,dialog=>{Snapshot(dialog,"close-day.png");Choose(dialog,"Закрыть полностью",true);});
   Check(!window.IsVisible&&SavedMode()==2,"Remembering full exit persists its choice");
   using(var data=JsonDocument.Parse(File.ReadAllText(PreferencesFile)))Check(data.RootElement.GetProperty("LightMode").GetBoolean()&&!data.RootElement.GetProperty("AllowOffscreen").GetBoolean()&&data.RootElement.GetProperty("SelectedWidgets")[0].GetString()=="mini-player","Saving close choice preserves other hub settings");
   window=NewWindow(2);windows.Add(window);window.Close();Check(!window.IsVisible&&Field(window,"closeChoiceWindow")==null,"Persisted full exit closes without a prompt");
   window=NewWindow(0);windows.Add(window);Call(window,"RequestShutdown");Check(!window.IsVisible&&Field(window,"closeChoiceWindow")==null,"Stop command bypasses the close prompt");
   window=NewWindow(0);windows.Add(window);Prompt(window,dialog=>Call(window,"RequestShutdown"));Check(!window.IsVisible,"Stop command can close an already open confirmation");
   window=NewWindow(0);windows.Add(window);
   File.SetAttributes(PreferencesFile,FileAttributes.ReadOnly);
   try{Prompt(window,dialog=>{Choose(dialog,"В трей",true);Check(dialog.IsVisible&&Walk((DependencyObject)dialog.Content).OfType<TextBlock>().Any(x=>x.Text.StartsWith("Не удалось запомнить")),"Save failure stays in the dialog with a visible explanation");Choose(dialog,"Закрыть полностью",false);});}
   finally{File.SetAttributes(PreferencesFile,FileAttributes.Normal);}
   Check(!window.IsVisible&&SavedMode()==0,"A one-time choice still works when settings cannot be saved");
   Console.WriteLine("TOTAL "+checks+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  finally{foreach(var window in windows)try{Call(window,"RequestShutdown");}catch{}app?.Shutdown();}
 }
}
