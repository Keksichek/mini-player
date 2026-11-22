using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetLauncher;

public sealed record ObsState(int Version=1,string Title="",string Subtitle="",string Image="",double Progress=0,long UpdatedAt=0,double Position=0,double Duration=0);
public sealed class ObsEntry { public bool Enabled{get;set;} public string Token{get;set;}=Guid.NewGuid().ToString("N"); public string NoteId{get;set;}=""; }
public sealed class ObsPreferences { public int Port{get;set;}=18765; public Dictionary<string,ObsEntry> Widgets{get;set;}=new(); }

public sealed class ObsBridge:IDisposable
{
 readonly WidgetManager manager;readonly object gate=new();readonly SemaphoreSlim slots=new(8);readonly CancellationTokenSource cancel=new();
 TcpListener? listener;
 public ObsPreferences Settings{get;}
 string SettingsPath=>Path.Combine(manager.Root,"Data","obs-settings.json");
 public bool Running=>listener!=null;
 public ObsBridge(WidgetManager manager){this.manager=manager;try{Settings=JsonSerializer.Deserialize<ObsPreferences>(File.ReadAllText(SettingsPath))??new();}catch{Settings=new();}if(Settings.Port<1024||Settings.Port>65535)Settings.Port=18765;}
 public ObsEntry Entry(string id){lock(gate){if(!Settings.Widgets.TryGetValue(id,out var e))Settings.Widgets[id]=e=new();return e;}}
 public void Save(){lock(gate){WidgetManager.RejectLinks(Path.GetDirectoryName(SettingsPath)!);Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);File.WriteAllText(SettingsPath+".tmp",JsonSerializer.Serialize(Settings));File.Move(SettingsPath+".tmp",SettingsPath,true);}}
 public string Url(string id)=>$"http://127.0.0.1:{Settings.Port}/overlay/{Entry(id).Token}/{id}";
 public void Start(){if(listener!=null)return;var next=new TcpListener(IPAddress.Loopback,Settings.Port);next.Start(16);listener=next;_=Accept(next);}
 async Task Accept(TcpListener server){try{while(!cancel.IsCancellationRequested){var client=await server.AcceptTcpClientAsync(cancel.Token);if(!slots.Wait(0)){client.Dispose();continue;}_=Serve(client);}}catch(OperationCanceledException){}catch(SocketException){}catch(ObjectDisposedException){}}
 public void Enable(Widget widget,bool enabled){
  if(enabled)Start();var entry=Entry(widget.Id);bool previous=entry.Enabled;entry.Enabled=enabled;
  try{var folder=manager.DataDirectory(widget);WidgetManager.RejectLinks(folder);Directory.CreateDirectory(folder);var marker=Path.Combine(folder,".obs-enabled");if(enabled)File.WriteAllText(marker,"1");else if(File.Exists(marker))File.Delete(marker);Save();if(!Settings.Widgets.Values.Any(x=>x.Enabled)){listener?.Stop();listener=null;}}catch{entry.Enabled=previous;throw;}
 }
 public List<(string Id,string Text)> Notes(){
  var result=new List<(string,string)>();try{var file=Path.Combine(manager.Root,"Data","notes","notes.json");WidgetManager.RejectLinks(file);if(new FileInfo(file).Length>8*1024*1024)return result;using var doc=JsonDocument.Parse(File.ReadAllText(file));
   void Add(JsonElement e){if(e.TryGetProperty("id",out var id)&&e.TryGetProperty("text",out var text))result.Add((id.GetString()??"",text.GetString()??""));if(e.TryGetProperty("entries",out var children)&&children.ValueKind==JsonValueKind.Array)foreach(var child in children.EnumerateArray())Add(child);}
   foreach(var e in doc.RootElement.EnumerateArray())Add(e);
  }catch{}return result;
 }
 public ObsState State(string id){
  var widget=manager.ReadCatalog().FirstOrDefault(w=>w.Id==id&&w.Available);if(widget==null||!manager.IsInstalled(widget)||!Entry(id).Enabled)return new();
  if(id=="notes"){var selected=Notes().FirstOrDefault(n=>n.Id==Entry(id).NoteId);return new(Title:selected.Text??"");}
  if(widget.ObsProtocol!=1)return new();
  try{string file=Path.Combine(manager.DataDirectory(widget),"obs.json");WidgetManager.RejectLinks(file);if(new FileInfo(file).Length>2*1024*1024)return new();var state=JsonSerializer.Deserialize<ObsState>(File.ReadAllText(file));
   if(state==null||state.Version!=1||Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-state.UpdatedAt)>10000)return new();
   return state with {Title=(state.Title??"")[..Math.Min(state.Title?.Length??0,8000)],Subtitle=(state.Subtitle??"")[..Math.Min(state.Subtitle?.Length??0,1000)],Image=state.Image!=null&&state.Image.StartsWith("data:image/png;base64,",StringComparison.Ordinal)&&state.Image.Length<1500000?state.Image:"",Progress=double.IsFinite(state.Progress)?Math.Clamp(state.Progress,0,1):0,Position=double.IsFinite(state.Position)?Math.Max(0,state.Position):0,Duration=double.IsFinite(state.Duration)?Math.Max(0,state.Duration):0};
  }catch{return new();}
 }
 async Task Serve(TcpClient client){
  using(client)using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token)){
   timeout.CancelAfter(4000);
   try{var stream=client.GetStream();var buffer=new byte[8192];int count=0;
    while(count<buffer.Length){int read=await stream.ReadAsync(buffer.AsMemory(count,buffer.Length-count),timeout.Token);if(read==0)return;count+=read;if(Encoding.ASCII.GetString(buffer,0,count).Contains("\r\n\r\n"))break;}
    string request=Encoding.ASCII.GetString(buffer,0,count);var lines=request.Split("\r\n");var first=lines[0].Split(' ');string body="",type="text/plain; charset=utf-8";int code=404;
    bool host=lines.Any(x=>x.Equals($"Host: 127.0.0.1:{Settings.Port}",StringComparison.OrdinalIgnoreCase));
    if(first.Length==3&&first[0]=="GET"&&host&&request.Contains("\r\n\r\n")){
     var path=first[1].Split('?')[0].Split('/');
     if(path.Length==4&&(path[1]=="overlay"||path[1]=="state")){
      var widget=manager.ReadCatalog().FirstOrDefault(w=>w.Id==path[3]&&w.Available&&(w.ObsProtocol==1||w.Id=="notes"));
      if(widget!=null&&manager.IsInstalled(widget)&&Entry(widget.Id).Enabled&&path[2]==Entry(widget.Id).Token){code=200;if(path[1]=="overlay"){body=Html;type="text/html; charset=utf-8";}else{body=JsonSerializer.Serialize(State(widget.Id));type="application/json; charset=utf-8";}}
     }
    }
    byte[] bytes=Encoding.UTF8.GetBytes(body);var header=Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {(code==200?"OK":"Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; connect-src 'self'; frame-ancestors 'none'\r\nConnection: close\r\n\r\n");await stream.WriteAsync(header,timeout.Token);await stream.WriteAsync(bytes,timeout.Token);
   }catch{}finally{slots.Release();}
  }
 }
 public void Dispose(){cancel.Cancel();listener?.Stop();listener=null;}
 const string Html="""
<!doctype html><html lang="ru"><meta charset="utf-8"><style>
*{box-sizing:border-box}html,body{margin:0;background:transparent;font-family:'Segoe UI',sans-serif;color:#f5f3fa}
#card{display:none;align-items:center;gap:12px;margin:4px;padding:12px;border-radius:18px;background:linear-gradient(125deg,rgba(31,29,42,.97),rgba(19,19,28,.97));border:1px solid rgba(209,196,255,.16);width:360px;max-width:calc(100% - 8px);min-height:106px}
#art{width:80px;height:80px;flex:0 0 80px;border-radius:12px;overflow:hidden;background:linear-gradient(135deg,#52436c,#272535);display:grid;place-items:center;position:relative}#art:before{content:'♪';font-size:30px;color:#c4addf}#cover{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}
article{flex:1;min-width:0}h1{font-size:16px;line-height:21px;font-weight:650;letter-spacing:-.2px;margin:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}p{font-size:12px;line-height:17px;color:#aaa5b9;margin:2px 0 0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
#timeline{margin-top:13px}#times{display:flex;justify-content:space-between;font-size:10px;line-height:13px;font-variant-numeric:tabular-nums;color:#a9a3b8;margin-bottom:5px}#elapsed{color:#e0d7f4}#bar{height:4px;border-radius:4px;background:#3b3748;overflow:hidden}#fill{height:100%;width:0;border-radius:4px;background:linear-gradient(90deg,#a58cd7,#d3bfff);transition:width .8s linear}
body.note #card{width:auto;max-width:calc(100% - 24px);margin:12px;padding:22px;border-radius:24px}body.note #art,body.note #timeline{display:none!important}body.note h1{font-size:26px;line-height:1.25;white-space:pre-wrap;overflow-wrap:anywhere}body.note p{white-space:pre-wrap;font-size:17px}
@media(max-width:280px){#card{gap:9px;padding:10px}#art{width:60px;height:60px;flex-basis:60px}h1{font-size:14px}#timeline{margin-top:8px}}
</style><div id="card"><div id="art"><img id="cover" alt=""></div><article><h1 id="title"></h1><p id="subtitle"></p><div id="timeline"><div id="times"><span id="elapsed">0:00</span><span id="duration">0:00</span></div><div id="bar"><div id="fill"></div></div></div></article></div><script>
const card=document.getElementById('card'),title=document.getElementById('title'),sub=document.getElementById('subtitle'),cover=document.getElementById('cover'),fill=document.getElementById('fill'),elapsed=document.getElementById('elapsed'),duration=document.getElementById('duration');
if(location.pathname.endsWith('/notes'))document.body.classList.add('note');
function time(value){let n=Math.max(0,Math.floor(Number(value)||0)),h=Math.floor(n/3600),m=Math.floor(n/60)%60,s=String(n%60).padStart(2,'0');return h?h+':'+String(m).padStart(2,'0')+':'+s:Math.floor(n/60)+':'+s}
async function refresh(){try{const r=await fetch(location.pathname.replace('/overlay/','/state/'),{cache:'no-store'});if(!r.ok)throw Error();const s=await r.json();title.textContent=s.Title||'';sub.textContent=s.Subtitle||'';sub.style.display=s.Subtitle?'block':'none';cover.style.display=s.Image?'block':'none';if(s.Image&&cover.getAttribute('src')!==s.Image)cover.src=s.Image;elapsed.textContent=time(s.Position);duration.textContent=s.Duration>0?time(s.Duration):'—:—';fill.style.width=((s.Progress||0)*100)+'%';card.style.display=s.Title?'flex':'none';}catch{card.style.display='none';}setTimeout(refresh,1000)}refresh();
</script></html>
""";
}
