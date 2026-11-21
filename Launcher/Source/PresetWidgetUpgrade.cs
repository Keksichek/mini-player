using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace WidgetLauncher;

// The preset renderer is linked into MiniPlayer.dll; updating it does not touch widget settings or extra files.
internal static class PresetWidgetUpgrade
{
 const string Marker="preset-format.json";
 static bool Current(string folder){
  string path=Path.Combine(folder,Marker);WidgetManager.RejectLinks(path);
  try{if(!File.Exists(path)||new FileInfo(path).Length>4096)return false;using var doc=JsonDocument.Parse(File.ReadAllText(path));return doc.RootElement.TryGetProperty("Version",out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out int version)&&version>=4;}catch(JsonException){return false;}
 }
 public static async Task EnsureAsync(WidgetManager manager,Widget widget){
  if(widget.Id!="mini-player"||!manager.IsInstalled(widget))return;
  string app=Path.GetDirectoryName(manager.Executable(widget))!;WidgetManager.RejectLinks(app);
  if(Current(app))return;
  string destination=Path.Combine(app,"MiniPlayer.dll"),marker=Path.Combine(app,Marker);WidgetManager.RejectLinks(destination);WidgetManager.RejectLinks(marker);
  string package=WidgetManager.Child(Path.Combine(manager.Root,"Packages"),widget.Package);WidgetManager.RejectLinks(package);
  string stage=WidgetManager.Child(manager.WidgetsDirectory,".preset-upgrade-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
  string staged=Path.Combine(stage,"MiniPlayer.dll"),backup=Path.Combine(stage,"previous.dll"),stagedMarker=Path.Combine(stage,Marker);
  bool wasRunning=manager.IsRunning(widget),replaced=false,stopped=false,complete=false;
  if(File.Exists(marker)&&new FileInfo(marker).Length>4096)throw new IOException("Файл формата пресетов повреждён.");
  byte[]? previousMarker=File.Exists(marker)?File.ReadAllBytes(marker):null;
  try{
   using(var archive=ZipFile.OpenRead(package)){
    ZipArchiveEntry? dll=null,format=null;
    foreach(var entry in archive.Entries){if(entry.FullName.Replace('\\','/')=="App/MiniPlayer.dll"){if(dll!=null)throw new IOException("В пакете повторяются файлы плеера.");dll=entry;}if(entry.FullName.Replace('\\','/')=="App/"+Marker){if(format!=null)throw new IOException("В пакете повторяется формат пресета.");format=entry;}}
    if(dll==null||format==null||dll.Length<=0||dll.Length>20*1024*1024||format.Length>4096)throw new IOException("Установите свежую локальную сборку лаунчера: в ней нет подходящего плеера.");
    foreach(var pair in new[]{(dll,staged),(format,stagedMarker)}){
     if((pair.Item1.ExternalAttributes>>16&0xF000)==0xA000)throw new IOException("Файл пакета не должен быть ссылкой.");
     using var input=pair.Item1.Open();using var output=new FileStream(pair.Item2,FileMode.CreateNew,FileAccess.Write,FileShare.None);input.CopyTo(output);
    }
   }
   if(!Current(stage)||AssemblyName.GetAssemblyName(staged).Name!="MiniPlayer")throw new IOException("Пакет содержит неподходящую сборку плеера.");
   await manager.StopAsync(widget);stopped=true;
   // StopAsync observes the instance mutex; retry the brief interval before Windows releases the DLL mapping.
   for(int attempt=0;;attempt++)try{File.Replace(staged,destination,backup,true);replaced=true;break;}catch(IOException)when(attempt<30){await Task.Delay(100);}
   File.Copy(stagedMarker,marker,true);
   await manager.LaunchAsync(widget);
   if(!System.Threading.EventWaitHandle.TryOpenExisting(manager.InstanceName(widget)+".ApplyPreset",out var capability))throw new IOException("Новый плеер не подтвердил поддержку пресетов.");
   capability.Dispose();complete=true;
  }catch(Exception failure){
   if(replaced){
    try{
     await manager.StopAsync(widget);
     for(int attempt=0;;attempt++)try{File.Replace(backup,destination,null,true);break;}catch(IOException)when(attempt<30){await Task.Delay(100);}
     if(previousMarker==null){if(File.Exists(marker))File.Delete(marker);}else File.WriteAllBytes(marker,previousMarker);
     if(wasRunning)await manager.LaunchAsync(widget);
    }catch(Exception rollback){throw new IOException("Не удалось завершить обновление плеера. Резервная копия сохранена в "+stage+". "+rollback.Message,failure);}
   }
   if(!replaced&&stopped&&wasRunning){try{await manager.LaunchAsync(widget);}catch(Exception resume){throw new IOException("Файлы плеера не изменены, но не удалось возобновить его запуск: "+resume.Message,failure);}}
   throw new IOException("Плеер не обновлён: "+failure.Message,failure);
  }finally{
   // Never remove a backup left by a failed rollback.
   if(complete||!replaced||!File.Exists(backup)){
    foreach(string file in new[]{staged,backup,stagedMarker})if(File.Exists(file))File.Delete(file);
    if(Directory.Exists(stage)&&Directory.GetFileSystemEntries(stage).Length==0)Directory.Delete(stage);
   }
  }
 }
}
