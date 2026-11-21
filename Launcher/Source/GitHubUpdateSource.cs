using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VibeWidgetsUpdates;

namespace WidgetLauncher;

internal sealed record GitHubUpdateCandidate(string FeedDirectory,UpdateManifest Manifest,Uri PackageUri);

internal sealed class GitHubUpdateSource:IDisposable
{
 public const string RepositoryUrl="https://github.com/Keksichek/mini-player";
 const string RepositoryPath="/Keksichek/mini-player/";
 readonly HttpClient client;readonly bool ownsClient;readonly string cache,publicKey;
 public GitHubUpdateSource(string cacheDirectory,HttpClient? http=null,string? key=null){
  cache=Path.GetFullPath(cacheDirectory);publicKey=key??UpdateProtocol.PublicKey();ownsClient=http==null;
  client=http??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(15)};
 }
 static bool SafeUri(Uri uri)=>uri.Scheme=="https"&&uri.Port==443&&uri.UserInfo.Length==0&&new[]{"github.com","api.github.com","release-assets.githubusercontent.com","objects.githubusercontent.com"}.Contains(uri.Host,StringComparer.OrdinalIgnoreCase);
 static Uri AssetUri(string text){
  if(!Uri.TryCreate(text,UriKind.Absolute,out var uri)||!SafeUri(uri)||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith(RepositoryPath+"releases/download/",StringComparison.OrdinalIgnoreCase))throw new IOException("GitHub вернул недопустимый адрес пакета.");
  return uri;
 }
 async Task<HttpResponseMessage> OpenAsync(Uri address,CancellationToken token){
  for(int redirects=0;redirects<6;redirects++){
   if(!SafeUri(address))throw new IOException("Недопустимое перенаправление загрузки.");
   using var request=new HttpRequestMessage(HttpMethod.Get,address);
   request.Headers.UserAgent.ParseAdd("VibeWidgetsHub/"+BuildInfo.Version);request.Headers.Accept.ParseAdd(address.Host=="api.github.com"?"application/vnd.github+json":"application/octet-stream");
   if(address.Host=="api.github.com")request.Headers.Add("X-GitHub-Api-Version","2026-03-10");
   var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
   int code=(int)response.StatusCode;
   if(code is 301 or 302 or 303 or 307 or 308){var location=response.Headers.Location;response.Dispose();if(location==null)throw new IOException("Адрес загрузки отсутствует.");address=location.IsAbsoluteUri?location:new Uri(address,location);continue;}
   if(response.StatusCode==HttpStatusCode.Forbidden||code==429){response.Dispose();throw new IOException("GitHub временно ограничил проверки. Повторите позже.");}
   if(!response.IsSuccessStatusCode){response.Dispose();throw new IOException(code==404?"Файлы обновления на GitHub пока недоступны.":"GitHub не ответил на запрос обновления ("+code+").");}
   return response;
  }
  throw new IOException("Слишком много перенаправлений при загрузке.");
 }
 async Task<byte[]> ReadAsync(Uri uri,int limit,CancellationToken token){
  using var response=await OpenAsync(uri,token).ConfigureAwait(false);
  if(response.Content.Headers.ContentLength>limit)throw new IOException("Ответ GitHub превышает допустимый размер.");
  using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);using var output=new MemoryStream();var buffer=new byte[16384];int count;
  while((count=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0){if(output.Length+count>limit)throw new IOException("Ответ GitHub превышает допустимый размер.");output.Write(buffer,0,count);}return output.ToArray();
 }
 public async Task<GitHubUpdateCandidate?> FindAsync(long currentBuild,CancellationToken token){
  WidgetManager.RejectLinks(cache);
  if(Directory.Exists(cache))foreach(string folder in Directory.EnumerateDirectories(cache)){string name=Path.GetFileName(folder);if(name.StartsWith("github-",StringComparison.Ordinal)&&Guid.TryParseExact(name[7..],"N",out _))try{CleanFolder(folder);}catch(IOException){}catch(UnauthorizedAccessException){}}
  for(int page=1;page<=3;page++){
   var bytes=await ReadAsync(new Uri("https://api.github.com/repos/Keksichek/mini-player/releases?per_page=30&page="+page),2*1024*1024,token).ConfigureAwait(false);
   using var data=JsonDocument.Parse(bytes);if(data.RootElement.ValueKind!=JsonValueKind.Array)throw new IOException("Ответ списка релизов GitHub повреждён.");
   foreach(var release in data.RootElement.EnumerateArray().OrderByDescending(x=>x.TryGetProperty("published_at",out var date)?date.GetString():"")){
    if(!release.TryGetProperty("draft",out var draft)||draft.ValueKind!=JsonValueKind.False||!release.TryGetProperty("target_commitish",out var branch)||branch.GetString()!="keksichek/launcher"||!release.TryGetProperty("assets",out var assets)||assets.ValueKind!=JsonValueKind.Array)continue;
    Uri? Asset(string name){var matches=assets.EnumerateArray().Where(x=>x.TryGetProperty("name",out var n)&&n.GetString()==name&&x.TryGetProperty("state",out var state)&&state.GetString()=="uploaded").ToArray();return matches.Length==1?AssetUri(matches[0].GetProperty("browser_download_url").GetString()??""):null;}
    var json=Asset("update.json");var signature=Asset("update.sig");if(json==null||signature==null)continue;
    byte[] metadata=await ReadAsync(json,65536,token).ConfigureAwait(false),signed=await ReadAsync(signature,16384,token).ConfigureAwait(false);
    // Each check has its own directory so a simultaneous publisher change cannot mix files.
    WidgetManager.RejectLinks(cache);Directory.CreateDirectory(cache);string folder=Path.Combine(cache,"github-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
    try{
     await File.WriteAllBytesAsync(Path.Combine(folder,"update.json"),metadata,token).ConfigureAwait(false);await File.WriteAllBytesAsync(Path.Combine(folder,"update.sig"),signed,token).ConfigureAwait(false);
     var manifest=UpdateProtocol.Read(folder,publicKey);var package=Asset(manifest.Package);
     if(manifest.Package!="payload.zip")throw new IOException("У релиза неподдерживаемое имя пакета. Ожидается payload.zip.");
     if(package==null)throw new IOException("В релизе не хватает подписанного пакета обновления.");
     if(manifest.BuildNumber>currentBuild)return new(folder,manifest,package);
    }catch{CleanFolder(folder);throw;}
    CleanFolder(folder);
   }
   if(data.RootElement.GetArrayLength()<30)break;
  }
  return null;
 }
 public async Task DownloadAsync(GitHubUpdateCandidate candidate,IProgress<int>? progress,CancellationToken token){
  var manifest=UpdateProtocol.Read(candidate.FeedDirectory,publicKey);
  if(manifest.BuildNumber!=candidate.Manifest.BuildNumber||manifest.Sha256!=candidate.Manifest.Sha256||manifest.Package!=candidate.Manifest.Package||manifest.Size!=candidate.Manifest.Size)throw new IOException("Описание пакета изменилось. Повторите проверку.");
  string file=Path.Combine(candidate.FeedDirectory,manifest.Package),partial=file+".partial";WidgetManager.RejectLinks(file);WidgetManager.RejectLinks(partial);
  try{
   using(var response=await OpenAsync(candidate.PackageUri,token).ConfigureAwait(false)){
    if(response.Content.Headers.ContentLength is long length&&length!=manifest.Size)throw new IOException("Размер загрузки не совпадает с подписанным описанием.");
    using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);using var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,65536,true);var buffer=new byte[65536];long total=0;int count,last=-1;
    while((count=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0){total+=count;if(total>manifest.Size)throw new IOException("Пакет обновления превышает подписанный размер.");await output.WriteAsync(buffer.AsMemory(0,count),token).ConfigureAwait(false);int percent=(int)(100*total/manifest.Size);if(percent!=last){progress?.Report(percent);last=percent;}}
    if(total!=manifest.Size)throw new IOException("Загрузка пакета прервалась.");
   }
   token.ThrowIfCancellationRequested();File.Move(partial,file,true);UpdateProtocol.VerifyPackage(candidate.FeedDirectory,manifest,publicKey);
  }catch{if(File.Exists(partial))File.Delete(partial);if(File.Exists(file))File.Delete(file);throw;}
 }
 public static void CleanFolder(string folder){
  WidgetManager.RejectLinks(folder);if(!Directory.Exists(folder)||!Path.GetFileName(folder).StartsWith("github-",StringComparison.Ordinal))return;
  foreach(string name in new[]{"update.json","update.sig","payload.zip","payload.zip.partial"}){string file=Path.Combine(folder,name);WidgetManager.RejectLinks(file);if(File.Exists(file))File.Delete(file);}
  if(!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);
 }
 public void Dispose(){if(ownsClient)client.Dispose();}
}
