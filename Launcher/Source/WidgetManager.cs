using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetLauncher;

public sealed record Widget
{
 public string Id { get; init; } = "";
 public string Name { get; init; } = "";
 public string Description { get; init; } = "";
 public string Version { get; init; } = "";
 public string Package { get; init; } = "";
 public string Executable { get; init; } = "";
 public bool Available { get; init; }
}

public sealed class WidgetManager
{
 readonly string root;
 public string Root => root;
 public string WidgetsDirectory => Path.Combine(root,"Widgets");
 public string DataDirectory(Widget widget) => Path.Combine(root,"Data",widget.Id);
 public string WidgetDirectory(Widget widget) => Child(WidgetsDirectory,widget.Id);
 public string Executable(Widget widget) => Child(WidgetDirectory(widget),widget.Executable);
 static readonly JsonSerializerOptions Json = new(){PropertyNameCaseInsensitive=true,WriteIndented=true};

 public WidgetManager(string directory) { root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); }
 public IReadOnlyList<Widget> ReadCatalog()
 {
  var catalog=JsonSerializer.Deserialize<List<Widget>>(File.ReadAllText(Path.Combine(root,"Catalog","widgets.json")),Json) ?? new();
  var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var widget in catalog)
  {
   ValidateId(widget.Id);
   if(!ids.Add(widget.Id))throw new InvalidDataException("В каталоге повторяется виджет.");
   if(widget.Available)
   {
    if(string.IsNullOrWhiteSpace(widget.Name)||string.IsNullOrWhiteSpace(widget.Version))throw new InvalidDataException("В каталоге не хватает названия или версии виджета.");
    _=Child(Path.Combine(root,"Packages"),widget.Package); _=Executable(widget);
   }
  }
  return catalog;
 }
 public static void ValidateId(string id)
 {
  if(!Regex.IsMatch(id,"^[a-z0-9][a-z0-9-]{0,63}$",RegexOptions.CultureInvariant))throw new InvalidDataException("Недопустимое имя папки виджета.");
 }
 public static string Child(string parent,string relative)
 {
  if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(':'))throw new InvalidDataException("Недопустимый путь в пакете виджета.");
  var normalized=relative.Replace('/',Path.DirectorySeparatorChar);
  if(normalized.Split(Path.DirectorySeparatorChar).Any(x=>x==".."||x=="."||x.EndsWith(' ')||x.EndsWith('.')))throw new InvalidDataException("Недопустимый путь в пакете виджета.");
  var full=Path.GetFullPath(Path.Combine(parent,normalized));
  if(!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Путь выходит за папку виджета.");
  return full;
 }
 public static void RejectLinks(string path)
 {
  for(var directory=new DirectoryInfo(Path.GetFullPath(path));directory!=null;directory=directory.Parent)
  {
   FileAttributes attributes;
   try{attributes=File.GetAttributes(directory.FullName);}
   catch(FileNotFoundException){continue;}
   catch(DirectoryNotFoundException){continue;}
   if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Папка установки не должна содержать ссылки на другие папки.");
  }
 }
 public bool HasInstallation(Widget widget)
 {
  try
  {
   var marker=Path.Combine(WidgetDirectory(widget),".widget-install.json");
   if(!File.Exists(marker))return false;
   var installed=JsonSerializer.Deserialize<Widget>(File.ReadAllText(marker),Json);
   return installed?.Id==widget.Id&&installed.Executable.Equals(widget.Executable,StringComparison.OrdinalIgnoreCase);
  }
  catch{return false;}
 }
 public bool IsInstalled(Widget widget)=>HasInstallation(widget)&&File.Exists(Executable(widget));
 public string InstanceName(Widget widget) => InstanceNameForData(DataDirectory(widget));
 public static string InstanceNameForData(string dataDirectory)
 {
  var key=Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)).ToUpperInvariant();
  return "Local\\MiniPlayer."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
 }
 public bool IsRunning(Widget widget)
 {
  if(!Mutex.TryOpenExisting(InstanceName(widget),out var mutex))return false;
  using(mutex)
  {
   try{if(!mutex.WaitOne(0))return true;mutex.ReleaseMutex();return false;}
   catch(AbandonedMutexException){mutex.ReleaseMutex();return false;}
  }
 }
 public async Task InstallAsync(Widget widget)
 {
  ValidateId(widget.Id);
  if(!widget.Available)throw new InvalidOperationException("Этот виджет пока недоступен.");
  await Task.Run(()=>
  {
   RejectLinks(root); RejectLinks(WidgetsDirectory);
   var destination=WidgetDirectory(widget);
   if(Directory.Exists(destination))throw new IOException("Папка виджета уже существует. Удалите виджет через лаунчер перед повторной установкой.");
   var package=Child(Path.Combine(root,"Packages"),widget.Package);
   if(!File.Exists(package))throw new FileNotFoundException("Пакет виджета отсутствует. Переустановите лаунчер из полного установщика.");
   RejectLinks(Path.GetDirectoryName(package)!);
   if((File.GetAttributes(package)&FileAttributes.ReparsePoint)!=0)throw new IOException("Пакет виджета не должен быть ссылкой.");
   Directory.CreateDirectory(WidgetsDirectory);
   var staging=Child(WidgetsDirectory,".install-"+widget.Id+"-"+Guid.NewGuid().ToString("N"));
   try
   {
    Directory.CreateDirectory(staging);
    using var zip=ZipFile.OpenRead(package);
    if(zip.Entries.Count>5000)throw new InvalidDataException("В пакете слишком много файлов.");
    long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach(var entry in zip.Entries)
    {
     if((total+=entry.Length)>512L*1024*1024)throw new InvalidDataException("Пакет виджета слишком большой.");
     if((entry.ExternalAttributes>>16&0xF000)==0xA000)throw new InvalidDataException("Пакет содержит ссылку.");
     string name=entry.FullName.Replace('\\','/');
     var output=Child(staging,name.TrimEnd('/'));
     if(!names.Add(output))throw new InvalidDataException("Пакет содержит повторяющиеся пути.");
     if(name.EndsWith('/')){Directory.CreateDirectory(output);continue;}
     Directory.CreateDirectory(Path.GetDirectoryName(output)!);
     using var source=entry.Open();using var target=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None);source.CopyTo(target);
    }
    if(!File.Exists(Child(staging,widget.Executable)))throw new InvalidDataException("В пакете отсутствует приложение виджета.");
    File.WriteAllText(Path.Combine(staging,".widget-install.json"),JsonSerializer.Serialize(widget,Json));
    Directory.Move(staging,destination);
   }
   finally{if(Directory.Exists(staging))DeleteOwnedTree(staging,WidgetsDirectory);}
  });
 }
 public void Launch(Widget widget,string? mode=null)=>LaunchAsync(widget,mode).GetAwaiter().GetResult();
 public async Task LaunchAsync(Widget widget,string? mode=null)
 {
  if(!IsInstalled(widget))throw new InvalidOperationException("Сначала установите виджет.");
  RejectLinks(WidgetDirectory(widget));RejectLinks(DataDirectory(widget));
  var runtime=Path.Combine(root,"Runtime","dotnet.exe");
  if(!File.Exists(runtime))throw new FileNotFoundException("Не хватает компонентов запуска. Переустановите лаунчер.");
  var executable=Executable(widget);var dll=Path.ChangeExtension(executable,".dll");
  if(!File.Exists(dll))throw new FileNotFoundException("Файлы виджета повреждены. Удалите и установите его заново.");
  var start=new ProcessStartInfo(runtime){WorkingDirectory=Path.GetDirectoryName(executable)!,UseShellExecute=false,CreateNoWindow=true};
  start.ArgumentList.Add(dll);
  start.ArgumentList.Add("--data-dir");start.ArgumentList.Add(DataDirectory(widget));
  start.ArgumentList.Add("--launcher-root");start.ArgumentList.Add(root);
  start.ArgumentList.Add("--show-existing");
  if(mode!=null)start.ArgumentList.Add(mode);
  using var process=Process.Start(start)??throw new IOException("Не удалось запустить виджет.");
  await Task.Delay(650).ConfigureAwait(false);
  if(process.HasExited&&(process.ExitCode!=0||!IsRunning(widget)))throw new IOException("Виджет не смог запуститься. Попробуйте удалить и установить его заново.");
 }
 public async Task StopAsync(Widget widget)
 {
  if(!IsRunning(widget))return;
  if(!EventWaitHandle.TryOpenExisting(InstanceName(widget)+".Stop",out var stop))throw new IOException("Закройте виджет через его значок возле часов и повторите действие.");
  using(stop)stop.Set();
  for(int i=0;i<60;i++){await Task.Delay(100);if(!IsRunning(widget))return;}
  throw new IOException("Виджет ещё завершает работу. Подождите и повторите действие.");
 }
 public async Task RemoveAsync(Widget widget)
 {
  ValidateId(widget.Id);var path=WidgetDirectory(widget);
  if(!File.Exists(Path.Combine(path,".widget-install.json")))throw new IOException("Папка не отмечена как установленный виджет. Автоматическое удаление отменено.");
  var installed=JsonSerializer.Deserialize<Widget>(File.ReadAllText(Path.Combine(path,".widget-install.json")),Json);
  if(installed?.Id!=widget.Id)throw new IOException("Не удалось проверить папку виджета.");
  await StopAsync(widget);
  await Task.Run(()=>DeleteOwnedTree(path,WidgetsDirectory));
 }
 public static void DeleteOwnedTree(string path,string parent)
 {
  path=Path.GetFullPath(path);parent=Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
  if(!path.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||path.Equals(parent,StringComparison.OrdinalIgnoreCase))throw new IOException("Удаление вне папки виджетов запрещено.");
  RejectLinks(parent);
  // Inspect the complete tree before deleting anything. Never follow junctions.
  void Check(string folder)
  {
   if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("В папке виджета обнаружена ссылка. Удаление остановлено.");
   foreach(var item in Directory.EnumerateFileSystemEntries(folder))
   {
    var attr=File.GetAttributes(item);
    if((attr&FileAttributes.ReparsePoint)!=0)throw new IOException("В папке виджета обнаружена ссылка. Удаление остановлено.");
    if((attr&FileAttributes.Directory)!=0)Check(item);
   }
  }
  Check(path);
  void DeleteWithRetry(Action operation)
  {
   for(int attempt=0;;attempt++)
   {
    try{operation();return;}
    catch(IOException) when(attempt<29){Thread.Sleep(100);}
    catch(UnauthorizedAccessException) when(attempt<29){Thread.Sleep(100);}
   }
  }
  void DeleteContents(string folder,bool top)
  {
   foreach(var entry in Directory.EnumerateFileSystemEntries(folder))
   {
    if(top&&Path.GetFileName(entry)==".widget-install.json")continue;
    if(Directory.Exists(entry)){DeleteContents(entry,false);DeleteWithRetry(()=>Directory.Delete(entry));}
    else DeleteWithRetry(()=>{File.SetAttributes(entry,FileAttributes.Normal);File.Delete(entry);});
   }
  }
  DeleteContents(path,true);
  // Keep the ownership marker until the last file is gone, so interrupted removals stay repairable.
  var marker=Path.Combine(path,".widget-install.json");
  if(File.Exists(marker))DeleteWithRetry(()=>{File.SetAttributes(marker,FileAttributes.Normal);File.Delete(marker);});
  DeleteWithRetry(()=>Directory.Delete(path));
 }
}
