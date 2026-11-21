using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WidgetLauncher;

internal sealed record BackupManifest(string Format,int Version,DateTimeOffset Created,string[] Widgets,Dictionary<string,string> Files);
internal sealed record BackupPreview(string FileName,string Fingerprint,BackupManifest Manifest);

internal static class HubBackupArchive
{
 const string Format="VibeWidgetsHub/backup";
 const string Custom="Customization/mini-player/";
 const int MaxFiles=10000,JsonLimit=1024*1024,ManifestLimit=4*1024*1024;
 const long TotalLimit=4L*1024*1024*1024;
 static readonly string[] TopFiles={"hub-settings.json","mini-player/settings.json","mini-player/appearance.json",
  "mini-player/hotkeys.json","mini-player/source-presets.json","mini-player/source-preset-override.json",Custom+"active.json",Custom+"draft.json"};
 static readonly string[] Folders={Custom+"Presets",Custom+"Favorites",Custom+"Media"};
 static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
 internal static bool Portable(string name){
  if(TopFiles.Contains(name,StringComparer.Ordinal))return true;
  if(!name.StartsWith(Custom,StringComparison.Ordinal))return false;
  string leaf=name[Custom.Length..];
  return PresetStore.ValidMediaPath(leaf)||Regex.IsMatch(leaf,@"\A(?:Presets|Favorites)/[a-f0-9]{32}\.json\z",RegexOptions.CultureInvariant);
 }
 static IEnumerable<string> Names(string data){
  foreach(string relative in TopFiles){
   string path=WidgetManager.Child(data,relative);WidgetManager.RejectLinks(path);
   if(File.Exists(path))yield return relative;
  }
  foreach(string folder in Folders){
   string path=WidgetManager.Child(data,folder);WidgetManager.RejectLinks(path);
   if(!Directory.Exists(path))continue;
   foreach(string file in Directory.EnumerateFiles(path)){
    string relative=folder+"/"+Path.GetFileName(file);
    if(Portable(relative)){WidgetManager.RejectLinks(file);yield return relative;}
   }
  }
 }
 static string Digest(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
 static bool Media(string name)=>name.StartsWith(Custom+"Media/",StringComparison.Ordinal);
 static long FileLimit(string name){
  if(Media(name))return PresetStore.MaximumMediaBytes;
  if(name.StartsWith(Custom,StringComparison.Ordinal))return PresetStore.MaximumJsonBytes;
  if(name is "mini-player/source-presets.json" or "mini-player/source-preset-override.json")return 256*1024;
  return JsonLimit;
 }
 static byte[] Read(Stream input,long length,long maximum){
  if(length<1||length>maximum)throw new InvalidDataException("Размер файла резервной копии вне допустимых пределов.");
  byte[] bytes=new byte[(int)length];input.ReadExactly(bytes);
  if(input.ReadByte()!=-1)throw new InvalidDataException("Размер записи не совпадает с описанием.");
  return bytes;
 }
 // Missing fields use the widget's defaults, so backups from earlier versions remain portable.
 sealed record PlayerSettings(double X,double Y,bool Pin,bool Auto,string? Source,string? AudioTarget,string? AudioSource,
  bool MovementLocked,int OnPause,bool ClickThrough,int Anchor,string? AnchorMonitor,int AnchorGap);
 sealed record PlayerAppearance(string Theme="Светлая",string Accent="#7FA6D9",double Scale=1,double Alpha=1,
  string? BorderColor=null,double Round=20,double BorderWidth=1,double CoverSize=52,bool ShowArtist=true,
  bool ShowTime=true,string? BackgroundColor=null,double Sensitivity=1);
 sealed record KeyBinding(int Modifiers,int Key);
 sealed record SourceOverride(string PresetId,string? PreviousId,JsonElement? General);
 static bool ValidColor(string? color)=>color!=null&&Regex.IsMatch(color,@"\A#[0-9a-fA-F]{6}\z");
 static void ValidateWidgetSettings(string name,JsonElement value,Action<JsonElement> checkPreset){
  switch(name){
   case "mini-player/settings.json":
    var s=value.Deserialize<PlayerSettings>()!;
    if(!double.IsFinite(s.X)||!double.IsFinite(s.Y))throw new InvalidDataException("Некорректные координаты плеера.");
    break;
   case "mini-player/hotkeys.json":
    var bindings=value.Deserialize<Dictionary<string,KeyBinding?>>()!;
    if(bindings.Values.Any(b=>b==null||!Enum.IsDefined((System.Windows.Input.Key)b.Key)||b.Key==0||(b.Modifiers&~15)!=0))
     throw new InvalidDataException("Некорректное сочетание клавиш.");
    break;
   case "mini-player/appearance.json":
    var a=value.Deserialize<PlayerAppearance>()!;
    if(!PresetThemes.Names.Contains(a.Theme)||!ValidColor(a.Accent)||(a.BorderColor!=null&&!ValidColor(a.BorderColor))||
       (a.BackgroundColor!=null&&!ValidColor(a.BackgroundColor)))
     throw new InvalidDataException("В копии указана неизвестная тема или неверный цвет.");
    break;
   case "mini-player/source-presets.json":
    var links=value.Deserialize<Dictionary<string,string?>>()!;
    if(links.Any(p=>string.IsNullOrWhiteSpace(p.Key)||string.IsNullOrWhiteSpace(p.Value)))throw new InvalidDataException("Пустая привязка пресета к источнику.");
    break;
   case "mini-player/source-preset-override.json":
    var active=value.Deserialize<SourceOverride>()!;
    if(!Guid.TryParseExact(active.PresetId,"N",out _)||(active.PreviousId!=null&&!Guid.TryParseExact(active.PreviousId,"N",out _)))
     throw new InvalidDataException("Некорректная привязка текущего пресета.");
    if(active.General is {} general)checkPreset(general);
    break;
  }
 }
 static void Validate(string name,byte[] bytes,string hash,HashSet<string> backgrounds){
  if(Media(name)){
   if(!hash.Equals(Path.GetFileNameWithoutExtension(name),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Повреждён фон пресета.");
   PresetStore.ValidateMedia(bytes,Path.GetExtension(name));return;
  }
  using var document=JsonDocument.Parse(bytes);var value=document.RootElement;
  if(name.StartsWith(Custom+"Favorites/",StringComparison.Ordinal)){
   if(value.ValueKind!=JsonValueKind.True)throw new InvalidDataException("Некорректная отметка избранного.");
   return;
  }
  if(value.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Некорректные настройки: "+name);
  if(name=="hub-settings.json")_=value.Deserialize<HubPreferences>(Json);
  void CheckPreset(JsonElement element){
   var p=element.Deserialize<WidgetPreset>(Json)??throw new InvalidDataException("Пустой пресет.");
   p.Normalize();
   if(name.StartsWith(Custom+"Presets/",StringComparison.Ordinal)&&p.Id!=Path.GetFileNameWithoutExtension(name))
    throw new InvalidDataException("Идентификатор сохранённого пресета не совпадает с именем файла.");
   if(p.MediaPath.Length>0)backgrounds.Add(Custom+p.MediaPath);
  }
  if(name.StartsWith(Custom,StringComparison.Ordinal))CheckPreset(value);
  else ValidateWidgetSettings(name,value,CheckPreset);
 }
 static void CheckBackgrounds(HashSet<string> backgrounds,Dictionary<string,string> files){
  if(backgrounds.Any(path=>!files.ContainsKey(path)))throw new InvalidDataException("В копии не хватает фона, указанного в пресете.");
 }
 public static void Export(WidgetManager manager,string file){
  string data=Path.Combine(manager.Root,"Data"),destination=Path.GetFullPath(file);
  if(destination.StartsWith(manager.Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&
     !destination.EndsWith(".vibebackup",StringComparison.OrdinalIgnoreCase))
   throw new IOException("Для копии в папке хаба используйте расширение .vibebackup.");
  string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{
   using(var guard=new PresetStore(manager.Root).AcquireApplyLock())
   using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
   using(var zip=new ZipArchive(output,ZipArchiveMode.Create)){
    var files=new Dictionary<string,string>(StringComparer.Ordinal);
    var backgrounds=new HashSet<string>();long total=0;
    foreach(string name in Names(data)){
     using var source=File.OpenRead(WidgetManager.Child(data,name));
     if(files.Count>=MaxFiles||(total+=source.Length)>TotalLimit)throw new IOException("Библиотека превышает размер одной резервной копии.");
     byte[] bytes=Read(source,source.Length,FileLimit(name));
     string hash=Digest(bytes);Validate(name,bytes,hash,backgrounds);files.Add(name,hash);
     using var entry=zip.CreateEntry("Data/"+name,Media(name)?CompressionLevel.NoCompression:CompressionLevel.Optimal).Open();
     entry.Write(bytes);
    }
    CheckBackgrounds(backgrounds,files);
    var widgets=manager.ReadCatalog().Where(manager.IsInstalled).Select(w=>w.Id).ToArray();
    using var description=zip.CreateEntry("backup.json").Open();
    JsonSerializer.Serialize(description,new BackupManifest(Format,1,DateTimeOffset.UtcNow,widgets,files));
   }
   File.Move(temporary,destination,true);
  }finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 // The manifest digest binds the confirmation screen to the exact files subsequently restored.
 public static BackupPreview ReadArchive(string file,string? stage=null,string? expectedFingerprint=null){
  using var input=File.OpenRead(file);
  if(input.Length>TotalLimit+ManifestLimit+16L*1024*1024)throw new InvalidDataException("Резервная копия слишком велика.");
  using var zip=new ZipArchive(input,ZipArchiveMode.Read);
  if(zip.Entries.Count>MaxFiles+1)throw new InvalidDataException("В резервной копии слишком много файлов.");
  var description=zip.GetEntry("backup.json")??throw new InvalidDataException("Не найдено описание резервной копии VibeWidgetsHub.");
  using var stream=description.Open();byte[] json=Read(stream,description.Length,ManifestLimit);string fingerprint=Digest(json);
  if(expectedFingerprint!=null&&expectedFingerprint!=fingerprint)throw new IOException("Копия изменилась после просмотра. Выберите файл заново.");
  var manifest=JsonSerializer.Deserialize<BackupManifest>(json);
  if(manifest?.Format!=Format||manifest.Version!=1||manifest.Files==null||manifest.Widgets==null||manifest.Widgets.Length>512||
     manifest.Widgets.Distinct().Count()!=manifest.Widgets.Length||zip.Entries.Count!=manifest.Files.Count+1)
   throw new InvalidDataException("Формат или состав резервной копии не поддерживается.");
  foreach(string id in manifest.Widgets)WidgetManager.ValidateId(id??"");
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var backgrounds=new HashSet<string>();long total=0;
  foreach(var entry in zip.Entries){
   if(entry==description)continue;
   if(!entry.FullName.StartsWith("Data/",StringComparison.Ordinal))throw new InvalidDataException("В копии обнаружен посторонний файл.");
   string name=entry.FullName[5..];
   if(!Portable(name)||!seen.Add(name)||!manifest.Files.TryGetValue(name,out string? hash)||(total+=entry.Length)>TotalLimit)
    throw new InvalidDataException("Недопустимый состав резервной копии.");
   using var source=entry.Open();byte[] bytes=Read(source,entry.Length,FileLimit(name));
   var actual=Digest(bytes);if(actual!=hash)throw new InvalidDataException("Повреждён файл резервной копии: "+name);
   Validate(name,bytes,actual,backgrounds);
   if(stage!=null){
    string path=WidgetManager.Child(stage,name);WidgetManager.RejectLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);output.Write(bytes);
   }
  }
  CheckBackgrounds(backgrounds,manifest.Files);
  return new(file,fingerprint,manifest);
 }
}
