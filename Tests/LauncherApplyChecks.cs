using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
static class LauncherApplyChecks {
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int count;static void Check(bool result,string label){if(!result)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
 static void Await(Task task){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};int ticks=0;timer.Tick+=(_,_)=>{if(task.IsCompleted||++ticks>1500){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);if(!task.IsCompleted)throw new TimeoutException();task.GetAwaiter().GetResult();}
 static object? Call(object instance,string name,params object[] args)=>instance.GetType().GetMethod(name,Flags)!.Invoke(instance,args);
 static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
 [STAThread]static int Main(string[] args){Application? app=null;Window? launcher=null;object? manager=null,widget=null;try{
  string root=Path.GetFullPath(args[0]);using(var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"launcher-install.json"))))if(!marker.RootElement.GetProperty("TestInstallation").GetBoolean())throw new Exception("Only isolated test installations are allowed");
  var assembly=Assembly.LoadFrom(args[1]);var windowType=assembly.GetType("WidgetLauncher.LauncherWindow",true)!;var presetType=assembly.GetType("WidgetLauncher.WidgetPreset",true)!;
  app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());launcher=(Window)Activator.CreateInstance(windowType,new object[]{root,true,"customize"})!;
  manager=windowType.GetField("manager",Flags)!.GetValue(launcher)!;var catalog=(System.Collections.IEnumerable)Call(manager,"ReadCatalog")!;widget=catalog.Cast<object>().Single();
  object preset=JsonSerializer.Deserialize("{\"Name\":\"Actual launcher Apply fixture\",\"Width\":430,\"Height\":270,\"FontSize\":60,\"Shape\":\"hexagon\"}",presetType)!;
  windowType.GetField("presetDraft",Flags)!.SetValue(launcher,preset);
  Await((Task)Call(launcher,"ApplyCurrentPresetAsync")!);
  string active=Path.Combine(root,"Data","Customization","mini-player","active.json"),ackPath=Path.Combine(root,"Data","mini-player","preset-applied.json");
  using(var ack=JsonDocument.Parse(File.ReadAllText(ackPath)))Check(ack.RootElement.GetProperty("Success").GetBoolean()&&ack.RootElement.GetProperty("Sha256").GetString()!.Equals(Hash(active),StringComparison.OrdinalIgnoreCase),"Actual launcher Apply callback receives matching live-player success acknowledgement");
  Check(Directory.EnumerateFiles(Path.Combine(root,"Data","Customization","mini-player","Presets"),"*.json").Any(),"Apply also saves the user's preset");
  Await((Task)Call(launcher,"ApplyCurrentPresetAsync")!);string before=Hash(active);DateTime oldAck=File.GetLastWriteTimeUtc(ackPath);Thread.Sleep(30);Await((Task)Call(launcher,"ApplyCurrentPresetAsync")!);
  Check(Hash(active)==before&&File.GetLastWriteTimeUtc(ackPath)>oldAck,"Reapplying byte-identical preset waits for a fresh acknowledgement");
  string library=Path.Combine(Path.GetDirectoryName(active)!,"Presets",(string)presetType.GetProperty("Id")!.GetValue(preset)!+".json");
  string savedHash=Hash(library);
  string unavailable="Media/"+Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N")+".png";
  presetType.GetProperty("MediaPath")!.SetValue(preset,unavailable);bool failed=false;try{Await((Task)Call(launcher,"ApplyCurrentPresetAsync")!);}catch(IOException){failed=true;}
  Check(failed&&Hash(active)==before,"Actual launcher callback restores previous active preset when media cannot be applied");
  Check((bool)Call(manager,"IsRunning",widget)!,"Failed apply leaves the previous player running");
  Check(Hash(library)==savedHash,"Missing background preserves the existing library entry byte for byte");
  string brokenImage=Path.Combine(Path.GetDirectoryName(active)!,unavailable);
  Directory.CreateDirectory(Path.GetDirectoryName(brokenImage)!);
  File.WriteAllText(brokenImage,"fixture: unreadable PNG");
  try{
   failed=false;try{Await((Task)Call(launcher,"ApplyCurrentPresetAsync")!);}catch(IOException){failed=true;}
   Check(failed&&Hash(active)==before&&Hash(library)==savedHash,"Player-side decoding failure rolls back the active file and leaves the library intact");
  }finally{File.Delete(brokenImage);}

  var storeType=assembly.GetType("WidgetLauncher.PresetStore",true)!;
  var exchange=assembly.GetType("WidgetLauncher.PresetExchange",true)!;
  string sender=Path.Combine(root,"Data","mini-player","sharing-test-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(sender);
  string png=Path.Combine(sender,"background.png");
  var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
  encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[]{0,40,200,255},4)));
  using(var image=File.Create(png))encoder.Save(image);
  var senderStore=Activator.CreateInstance(storeType,new[]{sender,"mini-player"})!;
  string mediaPath=(string)Call(senderStore,"ImportMedia",png)!;
  presetType.GetProperty("MediaPath")!.SetValue(preset,mediaPath);
  string code=(string)exchange.GetMethod("ExportCode")!.Invoke(null,new[]{senderStore,preset})!;
  var receiverStore=Activator.CreateInstance(storeType,new[]{root,"mini-player"})!;
  var received=exchange.GetMethod("ImportCode")!.Invoke(null,new object[]{receiverStore,code})!;
  windowType.GetField("presetDraft",Flags)!.SetValue(launcher,received);
  Await((Task)Call(launcher,"ApplyCurrentPresetAsync")!);
  using(var ack=JsonDocument.Parse(File.ReadAllText(ackPath)))
   Check(ack.RootElement.GetProperty("Success").GetBoolean() && ack.RootElement.GetProperty("Sha256").GetString()!.Equals(Hash(active),StringComparison.OrdinalIgnoreCase) && Hash((string)Call(receiverStore,"ResolveMedia",mediaPath)!)==Hash(png),"Preset shared from a separate library with embedded PNG applies to the installed running player");
  Check(Hash(library)==savedHash,"Applying the imported copy preserves the original library entry");
  Console.WriteLine("TOTAL "+count+" PASS");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{if(manager!=null&&widget!=null)try{Await((Task)Call(manager,"StopAsync",widget)!);}catch{}try{launcher?.Close();}catch{}app?.Shutdown();}}
}
