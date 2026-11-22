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

public sealed record ObsState(int Version=1,string Title="",string Subtitle="",string Image="",double Progress=0,long UpdatedAt=0,double Position=0,double Duration=0,ObsAppearance? Appearance=null);
public sealed class ObsEntry { public bool Enabled{get;set;} public string Token{get;set;}=Guid.NewGuid().ToString("N"); public string NoteId{get;set;}=""; public ObsAppearance Appearance{get;set;}=new(); }
public sealed class ObsPreferences { public Dictionary<string,string> Fonts{get;set;}=new(); public Dictionary<string,ObsAppearance> PlayerPresets{get;set;}=new(); public int Port{get;set;}=18765; public Dictionary<string,ObsEntry> Widgets{get;set;}=new(); }

public sealed partial class ObsBridge:IDisposable
{
 readonly WidgetManager manager;readonly object gate=new();readonly SemaphoreSlim slots=new(8);readonly CancellationTokenSource cancel=new();
 TcpListener? listener;
 public ObsPreferences Settings{get;}
 string SettingsPath=>Path.Combine(manager.Root,"Data","obs-settings.json");
 public bool Running=>listener!=null;
 public ObsBridge(WidgetManager manager){this.manager=manager;try{Settings=JsonSerializer.Deserialize<ObsPreferences>(File.ReadAllText(SettingsPath))??new();}catch{Settings=new();}if(Settings.Port<1024||Settings.Port>65535)Settings.Port=18765;}
 public ObsEntry Entry(string id){lock(gate){if(!Settings.Widgets.TryGetValue(id,out var e))Settings.Widgets[id]=e=new();return e;}}
 public void Save(){lock(gate){WidgetManager.RejectLinks(Path.GetDirectoryName(SettingsPath)!);Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);File.WriteAllText(SettingsPath+".tmp",JsonSerializer.Serialize(Settings));File.Move(SettingsPath+".tmp",SettingsPath,true);}}
 public void ApplyAppearance(ObsAppearance appearance){lock(gate){var entry=Entry("mini-player");var old=entry.Appearance;entry.Appearance=appearance.Copy();try{Save();}catch{entry.Appearance=old;throw;}}}
 public void SavePreset(string name,ObsAppearance appearance){if(string.IsNullOrWhiteSpace(name)||name.Length>60)throw new InvalidDataException("Введите название до 60 символов.");lock(gate){Settings.PlayerPresets.TryGetValue(name,out var old);Settings.PlayerPresets[name]=appearance.Copy();try{Save();}catch{if(old==null)Settings.PlayerPresets.Remove(name);else Settings.PlayerPresets[name]=old;throw;}}}
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
   return state with {Title=(state.Title??"")[..Math.Min(state.Title?.Length??0,8000)],Subtitle=(state.Subtitle??"")[..Math.Min(state.Subtitle?.Length??0,1000)],Image=state.Image!=null&&state.Image.StartsWith("data:image/png;base64,",StringComparison.Ordinal)&&state.Image.Length<1500000?state.Image:"",Progress=double.IsFinite(state.Progress)?Math.Clamp(state.Progress,0,1):0,Position=double.IsFinite(state.Position)?Math.Max(0,state.Position):0,Duration=double.IsFinite(state.Duration)?Math.Max(0,state.Duration):0,Appearance=id=="mini-player"?(Entry(id).Appearance??new()).Copy():null};
  }catch{return new();}
 }
 async Task Serve(TcpClient client){
  using(client)using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token)){
   timeout.CancelAfter(4000);
   try{var stream=client.GetStream();var buffer=new byte[8192];int count=0;
    while(count<buffer.Length){int read=await stream.ReadAsync(buffer.AsMemory(count,buffer.Length-count),timeout.Token);if(read==0)return;count+=read;if(Encoding.ASCII.GetString(buffer,0,count).Contains("\r\n\r\n"))break;}
    string request=Encoding.ASCII.GetString(buffer,0,count);var lines=request.Split("\r\n");var first=lines[0].Split(' ');byte[]? binary=null;string body="",type="text/plain; charset=utf-8";int code=404;
    bool host=lines.Any(x=>x.Equals($"Host: 127.0.0.1:{Settings.Port}",StringComparison.OrdinalIgnoreCase));
    if(first.Length==3&&first[0]=="GET"&&host&&request.Contains("\r\n\r\n")){
     var path=first[1].Split('?')[0].Split('/');
     if(path.Length==5&&path[1]=="font"&&path[3]=="mini-player"){
      var widget=manager.ReadCatalog().FirstOrDefault(w=>w.Id=="mini-player"&&w.Available&&w.ObsProtocol==1);
      if(widget!=null&&manager.IsInstalled(widget)&&Entry(widget.Id).Enabled&&path[2]==Entry(widget.Id).Token){var file=FontPath(path[4]);if(file!=null){binary=File.ReadAllBytes(file);type=path[4].EndsWith(".otf")?"font/otf":"font/ttf";code=200;}}
     }
     if(path.Length==4&&(path[1]=="overlay"||path[1]=="state")){
      var widget=manager.ReadCatalog().FirstOrDefault(w=>w.Id==path[3]&&w.Available&&(w.ObsProtocol==1||w.Id=="notes"));
      if(widget!=null&&manager.IsInstalled(widget)&&Entry(widget.Id).Enabled&&path[2]==Entry(widget.Id).Token){code=200;if(path[1]=="overlay"){body=Html;type="text/html; charset=utf-8";}else{body=JsonSerializer.Serialize(State(widget.Id));type="application/json; charset=utf-8";}}
     }
    }
    byte[] bytes=binary??Encoding.UTF8.GetBytes(body);var header=Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {(code==200?"OK":"Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'\r\nConnection: close\r\n\r\n");await stream.WriteAsync(header,timeout.Token);await stream.WriteAsync(bytes,timeout.Token);
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
let appearanceKey='',fontKey='',activeFont=null;
const scrollState=new WeakMap();
function setText(e,value){if(e.dataset.value===value)return;e.dataset.value=value;e.replaceChildren();const span=document.createElement('span');span.textContent=value;span.style.cssText='display:inline-block;white-space:inherit';e.appendChild(span);scrollState.get(e)?.animation?.cancel();scrollState.delete(e);}
function scrollText(){if(!appearanceKey)return;for(const e of [title,sub]){const span=e.firstElementChild;if(!span)continue;const overflow=Math.ceil(span.getBoundingClientRect().width-e.clientWidth),key=[e.dataset.value,e.clientWidth,span.getBoundingClientRect().width,getComputedStyle(e).fontFamily,e.clientHeight].join('|');const old=scrollState.get(e);if(old?.key===key)continue;old?.animation?.cancel();let animation=null;
 if(e.clientWidth>0&&e.clientHeight>0&&overflow>1){const travel=overflow/28*1000,pause=1800,total=2*travel+2*pause;animation=span.animate([{transform:'translateX(0)',offset:0},{transform:'translateX(0)',offset:pause/total},{transform:`translateX(-${overflow}px)`,offset:(pause+travel)/total},{transform:`translateX(-${overflow}px)`,offset:(2*pause+travel)/total},{transform:'translateX(0)',offset:1}],{duration:total,iterations:Infinity,easing:'linear'});}
 scrollState.set(e,{key,animation});e.style.textOverflow='clip';}}
const resizeObserver=new ResizeObserver(scrollText);resizeObserver.observe(title);resizeObserver.observe(sub);
function customFont(a){const id=a.FontId||'';if(id===fontKey)return;fontKey=id;if(activeFont){document.fonts.delete(activeFont);activeFont=null;}if(!/^[a-f0-9]{64}\.(ttf|otf)$/.test(id))return;
 const face=new FontFace('OBS-'+id,`url("${location.pathname.replace('/overlay/','/font/')}/${id}")`);activeFont=face;face.load().then(()=>{if(activeFont!==face)return;document.fonts.add(face);scrollText();}).catch(()=>{});
}
function appearance(a){if(!a)return;const key=JSON.stringify(a);if(key===appearanceKey)return;appearanceKey=key;customFont(a);
const map={cover:'art',title:'title',artist:'subtitle',elapsed:'elapsed',duration:'duration',bar:'bar'};
card.style.cssText=`position:relative;display:block;box-sizing:content-box;width:${a.Width}px;height:${a.Height}px;max-width:none;min-height:0;margin:4px;padding:0;border:0;border-radius:${a.Shape==='circle'?a.Width/2:a.Shape==='rectangle'?0:a.Radius}px;overflow:hidden;background:transparent;`;
let bg=document.getElementById('custom-background');if(!bg){bg=document.createElement('div');bg.id='custom-background';card.prepend(bg);}bg.style.cssText=`position:absolute;inset:0;box-sizing:border-box;border:${a.BorderWidth}px solid ${a.Border};border-radius:inherit;background:rgba(${parseInt(a.Background.slice(1,3),16)},${parseInt(a.Background.slice(3,5),16)},${parseInt(a.Background.slice(5,7),16)},${a.Opacity});pointer-events:none`;
for(const [key,id] of Object.entries(map)){const p=a.Parts[key],e=document.getElementById(id);card.appendChild(e);e.style.cssText=`position:absolute;box-sizing:border-box;left:${p.X}px;top:${p.Y}px;width:${p.Width}px;height:${p.Height}px;margin:0;display:${p.Visible?'block':'none'};font-family:${a.FontId?"'OBS-"+a.FontId+"',":''}'${a.Font}';font-size:${p.FontSize}px;line-height:1.25;overflow:hidden;white-space:nowrap;color:${key==='title'?a.Text:a.Secondary};`;if(key==='duration')e.style.textAlign='right';if(key==='cover'){if(p.Visible)e.style.display='grid';e.style.borderRadius='12px';e.style.overflow='hidden';}if(key==='bar'){e.style.borderRadius='4px';e.style.background='#40394F';e.style.overflow='hidden';}}
fill.style.background=a.Accent;
}
async function refresh(){try{const r=await fetch(location.pathname.replace('/overlay/','/state/'),{cache:'no-store'});if(!r.ok)throw Error();const s=await r.json();setText(title,s.Title||'');setText(sub,s.Subtitle||'');if(!s.Appearance)sub.style.display=s.Subtitle?'block':'none';cover.style.display=s.Image?'block':'none';if(s.Image&&cover.getAttribute('src')!==s.Image)cover.src=s.Image;elapsed.textContent=time(s.Position);duration.textContent=s.Duration>0?time(s.Duration):'—:—';fill.style.width=((s.Progress||0)*100)+'%';appearance(s.Appearance);card.style.display=s.Title?(s.Appearance?'block':'flex'):'none';scrollText();}catch{card.style.display='none';}setTimeout(refresh,1000)}refresh();
</script></html>
""";
}
