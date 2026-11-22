using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using File=WidgetLauncherSetup.HubFile;
using Directory=WidgetLauncherSetup.HubDirectory;

[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]
[assembly: AssemblyInformationalVersion(WidgetLauncherSetup.InstallEngine.ProductVersion)]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace WidgetLauncherSetup
{
 // Canonical paths are used for validation and manifests. Extended paths are
 // used only at the filesystem boundary, without changing system policy.
 internal static class HubPath
 {
  public static string Disk(string path){string full=Path.GetFullPath(path);if(full.StartsWith(@"\\?\",StringComparison.Ordinal))return full;return full.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+full.Substring(2):@"\\?\"+full;}
  public static string Plain(string path){if(path.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase))return @"\\"+path.Substring(8);return path.StartsWith(@"\\?\",StringComparison.Ordinal)?path.Substring(4):path;}
 }
 internal static class HubFile
 {
  public static bool Exists(string path){return !String.IsNullOrEmpty(path)&&System.IO.File.Exists(HubPath.Disk(path));}
  public static FileStream Create(string path){return System.IO.File.Create(HubPath.Disk(path));}
  public static void Delete(string path){System.IO.File.Delete(HubPath.Disk(path));}
  public static void Move(string source,string target){System.IO.File.Move(HubPath.Disk(source),HubPath.Disk(target));}
  public static void Copy(string source,string target){Copy(source,target,false);}
  public static void Copy(string source,string target,bool overwrite){System.IO.File.Copy(HubPath.Disk(source),HubPath.Disk(target),overwrite);}
  public static FileAttributes GetAttributes(string path){return System.IO.File.GetAttributes(HubPath.Disk(path));}
  public static void SetAttributes(string path,FileAttributes attributes){System.IO.File.SetAttributes(HubPath.Disk(path),attributes);}
  public static string ReadAllText(string path){return System.IO.File.ReadAllText(HubPath.Disk(path));}
  public static string ReadAllText(string path,Encoding encoding){return System.IO.File.ReadAllText(HubPath.Disk(path),encoding);}
  public static void WriteAllText(string path,string value){System.IO.File.WriteAllText(HubPath.Disk(path),value);}
  public static void WriteAllText(string path,string value,Encoding encoding){System.IO.File.WriteAllText(HubPath.Disk(path),value,encoding);}
 }
 internal static class HubDirectory
 {
  public static bool Exists(string path){return !String.IsNullOrEmpty(path)&&System.IO.Directory.Exists(HubPath.Disk(path));}
  public static DirectoryInfo CreateDirectory(string path){return System.IO.Directory.CreateDirectory(HubPath.Disk(path));}
  public static void Delete(string path){System.IO.Directory.Delete(HubPath.Disk(path));}
  public static string[] GetFiles(string path){return System.IO.Directory.GetFiles(HubPath.Disk(path)).Select(HubPath.Plain).ToArray();}
  public static string[] GetDirectories(string path){return System.IO.Directory.GetDirectories(HubPath.Disk(path)).Select(HubPath.Plain).ToArray();}
  public static IEnumerable<string> EnumerateFileSystemEntries(string path){return System.IO.Directory.EnumerateFileSystemEntries(HubPath.Disk(path)).Select(HubPath.Plain);}
 }
 public sealed class InstallMarker
 {
  public string ProductId { get; set; }
  public string InstallPath { get; set; }
  public string InstallId { get; set; }
  public string[] Files { get; set; }
  public string[] WidgetIds { get; set; }
  public bool TestInstallation { get; set; }
  public string Version { get; set; }
 }

 public static class InstallEngine
 {
  public const string ProductId="MiniPlayer.WidgetLauncher";
  public const string ProductName="VibeWidgetsHub alpha";
  public const string ProductVersion="0.3";
  public const string MarkerName="launcher-install.json";
  public static void ConfigureRuntime(){AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);}
  public static string ResolveDestination(string selected){
   if(String.IsNullOrWhiteSpace(selected)||!Path.IsPathRooted(selected))throw new IOException("Выберите полный путь к папке установки.");
   string drive=Path.GetPathRoot(selected);
   if(drive.Length!=3||drive[1]!=':'||(drive[2]!='\\'&&drive[2]!='/'))throw new IOException("Выберите полный путь на локальном диске, например D:\\Приложения.");
   string full=Path.GetFullPath(selected);
   if(full.StartsWith("\\\\",StringComparison.Ordinal))throw new IOException("Выберите папку на локальном диске.");
   if(File.Exists(full))throw new IOException("По этому пути находится файл. Выберите папку.");
   NoLinks(full);
   string trimmed=full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
   if(File.Exists(Path.Combine(full,MarkerName))){ReadMarker(full);return ValidateDestination(full);}
   string target=String.Equals(Path.GetFileName(trimmed),ProductName,StringComparison.OrdinalIgnoreCase)?full:Path.Combine(full,ProductName);
   return ValidateDestination(target);
  }
  public static string Normalize(string value) { return Path.GetFullPath(HubPath.Plain(value)).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar); }
  public static string Hash(string value) {
   using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Normalize(value).ToUpperInvariant()))).Replace("-","").Substring(0,16);
  }
  public static string Quote(string value) { return "\""+value.Replace("\"","\\\"").TrimEnd('\\')+"\""; }
  public static string Child(string root,string relative) {
   if(String.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(":"))throw new IOException("Недопустимый путь внутри пакета.");
   foreach(string part in relative.Replace('/',Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar))
    if(String.IsNullOrEmpty(part)||part=="."||part==".."||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal))throw new IOException("Недопустимый сегмент пути внутри пакета.");
   string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
   if(!full.StartsWith(Normalize(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Пакет содержит путь за пределами папки установки.");
   return full;
  }
  public static void NoLinks(string path) {
   string parent=Path.GetDirectoryName(Path.GetFullPath(path));
   for(var p=parent==null?null:new DirectoryInfo(parent);p!=null;p=p.Parent)
    if(IsLink(p.FullName))throw new IOException("Папка установки не должна проходить через ссылку или перенаправление.");
   if(IsLink(path))throw new IOException("Обнаружена ссылка вместо обычного файла или папки.");
  }
  static bool IsLink(string path){try{return (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;}catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}}
  static JavaScriptSerializer Json { get { return new JavaScriptSerializer { MaxJsonLength=8*1024*1024 }; } }
  public static InstallMarker ReadMarker(string root) {
   root=Normalize(root);NoLinks(root);var path=Child(root,MarkerName);NoLinks(path);
   if(!File.Exists(path))throw new IOException("В выбранной папке не найден установленный Widget Launcher.");
   var marker=Json.Deserialize<InstallMarker>(File.ReadAllText(path,Encoding.UTF8));
   if(marker==null||marker.ProductId!=ProductId||!String.Equals(Normalize(marker.InstallPath),root,StringComparison.OrdinalIgnoreCase)||marker.InstallId!=Hash(root)||marker.Files==null)
    throw new IOException("Папка не прошла проверку установки Widget Launcher.");
   foreach(var file in marker.Files)Child(root,file);
   return marker;
  }
  public static string ValidateDestination(string requested) {
   if(String.IsNullOrWhiteSpace(requested)||!Path.IsPathRooted(requested))throw new IOException("Выберите полный путь к папке установки.");
   string root=Normalize(requested);
   if(root.StartsWith("\\\\",StringComparison.Ordinal)||root.Length<=3)throw new IOException("Выберите отдельную папку на локальном диске.");
   foreach(var folder in new[]{Environment.SpecialFolder.Windows,Environment.SpecialFolder.System,Environment.SpecialFolder.ProgramFiles,Environment.SpecialFolder.ProgramFilesX86,Environment.SpecialFolder.UserProfile,Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.MyDocuments,Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolder.ApplicationData}) {
    string value=Environment.GetFolderPath(folder);
    if(!String.IsNullOrEmpty(value)&&String.Equals(root,Normalize(value),StringComparison.OrdinalIgnoreCase))throw new IOException("Для лаунчера нужна отдельная папка, например D:\\WidgetLauncher.");
   }
   NoLinks(root);
   if(File.Exists(root))throw new IOException("По этому пути находится файл. Выберите папку.");
   if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any()&&
      (File.Exists(Child(root,MarkerName))||!IsUninstallRemainder(root)))ReadMarker(root);
   return root;
  }
  public static bool IsRunning(string name) {
   Mutex mutex;try { if(Mutex.TryOpenExisting(name,out mutex)){mutex.Dispose();return true;} }catch(UnauthorizedAccessException){return true;}return false;
  }
  static string[] InstalledIds(string root){
   if(!File.Exists(Child(root,MarkerName)))return new string[0];
   var ids=new HashSet<string>(ReadMarker(root).WidgetIds??new string[0],StringComparer.OrdinalIgnoreCase);
   if(ids.Any(id=>!ValidId(id)))throw new IOException("Некорректное имя виджета.");
   // Older catalog updates omitted newly published IDs from the hub marker.
   string widgets=Child(root,"Widgets");NoLinks(widgets);
   if(Directory.Exists(widgets))foreach(string folder in Directory.GetDirectories(widgets)){
    string id=Path.GetFileName(folder);
    if(!ValidId(id)||ids.Contains(id)||IsLink(folder))continue;
    string marker=Child(folder,".widget-install.json");
    if(!File.Exists(marker)||IsLink(marker)||new FileInfo(HubPath.Disk(marker)).Length>65536)continue;
    Dictionary<string,object> record;
    try{record=Json.DeserializeObject(File.ReadAllText(marker)) as Dictionary<string,object>;}
    catch(ArgumentException){continue;}catch(InvalidOperationException){continue;}
    object savedId,executable;
    if(record!=null&&record.TryGetValue("Id",out savedId)&&String.Equals(savedId as string,id,StringComparison.OrdinalIgnoreCase)&&
       record.TryGetValue("Executable",out executable)&&!String.IsNullOrWhiteSpace(executable as string)){
     Child(folder,(string)executable);ids.Add(id);
    }
   }
   return ids.ToArray();
  }
  internal static long InstalledBuild(string directory) {
   string path=Child(directory,"update-state.json");
   NoLinks(path);
   if(!File.Exists(path))return 0;
   Dictionary<string,object> state;
   try {
    if(new FileInfo(HubPath.Disk(path)).Length>65536)throw new IOException("Описание сборки слишком велико.");
    state=Json.DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>;
   }catch(ArgumentException ex){throw new IOException("Не удалось прочитать номер сборки.",ex);}
   object number;
   if(state!=null&&state.TryGetValue("BuildNumber",out number)&&(number is int||number is long)){
    long build=Convert.ToInt64(number);
    if(build>0)return build;
   }
   throw new IOException("Некорректный номер установленной сборки.");
  }

  // Refresh the bundled renderer and player code in the same transaction as the launcher.
  // These two files belong to the widget, not to the launcher's core-file manifest.
  static List<string> StageInstalledPlayer(string root,string extract) {
   var files=new List<string>();
   string marker=Child(root,@"Widgets\mini-player\.widget-install.json");
   NoLinks(marker);
   if(!File.Exists(marker))return files;
   var widget=Json.DeserializeObject(File.ReadAllText(marker)) as Dictionary<string,object>;
   object id,executable;
   if(widget==null||!widget.TryGetValue("Id",out id)||!Equals(id,"mini-player")||
      !widget.TryGetValue("Executable",out executable)||!(executable is string)||
      !String.Equals(((string)executable).Replace('\\','/'),"App/MiniPlayer.exe",StringComparison.OrdinalIgnoreCase))
    throw new IOException("Не удалось проверить установленный мини-плеер.");
   using(var package=ZipFile.OpenRead(HubPath.Disk(Child(extract,@"Packages\mini-player.zip")))){
    foreach(string name in new[]{"MiniPlayer.dll","preset-format.json"}){
     var entry=package.Entries.SingleOrDefault(item=>item.FullName.Replace('\\','/')=="App/"+name);
     if(entry==null||entry.Length<=0||entry.Length>(name=="MiniPlayer.dll"?20*1024*1024:4096))
      throw new IOException("Пакет мини-плеера неполон. Установка отменена.");
     string relative=@"Widgets\mini-player\App\"+name;
     string target=Child(extract,relative);
     Directory.CreateDirectory(Path.GetDirectoryName(target));
     using(var input=entry.Open())using(var output=File.Create(target))input.CopyTo(output);
     files.Add(relative);
    }
   }
   return files;
  }
  public static void CheckStopped(string root) {
   if(IsRunning("Local\\WidgetLauncher."+Hash(root))||InstalledIds(root).Any(id=>IsRunning("Local\\MiniPlayer."+Hash(Path.Combine(root,"Data",id)))))
    throw new IOException("Сначала закройте лаунчер и его мини-плеер, затем повторите установку.");
  }
  public static string Install(Stream zipStream,string requested,bool test,Action<int,string> progress) {
   using(var gate=new Mutex(false,"Local\\WidgetLauncher.Install."+Hash(requested))){bool held=false;try{try{held=gate.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Установка или удаление уже выполняется. Дождитесь завершения.");return InstallCore(zipStream,requested,test,progress);}finally{if(held)gate.ReleaseMutex();}}
  }
  static string InstallCore(Stream zipStream,string requested,bool test,Action<int,string> progress) {
   string root=ValidateDestination(requested);CheckStopped(root);
   bool rootExisted=Directory.Exists(root);InstallMarker previous=File.Exists(Path.Combine(root,MarkerName))?ReadMarker(root):null;
   if(previous!=null&&previous.TestInstallation!=test)throw new IOException("Тестовая и обычная установки не могут заменять друг друга.");
   Directory.CreateDirectory(root);
   string stage=Child(root,".install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
   string extract=Path.Combine(stage,"new"),backup=Path.Combine(stage,"old");Directory.CreateDirectory(extract);Directory.CreateDirectory(backup);
   var moved=new List<string>();var backed=new List<string>();var files=new List<string>();bool completed=false,keepBackup=false;
   try {
    progress(2,"Проверяем и распаковываем компоненты…");
    using(var archive=new ZipArchive(zipStream,ZipArchiveMode.Read,true)) {
     if(archive.Entries.Count>40000)throw new IOException("В пакете слишком много файлов.");
     long size=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int count=0;
     foreach(var entry in archive.Entries) {
      size+=entry.Length;if(size>2000000000)throw new IOException("Пакет превышает допустимый размер.");
      string relative=entry.FullName.Replace('/',Path.DirectorySeparatorChar);string target=Child(extract,relative);
      if((entry.ExternalAttributes>>16&0xF000)==0xA000)throw new IOException("Пакет содержит ссылку.");
      if(relative.EndsWith("\\",StringComparison.Ordinal))continue;
      if(!seen.Add(relative))throw new IOException("Пакет содержит повторяющиеся файлы.");
      if(relative.StartsWith("Widgets\\",StringComparison.OrdinalIgnoreCase)||relative.StartsWith("Data\\",StringComparison.OrdinalIgnoreCase)||relative.Equals(MarkerName,StringComparison.OrdinalIgnoreCase))throw new IOException("Пакет пытается изменить данные виджетов.");
      Directory.CreateDirectory(Path.GetDirectoryName(target));using(var input=entry.Open())using(var output=File.Create(target))input.CopyTo(output);
      files.Add(relative);progress(5+(++count*65/Math.Max(1,archive.Entries.Count)),"Распаковываем компоненты…");
     }
    }
    foreach(string required in new[]{"Launcher.exe","Launcher.dll","Launcher.runtimeconfig.json","Uninstall.exe","Runtime\\dotnet.exe","Catalog\\widgets.json","Packages\\mini-player.zip"})
     if(!File.Exists(Child(extract,required)))throw new IOException("В установщике отсутствует компонент: "+required);
    var ids=ReadWidgetIds(Child(extract,"Catalog\\widgets.json"));
    long packageBuild=InstalledBuild(extract);
    if(previous!=null&&packageBuild<InstalledBuild(root))
     throw new IOException("Уже установлена более новая сборка лаунчера.");
    var widgetFiles=previous==null?new List<string>():StageInstalledPlayer(root,extract);
    progress(72,"Устанавливаем лаунчер…");
    int lastProgress=72;
    foreach(string relative in files.Concat(widgetFiles)) {
     string target=Child(root,relative);NoLinks(target);Directory.CreateDirectory(Path.GetDirectoryName(target));
     if(File.Exists(target)&&!widgetFiles.Contains(relative)&&(previous==null||!previous.Files.Contains(relative,StringComparer.OrdinalIgnoreCase)))throw new IOException("В папке найден посторонний файл с таким же именем: "+relative);
     if(File.Exists(target)){string old=Child(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(old));File.Move(target,old);backed.Add(relative);}
     File.Move(Child(extract,relative),target);moved.Add(relative);
     int percent=72+moved.Count*18/(files.Count+widgetFiles.Count);
     if(percent>lastProgress){progress(percent,"Устанавливаем компоненты…");lastProgress=percent;}
    }
    NoLinks(Child(root,"Widgets"));NoLinks(Child(root,"Data"));Directory.CreateDirectory(Child(root,"Widgets"));Directory.CreateDirectory(Child(root,"Data"));
    var marker=new InstallMarker{ProductId=ProductId,InstallPath=root,InstallId=Hash(root),Files=files.Union(previous==null?new string[0]:previous.Files,StringComparer.OrdinalIgnoreCase).ToArray(),WidgetIds=ids.Union(previous==null||previous.WidgetIds==null?new string[0]:previous.WidgetIds).ToArray(),TestInstallation=test,Version=ProductVersion};
    string markerPath=Child(root,MarkerName),tempMarker=Child(stage,MarkerName);File.WriteAllText(tempMarker,Json.Serialize(marker),new UTF8Encoding(false));
    if(File.Exists(markerPath))System.IO.File.Replace(HubPath.Disk(tempMarker),HubPath.Disk(markerPath),HubPath.Disk(Path.Combine(backup,MarkerName)));
    else File.Move(tempMarker,markerPath);
    completed=true;
    string warning="";progress(92,"Создаём ярлыки…");
    if(!test){try{CreateShortcuts(root);Register(root);}catch(Exception ex){warning="Лаунчер установлен. Не удалось создать один из ярлыков или запись удаления: "+ex.Message;}}
    progress(100,"Готово");return warning;
   }
   catch(Exception installError) {
    if(!completed){
     keepBackup=true;
     var failed=new List<string>();
     var originals=new HashSet<string>(backed,StringComparer.OrdinalIgnoreCase);
     var replacements=new HashSet<string>(moved,StringComparer.OrdinalIgnoreCase);
     foreach(var relative in moved.AsEnumerable().Reverse().Where(name=>!originals.Contains(name))){
      try{File.Delete(Child(root,relative));}catch(IOException){failed.Add(relative);}catch(UnauthorizedAccessException){failed.Add(relative);}
     }
     foreach(var relative in backed.AsEnumerable().Reverse()){
      string old=Child(backup,relative),target=Child(root,relative);
      try{
       if(!File.Exists(target))File.Move(old,target);
       else if(replacements.Contains(relative))System.IO.File.Replace(HubPath.Disk(old),HubPath.Disk(target),null);
       else failed.Add(relative);
      }catch(IOException){failed.Add(relative);}catch(UnauthorizedAccessException){failed.Add(relative);}
     }
     string savedMarker=Path.Combine(backup,MarkerName);
     if(File.Exists(savedMarker)){
      try{File.Copy(savedMarker,Child(root,MarkerName),true);}catch(IOException){failed.Add(MarkerName);}catch(UnauthorizedAccessException){failed.Add(MarkerName);}
     }
     if(failed.Count!=0)throw new IOException("Не удалось завершить откат установки: "+String.Join(", ",failed)+". Оставшиеся резервные файлы сохранены в "+backup+". "+installError.Message,installError);
     keepBackup=false;
    }
    throw;
   }
   finally {if(!keepBackup){if(Directory.Exists(stage))DeleteTree(stage,root);if(!completed&&!rootExisted)RemoveEmptyDirectories(root);}}
  }
  public static string[] ReadWidgetIds(string catalog) {
   object data=Json.DeserializeObject(File.ReadAllText(catalog));object[] entries=data as object[];
   var dict=data as Dictionary<string,object>;if(dict!=null){object value;if(dict.TryGetValue("widgets",out value)||dict.TryGetValue("Widgets",out value))entries=value as object[];}
   if(entries==null)throw new IOException("Каталог виджетов повреждён.");
   var result=new List<string>();foreach(var entry in entries){var item=entry as Dictionary<string,object>;if(item==null)continue;object value;if(!item.TryGetValue("id",out value)&&!item.TryGetValue("Id",out value))continue;string id=value as string;if(ValidId(id))result.Add(id);}
   return result.Distinct().ToArray();
  }
  static bool ValidId(string id){return !String.IsNullOrEmpty(id)&&id.Length<=64&&id.All(c=>(c>='a'&&c<='z')||(c>='0'&&c<='9')||c=='-');}
  // Workshop files have fixed names, preset IDs or content hashes. Leave other files in this tree alone.
  static bool IsPresetFile(string section,string name){
   Guid token;
   if(name.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase)){
    string stem=Path.GetFileNameWithoutExtension(name);
    int dot=stem.LastIndexOf('.');
    if(dot<=0||!Guid.TryParseExact(stem.Substring(dot+1),"N",out token))return false;
    name=stem.Substring(0,dot);
   }
   if(section=="")return String.Equals(name,"active.json",StringComparison.OrdinalIgnoreCase)||String.Equals(name,"draft.json",StringComparison.OrdinalIgnoreCase);
   if(section=="Presets")return String.Equals(Path.GetExtension(name),".json",StringComparison.OrdinalIgnoreCase)&&Guid.TryParseExact(Path.GetFileNameWithoutExtension(name),"N",out token);
   string hash=Path.GetFileNameWithoutExtension(name);
   return hash.Length==64&&hash.All(Uri.IsHexDigit)&&new[]{".png",".jpg",".bmp",".gif"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase);
  }
  static List<string> OwnedPresetFiles(string root){
   string workshop=Child(root,@"Data\Customization\mini-player");
   var result=new List<string>();
   foreach(string section in new[]{"","Presets","Media"}){
    string folder=section==""?workshop:Child(workshop,section);
    NoLinks(folder);
    if(!Directory.Exists(folder))continue;
    foreach(string file in Directory.GetFiles(folder)){
     if(!IsPresetFile(section,Path.GetFileName(file)))continue;
     NoLinks(file);result.Add(file);
    }
   }
   return result;
  }
  static List<string> OwnedObsFonts(string root){
   var result=new List<string>();string folder=Child(root,@"Data\ObsFonts");NoLinks(folder);if(!Directory.Exists(folder))return result;
   foreach(string file in Directory.GetFiles(folder)){string hash=Path.GetFileNameWithoutExtension(file),ext=Path.GetExtension(file);if(hash.Length!=64||!hash.All(Uri.IsHexDigit)||(ext!=".ttf"&&ext!=".otf"))continue;NoLinks(file);result.Add(file);}return result;
  }
  static bool IsUpdateBackup(string folder){
   const string prefix=".update-preserved-";
   string name=Path.GetFileName(folder);Guid id;
   return name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&Guid.TryParseExact(name.Substring(prefix.Length),"N",out id);
  }
  static bool IsUninstallRemainder(string root){
   if(!String.Equals(Path.GetFileName(root),ProductName,StringComparison.OrdinalIgnoreCase))return false;
   var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(string folder in Directory.GetDirectories(root)){
    if(IsUpdateBackup(folder)){
     CheckTree(folder);allowed.Add(folder);
    }
   }
   string workshop=Child(root,@"Data\Customization\mini-player");
   if(!Directory.Exists(workshop))return allowed.Count>0&&Directory.EnumerateFileSystemEntries(root).All(allowed.Contains);
   allowed.UnionWith(OwnedPresetFiles(root));
   if(allowed.Count==0)return false;
   var folders=new[]{root,Child(root,"Data"),Child(root,@"Data\Customization"),workshop,Child(workshop,"Presets"),Child(workshop,"Media")};
   allowed.UnionWith(folders.Skip(1));
   return folders.Where(Directory.Exists).All(folder=>Directory.EnumerateFileSystemEntries(folder).All(allowed.Contains));
  }
  static string RegistryPath(string root){return @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WidgetLauncher-"+Hash(root);}
  static void Register(string root){using(var key=Registry.CurrentUser.CreateSubKey(RegistryPath(root))){key.SetValue("DisplayName",ProductName);key.SetValue("DisplayVersion",ProductVersion);key.SetValue("Publisher","Mini Player Team");key.SetValue("InstallLocation",root);key.SetValue("DisplayIcon",Path.Combine(root,"Launcher.exe"));key.SetValue("UninstallString",Quote(Path.Combine(root,"Uninstall.exe")));key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}}
  static string[] ShortcutPaths(string root){string name=ProductName+".lnk";string common=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),ProductName);return new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),name),Path.Combine(common,name)};}
  static string[] LegacyShortcutPaths(){string name="Widget Launcher.lnk";return new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),name),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Widget Launcher",name)};}
  static string ShortcutTarget(object shortcut){return (string)shortcut.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null);}
  static object CreateShortcutObject(object shell,string path){return shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});}
  static void CreateShortcuts(string root){object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));try{foreach(string path in ShortcutPaths(root)){Directory.CreateDirectory(Path.GetDirectoryName(path));object shortcut=CreateShortcutObject(shell,path);try{string target=Path.Combine(root,"Launcher.exe");if(File.Exists(path)&&!String.Equals(ShortcutTarget(shortcut),target,StringComparison.OrdinalIgnoreCase))continue;shortcut.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{target});shortcut.GetType().InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{root});shortcut.GetType().InvokeMember("Description",BindingFlags.SetProperty,null,shortcut,new object[]{"Виджеты для рабочего стола"});shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);}finally{Marshal.FinalReleaseComObject(shortcut);}}}finally{Marshal.FinalReleaseComObject(shell);}}
  static void RemoveShortcuts(string root){object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));try{foreach(string path in ShortcutPaths(root).Concat(LegacyShortcutPaths())){if(!File.Exists(path))continue;object shortcut=CreateShortcutObject(shell,path);try{if(String.Equals(ShortcutTarget(shortcut),Path.Combine(root,"Launcher.exe"),StringComparison.OrdinalIgnoreCase))File.Delete(path);}finally{Marshal.FinalReleaseComObject(shortcut);}}}finally{Marshal.FinalReleaseComObject(shell);}}
  static string RunName(string root){return "VibeWidgetsHub-"+Hash(root);}
  static string RunCommand(string root){return Quote(Path.Combine(root,"Launcher.exe"))+" --autostart";}
  public static void RemoveAutostart(string root){using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)){if(key!=null&&String.Equals(key.GetValue(RunName(root)) as string,RunCommand(root),StringComparison.OrdinalIgnoreCase))key.DeleteValue(RunName(root),false);}}
  static void RelocateAutostart(string previous,string next){using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)){if(key==null||!String.Equals(key.GetValue(RunName(previous)) as string,RunCommand(previous),StringComparison.OrdinalIgnoreCase))return;if(key.GetValue(RunName(next))!=null&&!String.Equals(key.GetValue(RunName(next)) as string,RunCommand(next),StringComparison.OrdinalIgnoreCase))throw new IOException("В автозапуске уже есть другая запись для новой папки.");key.SetValue(RunName(next),RunCommand(next));key.DeleteValue(RunName(previous),false);}}
  static void RetargetShortcuts(string previous,string next){object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));try{foreach(string path in ShortcutPaths(previous).Concat(LegacyShortcutPaths())){if(!File.Exists(path))continue;object shortcut=CreateShortcutObject(shell,path);try{if(!String.Equals(ShortcutTarget(shortcut),Path.Combine(previous,"Launcher.exe"),StringComparison.OrdinalIgnoreCase))continue;shortcut.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{Path.Combine(next,"Launcher.exe")});shortcut.GetType().InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{next});shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);}finally{Marshal.FinalReleaseComObject(shortcut);}}}finally{Marshal.FinalReleaseComObject(shell);}}
  public static void RelocateRegistration(string previous,string next){
   using(var key=Registry.CurrentUser.OpenSubKey(RegistryPath(previous))){if(key!=null&&!String.Equals(key.GetValue("InstallLocation") as string,previous,StringComparison.OrdinalIgnoreCase))throw new IOException("Запись установки относится к другой папке.");}
   using(var key=Registry.CurrentUser.OpenSubKey(RegistryPath(next))){if(key!=null)throw new IOException("Новая папка уже зарегистрирована как отдельная установка.");}
   try{Register(next);RetargetShortcuts(previous,next);CreateShortcuts(next);RelocateAutostart(previous,next);Registry.CurrentUser.DeleteSubKeyTree(RegistryPath(previous),false);}
   catch{try{RetargetShortcuts(next,previous);RelocateAutostart(next,previous);Register(previous);Registry.CurrentUser.DeleteSubKeyTree(RegistryPath(next),false);}catch{}throw;}
  }
  public static void StopApplications(string root){Signal("Local\\WidgetLauncher."+Hash(root)+".Stop");foreach(string id in InstalledIds(root))Signal("Local\\MiniPlayer."+Hash(Path.Combine(root,"Data",id))+".Stop");}
  static void Signal(string name){EventWaitHandle handle;if(EventWaitHandle.TryOpenExisting(name,out handle)){using(handle)handle.Set();}}
  static void DeleteTree(string path,string root){root=Normalize(root);path=Normalize(path);if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Удаление за пределами папки приложения запрещено.");NoLinks(path);if(!Directory.Exists(path))return;foreach(string file in Directory.GetFiles(path)){NoLinks(file);DeleteFile(file);}foreach(string dir in Directory.GetDirectories(path)){NoLinks(dir);DeleteTree(dir,root);}Directory.Delete(path);}
  static void DeleteFile(string path){for(int attempt=0;attempt<40;attempt++){try{if(File.Exists(path)){File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);}return;}catch(IOException){if(attempt==39)throw;Thread.Sleep(250);}catch(UnauthorizedAccessException){if(attempt==39)throw;Thread.Sleep(250);}}}
  static void RemoveEmptyDirectories(string path,bool preserveUpdateBackups=false){
   if(!Directory.Exists(path))return;
   NoLinks(path);
   foreach(string sub in Directory.GetDirectories(path)){
    if(preserveUpdateBackups&&IsUpdateBackup(sub))continue;
    if((File.GetAttributes(sub)&FileAttributes.ReparsePoint)==0)RemoveEmptyDirectories(sub);
   }
   if(!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);
  }
  public static void Remove(string requested,bool test){
   using(var gate=new Mutex(false,"Local\\WidgetLauncher.Install."+Hash(requested))){bool held=false;try{try{held=gate.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Установка или удаление уже выполняется. Дождитесь завершения.");RemoveCore(requested,test);}finally{if(held)gate.ReleaseMutex();}}
  }
  static void RemoveCore(string requested,bool test){
   string root=Normalize(requested);var marker=ReadMarker(root);if(marker.TestInstallation!=test)throw new IOException("Тип установки не соответствует режиму удаления.");
   StopApplications(root);for(int i=0;i<40;i++){try{CheckStopped(root);break;}catch(IOException){if(i==39)throw;Thread.Sleep(250);}}
   // Preflight every path before the first delete. Never follow junctions or symlinks.
   foreach(string file in marker.Files)NoLinks(Child(root,file));
   var ownedPresets=OwnedPresetFiles(root);var ownedObsFonts=OwnedObsFonts(root);
   string[] installedIds=InstalledIds(root);
   foreach(string id in installedIds)foreach(string parent in new[]{"Widgets","Data"})CheckTree(Child(root,parent+"\\"+id));
   string hubSettings=Child(root,"Data\\hub-settings.json"),startupError=Child(root,"Data\\autostart-error.txt");NoLinks(hubSettings);NoLinks(startupError);
   string obsSettings=Child(root,@"Data\obs-settings.json"),obsTemp=obsSettings+".tmp";NoLinks(obsSettings);NoLinks(obsTemp);
   string downloads=Child(root,".update-downloads");CheckTree(downloads);
   if(!test){RemoveShortcuts(root);RemoveAutostart(root);using(var key=Registry.CurrentUser.OpenSubKey(RegistryPath(root))){if(key!=null&&!String.Equals(key.GetValue("InstallLocation") as string,root,StringComparison.OrdinalIgnoreCase))throw new IOException("Запись установки относится к другой папке.");}Registry.CurrentUser.DeleteSubKeyTree(RegistryPath(root),false);}
   foreach(string id in installedIds)foreach(string parent in new[]{"Widgets","Data"})DeleteTree(Child(root,parent+"\\"+id),root);
   foreach(string file in ownedPresets)DeleteFile(file);foreach(string file in ownedObsFonts)DeleteFile(file);
   DeleteFile(hubSettings);DeleteFile(obsSettings);DeleteFile(obsTemp);
   DeleteFile(startupError);
   DeleteTree(downloads,root);
   // Keep the uninstaller available if a locked file prevents completing the operation.
   foreach(string file in marker.Files.OrderBy(f=>f.Equals("Uninstall.exe",StringComparison.OrdinalIgnoreCase)?1:0)){string target=Child(root,file);DeleteFile(target);}
   File.Delete(Child(root,MarkerName));RemoveEmptyDirectories(root,true);
  }
  static void CheckTree(string path){NoLinks(path);if(!Directory.Exists(path))return;foreach(string file in Directory.GetFiles(path))NoLinks(file);foreach(string dir in Directory.GetDirectories(path))CheckTree(dir);}
 }

 internal static partial class Ui
 {
  static PrivateFontCollection fonts;static IntPtr fontMemory;
  [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr data,uint size,IntPtr reserved,ref uint count);
  public static readonly Color Background=Color.FromArgb(19,21,28),Panel=Color.FromArgb(29,32,42),Text=Color.FromArgb(239,239,246),Muted=Color.FromArgb(164,170,188),Accent=Color.FromArgb(180,154,255);
  public static Font Font(float size){if(fonts==null){fonts=new PrivateFontCollection();using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Onest.ttf")){if(stream!=null){byte[] bytes=new byte[stream.Length];stream.Read(bytes,0,bytes.Length);fontMemory=Marshal.AllocCoTaskMem(bytes.Length);Marshal.Copy(bytes,0,fontMemory,bytes.Length);fonts.AddMemoryFont(fontMemory,bytes.Length);uint count=0;AddFontMemResourceEx(fontMemory,(uint)bytes.Length,IntPtr.Zero,ref count);}}}return new Font(fonts.Families.Length>0?fonts.Families[0]:SystemFonts.MessageBoxFont.FontFamily,size,FontStyle.Bold);}
  public static Label Label(string text,int x,int y,int width,int height,float size,Color color){return new Label{Text=text,Location=new Point(x,y),Size=new Size(width,height),Font=Font(size),ForeColor=color,BackColor=Color.Transparent};}
  public static Button Button(string text,int x,int y,int width,bool primary){return new RoundedButton{Text=text,Location=new Point(x,y),Size=new Size(width,44),Font=Font(10),BackColor=primary?Accent:Panel,ForeColor=primary?Background:Text,Cursor=Cursors.Hand};}
  public static void Prepare(Form form){form.BackColor=Background;form.ForeColor=Text;form.Font=Font(10);form.AutoScaleMode=AutoScaleMode.Dpi;form.StartPosition=FormStartPosition.CenterScreen;form.FormBorderStyle=FormBorderStyle.None;form.MaximizeBox=false;AddChrome(form);using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Launcher.ico")){if(stream!=null)form.Icon=new Icon(stream);}}
 }
}
