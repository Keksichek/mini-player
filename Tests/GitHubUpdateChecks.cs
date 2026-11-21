using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WidgetLauncher;
using VibeWidgetsUpdates;

static class GitHubUpdateChecks
{
 const string Asset="https://github.com/Keksichek/mini-player/releases/download/test/";
 const System.Reflection.BindingFlags Instance=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
 static string root="",key="";static byte[] bytes=Encoding.UTF8.GetBytes("own signed update package");static int checks;
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 sealed class Network:HttpMessageHandler{
  public readonly List<string> Requests=new();public Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>>? Handler;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests.Add(request.RequestUri!.AbsoluteUri);if(request.Headers.Authorization!=null)throw new Exception("Unexpected authentication");return Handler!(request,token);}
 }
 static HttpResponseMessage Body(byte[] body)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(body)};
 static byte[] Manifest(long build=10)=>JsonSerializer.SerializeToUtf8Bytes(new{ProductId=UpdateProtocol.ProductId,Channel=UpdateProtocol.Channel,BuildNumber=build,Version="0.2",Package="payload.zip",Sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),Size=bytes.Length,Notes="Own fixture"});
 static byte[] Sign(byte[] metadata){using var rsa=RSA.Create();rsa.FromXmlString(File.ReadAllText(key));return Encoding.UTF8.GetBytes(Convert.ToBase64String(rsa.SignData(metadata,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)));}
 static byte[] Releases(string branch="keksichek/launcher",bool draft=false,bool assets=true,string host=Asset)=>JsonSerializer.SerializeToUtf8Bytes(new[]{new{draft,target_commitish=branch,published_at="2026-11-16T00:00:00Z",assets=assets?new[]{"update.json","update.sig","payload.zip"}.Select(n=>new{name=n,state="uploaded",browser_download_url=host+n}).ToArray():Array.Empty<object>()}});
 static Network Fixture(byte[]? releases=null,byte[]? manifest=null,byte[]? payload=null){
  byte[] metadata=manifest??Manifest();var network=new Network();network.Handler=(request,_)=>Task.FromResult(Body(request.RequestUri!.Host=="api.github.com"?releases??Releases():request.RequestUri.AbsolutePath.EndsWith("update.json")?metadata:request.RequestUri.AbsolutePath.EndsWith("update.sig")?Sign(metadata):payload??bytes));return network;
 }
 static GitHubUpdateSource Source(Network network)=>new(Path.Combine(root,"cache-"+Guid.NewGuid().ToString("N")),new HttpClient(network));
 static async Task Reject(Func<Task> action,string message){bool rejected=false;try{await action();}catch(Exception ex)when(ex is IOException or InvalidDataException or OperationCanceledException){rejected=true;}Check(rejected,message);}
 static async Task ClientChecks(){
  using(var net=Fixture())using(var source=Source(net)){
   var found=await source.FindAsync(0,CancellationToken.None);Check(found!=null&&found.Manifest.Version=="0.2"&&net.Requests.Count==3&&!File.Exists(Path.Combine(found.FeedDirectory,"payload.zip")),"Checking verifies the signed description without downloading the package");
   await source.DownloadAsync(found!,null,CancellationToken.None);Check(File.ReadAllBytes(Path.Combine(found!.FeedDirectory,"payload.zip")).SequenceEqual(bytes),"Downloading verifies the signed package bytes");
   Check(net.Requests.All(x=>x.StartsWith("https://")),"All requests use HTTPS");
   Check(await source.FindAsync(10,CancellationToken.None)==null,"Installed and older signed builds are not offered");
  }
  foreach(var value in new[]{Releases("main"),Releases("secondary"),Releases(draft:true),Releases(assets:false)})using(var net=Fixture(value))using(var source=Source(net))Check(await source.FindAsync(0,CancellationToken.None)==null&&net.Requests.Count==1,"Main, draft and installer-only releases are ignored");
  using(var net=Fixture(Releases(host:"https://example.invalid/")))using(var source=Source(net))await Reject(async()=>{await source.FindAsync(0,CancellationToken.None);},"Foreign release asset addresses are rejected");
  using(var net=Fixture())using(var source=Source(net)){
   var original=net.Handler!;net.Handler=(r,t)=>r.RequestUri!.AbsolutePath.EndsWith("update.sig")?Task.FromResult(Body(Encoding.UTF8.GetBytes("broken"))):original(r,t);
   await Reject(async()=>{await source.FindAsync(0,CancellationToken.None);},"Invalid signatures cannot enable an update");
  }
  using(var net=Fixture(payload:Enumerable.Repeat((byte)1,bytes.Length).ToArray()))using(var source=Source(net)){
   var found=(await source.FindAsync(0,CancellationToken.None))!;await Reject(()=>source.DownloadAsync(found,null,CancellationToken.None),"A mismatched checksum is rejected");Check(!Directory.EnumerateFiles(found.FeedDirectory,"*.zip*").Any(),"A corrupt download leaves no installable or partial package");
  }
  using(var net=Fixture(payload:new byte[1]))using(var source=Source(net)){var found=(await source.FindAsync(0,CancellationToken.None))!;await Reject(()=>source.DownloadAsync(found,null,CancellationToken.None),"An incomplete response cannot be installed");}
  using(var net=Fixture())using(var source=Source(net)){
   var original=net.Handler!;net.Handler=(r,t)=>r.RequestUri!.Host=="api.github.com"?Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)):original(r,t);
   await Reject(async()=>{await source.FindAsync(0,CancellationToken.None);},"Rate limits report a bounded failure without retry loops");Check(net.Requests.Count==1,"Rate-limited checks do not hammer the API");
  }
  using(var net=Fixture())using(var source=Source(net)){
   var found=(await source.FindAsync(0,CancellationToken.None))!;net.Handler=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found){Headers={Location=new Uri("http://127.0.0.1/payload.zip")}});
   await Reject(()=>source.DownloadAsync(found,null,CancellationToken.None),"Redirects to HTTP or a private host are rejected");
  }
  using(var net=Fixture())using(var source=Source(net)){
   var found=(await source.FindAsync(0,CancellationToken.None))!;net.Handler=(r,_)=>Task.FromResult(r.RequestUri!.Host=="github.com"?new HttpResponseMessage(HttpStatusCode.Found){Headers={Location=new Uri("https://release-assets.githubusercontent.com/own-fixture")}}:Body(bytes));
   await source.DownloadAsync(found,null,CancellationToken.None);Check(File.Exists(Path.Combine(found.FeedDirectory,"payload.zip")),"GitHub release CDN redirects are supported");
  }
  using(var net=Fixture())using(var source=Source(net)){
   var found=(await source.FindAsync(0,CancellationToken.None))!;net.Handler=async(_,token)=>{await Task.Delay(Timeout.Infinite,token);return Body(bytes);};using var cancel=new CancellationTokenSource(25);
   await Reject(()=>source.DownloadAsync(found,null,cancel.Token),"Downloads can be cancelled before the helper runs");Check(!Directory.EnumerateFiles(found.FeedDirectory,"*.zip*").Any(),"Cancelled downloads leave no package");
  }
 }
 static object? Field(Window w,string name)=>w.GetType().GetField(name,Instance)!.GetValue(w);
 static void Set(Window w,string name,object value)=>w.GetType().GetField(name,Instance)!.SetValue(w,value);
 static object? Call(Window w,string name,params object[] args)=>w.GetType().GetMethod(name,Instance)!.Invoke(w,args);
 static void PumpUntil(Func<bool> done){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};int count=0;timer.Tick+=(_,_)=>{if(done()||++count>500){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);if(!done())throw new TimeoutException();}
 static void Complete(Task task){PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
 static void AutoChecks(string probe){
  string folder=Path.Combine(root,"Hub");Directory.CreateDirectory(Path.Combine(folder,"Catalog"));Directory.CreateDirectory(Path.Combine(folder,"Data"));File.WriteAllText(Path.Combine(folder,"Catalog","widgets.json"),"[]");
  File.WriteAllText(Path.Combine(folder,"launcher-install.json"),JsonSerializer.Serialize(new{ProductId="MiniPlayer.WidgetLauncher",InstallPath=folder,InstallId=Autostart.RootHash(folder),TestInstallation=true}));File.Copy(probe,Path.Combine(folder,"Update.exe"));
  var window=new LauncherWindow(folder,false,"settings");var preferences=(HubPreferences)Field(window,"preferences")!;var net=Fixture();var source=Source(net);Set(window,"gitHubUpdates",source);
  try{
   Complete((Task)Call(window,"RunAutomaticUpdateAsync")!);Check(net.Requests.Count==0,"Disabled automatic updates make no network calls");
   preferences.AutoUpdateEnabled=true;preferences.UpdateSourceMode=1;Complete((Task)Call(window,"RunAutomaticUpdateAsync")!);Check(net.Requests.Count==0,"Local mode never starts a GitHub auto-update");
   preferences.UpdateSourceMode=0;Set(window,"busy",true);Complete((Task)Call(window,"RunAutomaticUpdateAsync")!);Check(net.Requests.Count==0,"Automatic updates wait while another operation is busy");Set(window,"busy",false);
   Complete((Task)Call(window,"RunAutomaticUpdateAsync")!);PumpUntil(()=>!(bool)Field(window,"updatePending")!);
   Check(File.ReadAllLines(Path.Combine(folder,"launches.txt")).Length==1&&HubPreferences.Load(folder).AutoUpdateAttemptBuild==10,"An enabled auto-update downloads a verified package and records the attempted build");
   Complete((Task)Call(window,"RunAutomaticUpdateAsync")!);Check(File.ReadAllLines(Path.Combine(folder,"launches.txt")).Length==1,"A failed build is not automatically installed in a restart loop");
   preferences.AutoUpdateAttemptBuild=0;net.Handler=async(r,token)=>{if(r.RequestUri!.AbsolutePath.EndsWith("payload.zip")){await Task.Delay(Timeout.Infinite,token);}return Body(r.RequestUri.Host=="api.github.com"?Releases():r.RequestUri.AbsolutePath.EndsWith("update.json")?Manifest():Sign(Manifest()));};
   Task operation=(Task)Call(window,"RunAutomaticUpdateAsync")!;PumpUntil(()=>Field(window,"networkUpdateCancellation")!=null&&net.Requests.Last().EndsWith("payload.zip"));preferences.AutoUpdateEnabled=false;((CancellationTokenSource)Field(window,"networkUpdateCancellation")!).Cancel();Complete(operation);
   Check(File.ReadAllLines(Path.Combine(folder,"launches.txt")).Length==1&&!(bool)Field(window,"busy")!,"Turning automatic updates off cancels downloading without starting the helper");
  }finally{window.RequestShutdown();source.Dispose();net.Dispose();}
 }
 [STAThread]static int Main(string[] args){Application? application=null;try{
  if(args[0]=="--live"){using var source=new GitHubUpdateSource(Path.GetFullPath(args[1]));var found=source.FindAsync(0,new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token).GetAwaiter().GetResult();Console.WriteLine(found==null?"GitHub read-only check: connected; no signed keksichek/launcher update is published yet.":"GitHub read-only check: found verified build "+found.Manifest.BuildNumber);return 0;}
  root=Path.GetFullPath(args[0]);key=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);application=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
  Complete(ClientChecks());AutoChecks(args[2]);Console.WriteLine("TOTAL "+checks+" PASS");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{application?.Shutdown();}}
}
