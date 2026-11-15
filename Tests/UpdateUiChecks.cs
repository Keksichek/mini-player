using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class UpdateUiChecks
{
 const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int checks;static Type type=null!;static Window window=null!;static string root="",feed="",key="";
 static object? Field(string name)=>type.GetField(name,Members)!.GetValue(window);
 static void Set(string name,object? value)=>type.GetField(name,Members)!.SetValue(window,value);
 static object? Call(string name,params object[] args)=>type.GetMethods(Members|BindingFlags.DeclaredOnly).Single(method=>method.Name==name&&method.GetParameters().Length==args.Length).Invoke(window,args);
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static IEnumerable<DependencyObject> Walk(DependencyObject node){yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var item in Walk(VisualTreeHelper.GetChild(node,i)))yield return item;}
 static IEnumerable<Button> Buttons(){var surface=(FrameworkElement)window.Content;surface.Measure(new Size(window.Width,window.Height));surface.Arrange(new Rect(0,0,window.Width,window.Height));surface.UpdateLayout();return Walk(surface).OfType<Button>();}
 static bool HasUpdate()=>Buttons().Any(button=>button.Content as string=="Обновить");
 static void PumpUntil(Func<bool> completed){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};int ticks=0;timer.Tick+=(_,_)=>{if(completed()||++ticks>600){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);if(!completed())throw new TimeoutException("UI operation timed out.");}
 static void Complete(Task task){PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
 static void WriteFeed(long build,string notes="Исправления и локальная проверка."){
  Directory.CreateDirectory(feed);string zip=Path.Combine(feed,"payload.zip");if(File.Exists(zip))File.Delete(zip);
  using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){using var output=new StreamWriter(archive.CreateEntry("sample.txt").Open());output.Write("Local update UI fixture");}
  var bytes=JsonSerializer.SerializeToUtf8Bytes(new{ProductId="MiniPlayer.WidgetLauncher",Channel="secondary-local",BuildNumber=build,Version="0.19999999999999999999999999999999",Package="payload.zip",Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant(),Size=new FileInfo(zip).Length,Notes=notes});
  File.WriteAllBytes(Path.Combine(feed,"update.json"),bytes);using var rsa=RSA.Create();rsa.FromXmlString(File.ReadAllText(key));File.WriteAllText(Path.Combine(feed,"update.sig"),Convert.ToBase64String(rsa.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)));
 }
 static void State(long build)=>File.WriteAllText(Path.Combine(root,"update-state.json"),JsonSerializer.Serialize(new{BuildNumber=build,Version="0.1"}));
 static void CheckFeed(){Complete((Task)Call("CheckForUpdatesAsync")!);Call("Render");}
 static void Screenshot(string file){Buttons().ToArray();var image=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);image.Render((Visual)window.Content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(file);png.Save(output);}
 [STAThread]static int Main(string[] args){
  Application? application=null;
  try{
   string scratch=Path.GetFullPath(args[0]);root=Path.Combine(scratch,"Hub");feed=Path.Combine(scratch,"Пакет обновления");key=Path.GetFullPath(args[2]);
   Directory.CreateDirectory(Path.Combine(root,"Data"));Directory.CreateDirectory(Path.Combine(root,"Catalog"));File.WriteAllText(Path.Combine(root,"Catalog","widgets.json"),"[]");
   string identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.TrimEnd('\\').ToUpperInvariant())))[..16];
   File.WriteAllText(Path.Combine(root,"launcher-install.json"),JsonSerializer.Serialize(new{ProductId="MiniPlayer.WidgetLauncher",InstallPath=root,InstallId=identity,TestInstallation=true,Files=Array.Empty<string>(),WidgetIds=Array.Empty<string>()}));
   File.WriteAllText(Path.Combine(root,"Data","hub-settings.json"),"{\"CloseMode\":2}");File.Copy(args[3],Path.Combine(root,"Update.exe"),true);
   application=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var assembly=Assembly.LoadFrom(args[1]);type=assembly.GetType("WidgetLauncher.LauncherWindow",true)!;
   window=(Window)Activator.CreateInstance(type,new object[]{root,false,"settings"})!;
   Check(!HasUpdate()&&!Buttons().Single(button=>button.Content as string=="Проверить обновления").IsEnabled,"Empty source offers folder selection without an Update button");
   var preferences=Field("preferences")!;preferences.GetType().GetProperty("UpdateFeedDirectory")!.SetValue(preferences,feed);
   State(7);WriteFeed(8);CheckFeed();Check(HasUpdate()&&Field("checkedUpdate")!=null,"Trusted newer package enables Update");
   Check((string)Field("updateMessage")! is string found&&found.Contains("0.19999999999999999999999999999999")&&found.Contains("8"),"Update identifies the available long version and build");Screenshot(Path.Combine(scratch,"updates-night.png"));
   preferences.GetType().GetProperty("LightMode")!.SetValue(preferences,true);Call("ApplyHubTheme");Call("Render");Screenshot(Path.Combine(scratch,"updates-day.png"));
   Check(HasUpdate()&&((SolidColorBrush)window.Background).Color.R>200,"Day mode keeps the verified update visible");
   WriteFeed(7);CheckFeed();Check(!HasUpdate()&&((string)Field("updateMessage")!).Contains("Новых обновлений нет"),"Equal build offers no update");
   WriteFeed(6);CheckFeed();Check(!HasUpdate(),"Older signed build offers no downgrade");
   File.Delete(Path.Combine(root,"update-state.json"));WriteFeed(1);CheckFeed();Check(HasUpdate(),"Existing installs without update state can receive their first update");
   File.AppendAllText(Path.Combine(feed,"update.json")," ");CheckFeed();Check(!HasUpdate()&&Field("checkedUpdate")==null,"Changed manifest signature clears a previously offered update");
   WriteFeed(2);File.AppendAllText(Path.Combine(feed,"payload.zip"),"corrupted");CheckFeed();Check(!HasUpdate(),"Corrupted package offers no Update action");
   WriteFeed(2);File.WriteAllText(Path.Combine(root,"update-state.json"),"{invalid");CheckFeed();Check(!HasUpdate(),"Corrupt installed state prevents update eligibility");
   State(1);preferences.GetType().GetProperty("UpdateFeedDirectory")!.SetValue(preferences,"https://example.invalid/update");CheckFeed();Check(!HasUpdate(),"URL is rejected by local-only checking");
   preferences.GetType().GetProperty("UpdateFeedDirectory")!.SetValue(preferences,feed);WriteFeed(2);CheckFeed();WriteFeed(3);
   Call("RequestCheckedUpdate");PumpUntil(()=>!(bool)Field("busy")!);Check(!File.Exists(Path.Combine(root,"launches.txt"))&&Field("checkedUpdate")==null,"Changed feed is rechecked before helper launch");
   CheckFeed();Call("RequestCheckedUpdate");Call("RequestCheckedUpdate");PumpUntil(()=>(bool)Field("updatePending")!);Call("RequestCheckedUpdate");
   Check(Buttons().Where(button=>button.Content as string=="Обновить"||button.Content as string=="Проверить обновления").All(button=>!button.IsEnabled),"Preparing update disables repeated actions");
   PumpUntil(()=>!(bool)Field("updatePending")!);Check(File.ReadAllLines(Path.Combine(root,"launches.txt")).Length==1,"Repeated update requests launch only one helper");
   Check(!HasUpdate()&&((string)Field("updateMessage")!).Contains("Не удалось"),"Bootstrap failure restores controls and explains the failure");
   Console.WriteLine("TOTAL "+checks+" PASS");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  finally{if(window!=null)try{Call("RequestShutdown");}catch{}application?.Shutdown();}
 }
}
