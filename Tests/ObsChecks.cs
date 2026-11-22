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
 var widgets=new[]{new Widget{Id="notes",Available=true,Name="Notes",Version="1",Package="notes.zip",Executable="Notes.exe",ObsProtocol=1},new Widget{Id="future",Available=true,Name="Future",Version="1",Package="future.zip",Executable="Future.exe",ObsProtocol=1},new Widget{Id="mini-player",Available=true,Name="Player",Version="1",Package="player.zip",Executable="Player.exe",ObsProtocol=1}};
 File.WriteAllText(Path.Combine(root,"Catalog","widgets.json"),JsonSerializer.Serialize(widgets));var manager=new WidgetManager(root);
 foreach(var w in widgets){Directory.CreateDirectory(manager.WidgetDirectory(w));File.WriteAllText(manager.Executable(w),"");File.WriteAllText(Path.Combine(manager.WidgetDirectory(w),".widget-install.json"),JsonSerializer.Serialize(w));Directory.CreateDirectory(manager.DataDirectory(w));}
 var noteFile=Path.Combine(root,"Data","notes","notes.json");File.WriteAllText(noteFile,"[{\"id\":\"public\",\"text\":\"Live note\"},{\"id\":\"private\",\"text\":\"SECRET\"}]");
 string url;string fontId="";byte[] fontData=File.ReadAllBytes(Path.Combine(Environment.CurrentDirectory,"Source","Fonts","Onest-Bold.ttf"));
 using(var bridge=new ObsBridge(manager)){
  var port=new TcpListener(IPAddress.Loopback,0);port.Start();bridge.Settings.Port=((IPEndPoint)port.LocalEndpoint).Port;port.Stop();bridge.Entry("notes").NoteId="public";bridge.Enable(widgets[0],true);url=bridge.Url("notes");if(args.Length>1){File.WriteAllText(args[1],url);await Task.Delay(30000);}
  using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(5)};
  var html=await client.GetStringAsync(url);Check(html.Contains("textContent")&&!html.Contains("SECRET"),"Browser page uses safe text rendering and contains no private notes");
  var state=await client.GetStringAsync(url.Replace("/overlay/","/state/"));Check(state.Contains("Live note")&&!state.Contains("SECRET"),"Only explicitly selected note is served");
  File.WriteAllText(noteFile,"[{\"id\":\"public\",\"text\":\"Changed\"}]");Check((await client.GetStringAsync(url.Replace("/overlay/","/state/"))).Contains("Changed"),"Note changes reach HTTP endpoint");
  Check((await client.GetAsync(url.Replace(bridge.Entry("notes").Token,"wrong"))).StatusCode==HttpStatusCode.NotFound,"Unknown token is refused");
  Check((await client.PostAsync(url,new StringContent("ignored"))).StatusCode==HttpStatusCode.NotFound,"Write requests are refused");
  using(var request=new HttpRequestMessage(HttpMethod.Get,url)){request.Headers.Host="example.com";Check((await client.SendAsync(request)).StatusCode==HttpStatusCode.NotFound,"Foreign Host is refused");}
  bridge.Enable(widgets[1],true);string file=Path.Combine(root,"Data","future","obs.json");File.WriteAllText(file,JsonSerializer.Serialize(new ObsState(Title:"Future widget",Image:"https://evil.invalid/a",Progress:2,Position:83,Duration:214,UpdatedAt:DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
  var next=bridge.State("future");Check(next.Title=="Future widget"&&next.Image==""&&next.Progress==1&&next.Position==83&&next.Duration==214,"New widget uses protocol without launcher changes; external images rejected");
  bridge.Enable(widgets[2],true);var playerFile=Path.Combine(root,"Data","mini-player","obs.json");File.WriteAllText(playerFile,JsonSerializer.Serialize(new ObsState(Title:"Track",UpdatedAt:DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
  var draft=bridge.Entry("mini-player").Appearance.Copy();draft.Width=480;draft.Parts["artist"].Visible=false;
  Check(bridge.State("mini-player").Appearance!.Width==360,"Editing a detached draft does not change live state");
  bridge.ApplyAppearance(draft);draft.Width=800;
  var live=JsonSerializer.Deserialize<ObsState>(await client.GetStringAsync(bridge.Url("mini-player").Replace("/overlay/","/state/")))!;
  Check(live.Appearance!.Width==480&&!live.Appearance.Parts["artist"].Visible,"Applied layout reaches HTTP state and remains independent from draft");
  string fontSource=Path.Combine(root,"custom.ttf");File.WriteAllBytes(fontSource,fontData);fontId=bridge.ImportFont(fontSource);Check(bridge.ImportFont(fontSource)==fontId&&bridge.Settings.Fonts.Count==1,"Duplicate font import is deduplicated");File.Delete(fontSource);
  string fontUrl=bridge.Url("mini-player").Replace("/overlay/","/font/")+"/"+fontId;var fontResponse=await client.GetAsync(fontUrl);Check(fontResponse.Content.Headers.ContentType!.MediaType=="font/ttf"&&Convert.ToBase64String(await fontResponse.Content.ReadAsByteArrayAsync())==Convert.ToBase64String(fontData),"Imported font remains available after original file is deleted");
  Check((await client.GetAsync(fontUrl.Replace(bridge.Entry("mini-player").Token,"wrong"))).StatusCode==HttpStatusCode.NotFound,"Font route rejects wrong token");
  Check((await client.GetAsync(fontUrl.Replace(fontId,"%2e%2e%2fobs-settings.json"))).StatusCode==HttpStatusCode.NotFound,"Font route rejects path traversal");
  var withFont=live.Appearance.Copy();withFont.FontId=fontId;bridge.ApplyAppearance(withFont);Check(bridge.State("mini-player").Appearance!.FontId==fontId,"Custom font selection reaches overlay");
  bridge.SavePreset("Font",withFont);bridge.Enable(widgets[2],false);Check((await client.GetAsync(fontUrl)).StatusCode==HttpStatusCode.NotFound,"Disabling player also revokes its font URL");bridge.Enable(widgets[2],true);
  bridge.SavePreset("Round",ObsAppearance.Preset("circle"));Check(bridge.State("mini-player").Appearance!.Width==480,"Saving preset leaves current stream unchanged");
  File.WriteAllText(file,JsonSerializer.Serialize(new ObsState(Title:"stale",UpdatedAt:1)));Check(bridge.State("future").Title=="","Stale state hides stopped widget");
  bridge.Enable(widgets[0],false);Check((await client.GetAsync(url)).StatusCode==HttpStatusCode.NotFound,"Disabling immediately revokes source");bridge.Enable(widgets[0],true);
 }
 using(var restored=new ObsBridge(manager)){Check(restored.Entry("mini-player").Appearance.FontId==fontId&&File.Exists(restored.FontPath(fontId))&&restored.Settings.PlayerPresets["Font"].FontId==fontId,"Font file and selection survive restart and preset save");Check(restored.Url("notes")==url&&restored.Entry("notes").NoteId=="public","Links and note selection survive restart");Check(restored.Entry("mini-player").Appearance.Width==480&&restored.Settings.PlayerPresets.ContainsKey("Round"),"Applied appearance and named presets survive restart");restored.Start();using var client=new HttpClient(new HttpClientHandler{UseProxy=false});Check((await client.GetAsync(url)).IsSuccessStatusCode,"Same source works after service restart");}
 string settingsPath=Path.Combine(root,"Data","obs-settings.json");File.WriteAllText(settingsPath,"{\"Widgets\":{\"mini-player\":null},\"Fonts\":null,\"PlayerPresets\":{\"invalid\":null}}");using(var corrupted=new ObsBridge(manager)){Check(corrupted.Settings.Fonts.Count==0&&corrupted.Settings.PlayerPresets.Count==0&&!corrupted.Entry("mini-player").Enabled,"Null settings collections and entries recover without launcher crash");File.WriteAllText(settingsPath+".tmp","interrupted write");corrupted.Save();Check(!File.Exists(settingsPath+".tmp")&&File.ReadAllText(settingsPath).Contains("mini-player"),"Interrupted settings save can be retried atomically");}
 return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
