using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace WidgetLauncher;

internal static class HubBackupRestore
{
 internal sealed class CommittedException(string message,Exception cause):IOException(message,cause);
 sealed class WidgetRestartException(string message):IOException(message);
 const string DirectoryName=".hub-restore";
 sealed record Journal(int Version,string[] Install,string[] Running);
 static FileStream Enter(string root){
  string file=WidgetManager.Child(root,DirectoryName+".lock");WidgetManager.RejectLinks(file);
  return new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose);
 }
 static string Part(string folder,string name){
  string path=WidgetManager.Child(folder,name);WidgetManager.RejectLinks(path);return path;
 }
 static Journal ReadJournal(string folder){
  string record=Part(folder,"restore.json");
  if(!File.Exists(record)||new FileInfo(record).Length>65536)throw new IOException("Не удалось проверить папку восстановления: "+folder);
  var plan=JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(record));
  if(plan?.Version!=1||plan.Install==null||plan.Running==null)throw new InvalidDataException("Повреждён журнал восстановления.");
  return plan;
 }
 static void CleanFinished(string root){
  string finished=Part(root,DirectoryName+".finished");
  if(!Directory.Exists(finished))return;
  if(Directory.EnumerateFileSystemEntries(finished).Any()){
   _=ReadJournal(finished);
   foreach(string path in Directory.EnumerateFileSystemEntries(finished)){
    if(Path.GetFileName(path)=="restore.json")continue;
    WidgetManager.RejectLinks(path);
    if(Directory.Exists(path))WidgetManager.DeleteOwnedTree(path,finished);
    else File.Delete(path);
   }
   File.Delete(Part(finished,"restore.json"));
  }
  Directory.Delete(finished);
 }
 // Only the current product's user data is replaced. Other local files stay with their owner.
 static void CopyExtras(string from,string to,string relative=""){
  Directory.CreateDirectory(to);
  foreach(string entry in Directory.EnumerateFileSystemEntries(from)){
   WidgetManager.RejectLinks(entry);
   string name=Path.GetFileName(entry),key=relative+name,target=Path.Combine(to,name);
   if(Directory.Exists(entry))CopyExtras(entry,target,key+"/");
   else if(!HubBackupArchive.Portable(key)&&key!="Customization/mini-player/apply.lock"&&key!="mini-player/preset-applied.json")
    File.Copy(entry,target);
  }
 }
 static Widget[] Resolve(WidgetManager manager,string[] ids){
  var known=manager.ReadCatalog().Where(w=>w.Available).ToDictionary(w=>w.Id);
  return ids.Select(id=>known.TryGetValue(id,out var widget)?widget:throw new InvalidDataException("В этом каталоге отсутствует виджет «"+id+"».")).ToArray();
 }
 static async Task Resume(WidgetManager manager,string[] running){
  var failures=new List<string>();
  foreach(var widget in Resolve(manager,running)){
   try{if(!manager.IsRunning(widget))await manager.LaunchAsync(widget).ConfigureAwait(false);}
   catch(Exception ex){failures.Add(widget.Name+" — "+ex.Message);}
  }
  if(failures.Count>0)throw new WidgetRestartException("Не удалось снова запустить виджеты: "+string.Join(", ",failures));
 }
 static async Task Finish(WidgetManager manager,string stage,Journal plan,bool committed){
  string previous=Part(stage,"previous"),data=Part(manager.Root,"Data");
  if(!committed){
   if(Directory.Exists(previous)){
    if(Directory.Exists(data))Directory.Move(data,Part(stage,"discarded"));
    Directory.Move(previous,data);
   }
   foreach(var widget in Resolve(manager,plan.Install))
    if(manager.HasInstallation(widget))await manager.RemoveAsync(widget).ConfigureAwait(false);
   if(File.Exists(Part(stage,"switching")))Autostart.Apply(manager.Root,HubPreferences.Load(manager.Root));
  }
  // Detach the completed transaction before deleting any old data or commit marker.
  // A shutdown during cleanup must never turn a finished restore into a rollback.
  Directory.Move(stage,Part(manager.Root,DirectoryName+".finished"));
  try{await Resume(manager,plan.Running).ConfigureAwait(false);}
  finally{CleanFinished(manager.Root);}
 }

 // A second activation can arrive while the original hub is restoring. Only the lock owner recovers.
 internal static bool RecoverIfNeeded(string root,Action<string>? warn=null){
  string stage=Part(root,DirectoryName);
  if(!Directory.Exists(stage)&&!Directory.Exists(Part(root,DirectoryName+".finished")))return true;
  FileStream guard;
  try{guard=Enter(root);}catch(IOException ex)when((ex.HResult&0xffff) is 32 or 33){return false;}
  using(guard){
   CleanFinished(root);
   if(!Directory.Exists(stage))return true;
   try{Finish(new WidgetManager(root),stage,ReadJournal(stage),File.Exists(Part(stage,"committed"))).GetAwaiter().GetResult();}
   catch(WidgetRestartException ex){warn?.Invoke(ex.Message);}
  }
  return true;
 }

 internal static async Task RestoreAsync(WidgetManager manager,BackupPreview preview){
  using var guard=Enter(manager.Root);
  CleanFinished(manager.Root);
  string stage=Part(manager.Root,DirectoryName);
  if(Directory.Exists(stage))throw new IOException("Перезапустите хаб, чтобы завершить предыдущее восстановление.");
  var missing=Resolve(manager,preview.Manifest.Widgets).Where(w=>!manager.IsInstalled(w)).ToArray();
  if(missing.Any(w=>Directory.Exists(manager.WidgetDirectory(w))))throw new IOException("В папке недостающего виджета уже есть файлы. Проверьте его установку.");
  var running=manager.ReadCatalog().Where(w=>w.Available&&manager.IsRunning(w)).ToArray();
  var plan=new Journal(1,missing.Select(w=>w.Id).ToArray(),running.Select(w=>w.Id).ToArray());
  string preparing=Part(manager.Root,DirectoryName+".prepare-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(preparing);
  try{
   File.WriteAllBytes(Part(preparing,"restore.json"),JsonSerializer.SerializeToUtf8Bytes(plan));
   Directory.Move(preparing,stage);
  }finally{if(Directory.Exists(preparing))WidgetManager.DeleteOwnedTree(preparing,manager.Root);}
  try{
   string next=Part(stage,"next"),data=Part(manager.Root,"Data");
   Directory.CreateDirectory(next);
   await Task.Run(()=>HubBackupArchive.ReadArchive(preview.FileName,next,preview.Fingerprint)).ConfigureAwait(false);
   foreach(var widget in running)await manager.StopAsync(widget).ConfigureAwait(false);
   foreach(var widget in missing)await manager.InstallAsync(widget).ConfigureAwait(false);
   await Task.Run(()=>{
    Directory.CreateDirectory(data);CopyExtras(data,next);
    File.WriteAllText(Part(stage,"switching"),"1");
    Directory.Move(data,Part(stage,"previous"));
    Directory.Move(next,data);
    Autostart.Apply(manager.Root,HubPreferences.Load(manager.Root));
    File.WriteAllText(Part(stage,"committed"),"1");
   }).ConfigureAwait(false);
  }catch(Exception ex){
   try{await Finish(manager,stage,plan,false).ConfigureAwait(false);}
   catch(WidgetRestartException restart){throw new IOException("Восстановление отменено. "+restart.Message,ex);}
   catch(Exception rollback){throw new IOException("Восстановление прервано. Перезапустите хаб для завершения отката. "+rollback.Message,ex);}
   throw;
  }
  try{await Finish(manager,stage,plan,true).ConfigureAwait(false);}
  catch(Exception ex){throw new CommittedException("Данные восстановлены. "+ex.Message,ex);}
 }
}
