using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using WidgetLauncher;
class ObsChecks{
 static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);}
 static async Task<int> Main(string[] args){try{
 var root=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.Combine(root,"Catalog"));
 var widgets=new[]{new Widget{Id="notes",Available=true,Name="Notes",Version="1",Package="notes.zip",Executable="Notes.exe",ObsProtocol=1},new Widget{Id="future",Available=true,Name="Future",Version="1",Package="future.zip",Executable="Future.exe",ObsProtocol=1}};
 File.WriteAllText(Path.Combine(root,"Catalog","widgets.json"),JsonSerializer.Serialize(widgets));var manager=new WidgetManager(root);
 foreach(var w in widgets){Directory.CreateDirectory(manager.WidgetDirectory(w));File.WriteAllText(manager.Executable(w),"");File.WriteAllText(Path.Combine(manager.WidgetDirectory(w),".widget-install.json"),JsonSerializer.Serialize(w));Directory.CreateDirectory(manager.DataDirectory(w));}
 var noteFile=Path.Combine(root,"Data","notes","notes.json");File.WriteAllText(noteFile,"[{\"id\":\"public\",\"text\":\"Live note\"},{\"id\":\"private\",\"text\":\"SECRET\"}]");
 string url;
 using(var bridge=new ObsBridge(manager)){
  var port=new TcpListener(IPAddress.Loopback,0);port.Start();bridge.Settings.Port=((IPEndPoint)port.LocalEndpoint).Port;port.Stop();bridge.Entry("notes").NoteId="public";bridge.Enable(widgets[0],true);url=bridge.Url("notes");if(args.Length>1){File.WriteAllText(args[1],url);await Task.Delay(30000);}
  using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(5)};
  var html=await client.GetStringAsync(url);Check(html.Contains("textContent")&&!html.Contains("SECRET"),"Browser page uses safe text rendering and contains no private notes");
  var state=await client.GetStringAsync(url.Replace("/overlay/","/state/"));Check(state.Contains("Live note")&&!state.Contains("SECRET"),"Only explicitly selected note is served");
  File.WriteAllText(noteFile,"[{\"id\":\"public\",\"text\":\"Changed\"}]");Check((await client.GetStringAsync(url.Replace("/overlay/","/state/"))).Contains("Changed"),"Note changes reach HTTP endpoint");
  Check((await client.GetAsync(url.Replace(bridge.Entry("notes").Token,"wrong"))).StatusCode==HttpStatusCode.NotFound,"Unknown token is refused");
  Check((await client.PostAsync(url,new StringContent("ignored"))).StatusCode==HttpStatusCode.NotFound,"Write requests are refused");
  using(var request=new HttpRequestMessage(HttpMethod.Get,url)){request.Headers.Host="example.com";Check((await client.SendAsync(request)).StatusCode==HttpStatusCode.NotFound,"Foreign Host is refused");}
  bridge.Enable(widgets[1],true);string file=Path.Combine(root,"Data","future","obs.json");File.WriteAllText(file,JsonSerializer.Serialize(new ObsState(Title:"Future widget",Image:"https://evil.invalid/a",Progress:2,UpdatedAt:DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
  var next=bridge.State("future");Check(next.Title=="Future widget"&&next.Image==""&&next.Progress==1,"New widget uses protocol without launcher changes; external images rejected");
  File.WriteAllText(file,JsonSerializer.Serialize(new ObsState(Title:"stale",UpdatedAt:1)));Check(bridge.State("future").Title=="","Stale state hides stopped widget");
  bridge.Enable(widgets[0],false);Check((await client.GetAsync(url)).StatusCode==HttpStatusCode.NotFound,"Disabling immediately revokes source");bridge.Enable(widgets[0],true);
 }
 using(var restored=new ObsBridge(manager)){Check(restored.Url("notes")==url&&restored.Entry("notes").NoteId=="public","Links and note selection survive restart");restored.Start();using var client=new HttpClient(new HttpClientHandler{UseProxy=false});Check((await client.GetAsync(url)).IsSuccessStatusCode,"Same source works after service restart");}
 return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
