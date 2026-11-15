using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using VibeWidgetsUpdates;
using File=WidgetLauncherSetup.HubFile;
using Directory=WidgetLauncherSetup.HubDirectory;

namespace WidgetLauncherSetup
{
 public sealed class UpdateResult {public bool Updated,RolledBack;public string Message;public long BuildNumber;public string[] ResumeIds;}
 public sealed class UpdateFile {public string Relative,OldHash,NewHash;public bool HadOriginal;}
 public sealed class UpdateHash {public string Relative,Hash;}
 public sealed class UpdateWidget {public string Id;public List<UpdateHash> OldFiles,NewFiles;}
 public sealed class UpdateJournal {
  public string ProductId,Root,InstallId,Phase;public bool TestInstallation,DataExisted;
  public long BuildNumber;public string[] Resume;public List<UpdateFile> Core;public List<UpdateWidget> Widgets;
  public List<UpdateHash> DataFiles;public string OldMarkerHash,NewMarkerHash;
 }
 public static class UpdateEngine
 {
  public const string TransactionName=".update-transaction",StateName="update-state.json";
  static JavaScriptSerializer Json {get{return new JavaScriptSerializer{MaxJsonLength=16*1024*1024};}}
  static string At(string root,string relative){return InstallEngine.Child(root,relative);}
  static string Transaction(string root){return At(root,TransactionName);}
  public static bool Pending(string root){return File.Exists(At(Transaction(root),"journal.json"));}
  static string Digest(string path){InstallEngine.NoLinks(path);using(var stream=new FileStream(HubPath.Disk(path),FileMode.Open,FileAccess.Read,FileShare.Read))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
  static void Put(string path,string text){InstallEngine.NoLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,text,new UTF8Encoding(false));}
  static void DeleteFile(string path){InstallEngine.NoLinks(path);if(!File.Exists(path))return;File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);}
  static void Move(string source,string destination){InstallEngine.NoLinks(source);InstallEngine.NoLinks(destination);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Move(source,destination);}
  static void MoveDirectory(string source,string destination){InstallEngine.NoLinks(source);InstallEngine.NoLinks(destination);Directory.CreateDirectory(Path.GetDirectoryName(destination));System.IO.Directory.Move(HubPath.Disk(source),HubPath.Disk(destination));}
  static void Copy(string source,string destination){InstallEngine.NoLinks(source);InstallEngine.NoLinks(destination);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(source,destination);}
  static void Collect(string root,string folder,List<UpdateHash> result){
   InstallEngine.NoLinks(folder);if(!Directory.Exists(folder))return;
   foreach(string path in Directory.GetFiles(folder)){InstallEngine.NoLinks(path);result.Add(new UpdateHash{Relative=InstallEngine.Normalize(path).Substring(InstallEngine.Normalize(root).Length+1),Hash=Digest(path)});}
   foreach(string path in Directory.GetDirectories(folder))Collect(root,path,result);
  }
  static List<UpdateHash> Files(string root){var result=new List<UpdateHash>();Collect(root,root,result);return result;}
  static void CopyTree(string source,string destination){InstallEngine.NoLinks(source);Directory.CreateDirectory(destination);foreach(string file in Directory.GetFiles(source))Copy(file,At(destination,Path.GetFileName(file)));foreach(string folder in Directory.GetDirectories(source))CopyTree(folder,At(destination,Path.GetFileName(folder)));}
  static void RemoveTree(string path,string owner){
   path=InstallEngine.Normalize(path);owner=InstallEngine.Normalize(owner);if(!path.StartsWith(owner+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Удаление вне временной папки запрещено.");InstallEngine.NoLinks(path);if(!Directory.Exists(path))return;
   foreach(string file in Directory.GetFiles(path))DeleteFile(file);foreach(string directory in Directory.GetDirectories(path))RemoveTree(directory,owner);Directory.Delete(path);
  }
  static bool Reserved(string relative){string p=relative.Replace('/','\\');return p.Equals("Data",StringComparison.OrdinalIgnoreCase)||p.StartsWith("Data\\",StringComparison.OrdinalIgnoreCase)||p.Equals("Widgets",StringComparison.OrdinalIgnoreCase)||p.StartsWith("Widgets\\",StringComparison.OrdinalIgnoreCase)||p.StartsWith(".update-",StringComparison.OrdinalIgnoreCase)||p.Equals(InstallEngine.MarkerName,StringComparison.OrdinalIgnoreCase)||p.Equals(StateName,StringComparison.OrdinalIgnoreCase);}
  static List<string> Extract(Stream stream,string destination,bool core){
   var files=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
   using(var zip=new ZipArchive(stream,ZipArchiveMode.Read,true)){
    if(zip.Entries.Count>(core?40000:5000))throw new IOException("В обновлении слишком много файлов.");
    foreach(var entry in zip.Entries){string relative=entry.FullName.Replace('/','\\');bool directory=relative.EndsWith("\\",StringComparison.Ordinal);relative=relative.TrimEnd('\\');string target=At(destination,relative);
     if((entry.ExternalAttributes>>16&0xF000)==0xA000)throw new IOException("Обновление содержит ссылку.");
     if(!seen.Add(relative))throw new IOException("Обновление содержит повторяющийся путь.");
     if(core&&Reserved(relative))throw new IOException("Обновление пытается заменить пользовательские данные.");
     if((total+=entry.Length)>(core?2000000000L:512L*1024*1024))throw new IOException("Обновление слишком большое.");
     if(directory){Directory.CreateDirectory(target);continue;}
     Directory.CreateDirectory(Path.GetDirectoryName(target));using(var input=entry.Open())using(var output=File.Create(target))input.CopyTo(output);files.Add(relative);
    }
   }return files;
  }
  static Dictionary<string,object> Dictionary(object value){var dictionary=value as Dictionary<string,object>;if(dictionary==null)throw new IOException("Описание виджета повреждено.");return dictionary;}
  static string Text(Dictionary<string,object> value,string key){object text;foreach(var name in value.Keys)if(String.Equals(name,key,StringComparison.OrdinalIgnoreCase)&&value.TryGetValue(name,out text))return text as string;return null;}
  static void ManagedAssembly(string path){
   if(!File.Exists(path))throw new IOException("В обновлении отсутствует приложение: "+Path.GetFileName(path));string temporary=null,probe=path;
   try{if(path.Length>=240){temporary=Path.Combine(Path.GetTempPath(),"VibeWidgetsHub-Metadata-"+Guid.NewGuid().ToString("N"));InstallEngine.NoLinks(temporary);Directory.CreateDirectory(temporary);probe=At(temporary,"assembly.dll");Copy(path,probe);}AssemblyName.GetAssemblyName(probe);}
   catch(Exception ex){throw new IOException("Приложение в обновлении повреждено: "+Path.GetFileName(path),ex);}
   finally{if(temporary!=null){try{DeleteFile(probe);Directory.Delete(temporary);}catch{}}}
  }
  static List<Dictionary<string,object>> Catalog(string file){var data=Json.DeserializeObject(File.ReadAllText(file)) as object[];if(data==null)throw new IOException("Каталог обновления повреждён.");return data.Select(Dictionary).ToList();}
  static void PrepareWidgets(string root,string stage,UpdateJournal journal){
   var catalog=Catalog(At(stage,"new\\Catalog\\widgets.json"));
   foreach(string folder in Directory.Exists(At(root,"Widgets"))?Directory.GetDirectories(At(root,"Widgets")):new string[0]){
    string installed=At(folder,".widget-install.json");if(!File.Exists(installed))continue;
    InstallEngine.NoLinks(folder);var old=Dictionary(Json.DeserializeObject(File.ReadAllText(installed)));string id=Text(old,"Id");if(String.IsNullOrEmpty(id)||Path.GetFileName(folder)!=id)throw new IOException("Не удалось проверить установленный виджет.");
    var next=catalog.SingleOrDefault(x=>String.Equals(Text(x,"Id"),id,StringComparison.OrdinalIgnoreCase));if(next==null)throw new IOException("В обновлении нет установленного виджета: "+id);
    string executable=Text(next,"Executable"),package=Text(next,"Package"),oldPackage=Text(old,"Package");if(String.IsNullOrEmpty(executable)||String.IsNullOrEmpty(package)||String.IsNullOrEmpty(oldPackage))throw new IOException("Пакет виджета не указан.");
    string target=At(stage,"new\\Widgets\\"+id);Directory.CreateDirectory(target);
    using(var stream=new FileStream(HubPath.Disk(At(At(stage,"new\\Packages"),package)),FileMode.Open,FileAccess.Read,FileShare.Read))Extract(stream,target,false);
    if(!File.Exists(At(target,executable)))throw new IOException("В обновлении нет приложения виджета: "+id);
    ManagedAssembly(Path.ChangeExtension(At(target,executable),".dll"));
    var owned=new HashSet<string>(StringComparer.OrdinalIgnoreCase){".widget-install.json"};
    using(var stream=new FileStream(HubPath.Disk(At(At(root,"Packages"),oldPackage)),FileMode.Open,FileAccess.Read,FileShare.Read))using(var archive=new ZipArchive(stream,ZipArchiveMode.Read))foreach(var entry in archive.Entries){string relative=entry.FullName.Replace('/','\\').TrimEnd('\\');At(folder,relative);owned.Add(relative);}
    var oldFiles=Files(folder);
    foreach(var file in oldFiles.Where(x=>!owned.Contains(x.Relative))){string extra=At(target,file.Relative);if(File.Exists(extra)||Directory.Exists(extra))throw new IOException("Новый виджет заменяет посторонний файл: "+id+"\\"+file.Relative);Copy(At(folder,file.Relative),extra);}
    Put(At(target,".widget-install.json"),Json.Serialize(next));
    journal.Widgets.Add(new UpdateWidget{Id=id,OldFiles=oldFiles,NewFiles=Files(target)});
   }
  }
  public static long CurrentBuild(string root){string path=At(root,StateName);InstallEngine.NoLinks(path);if(!File.Exists(path))return 0;try{var state=Dictionary(Json.DeserializeObject(File.ReadAllText(path)));object value;if(!state.TryGetValue("BuildNumber",out value)||!(value is int||value is long)||Convert.ToInt64(value)<=0)throw new IOException("Некорректный номер установленной сборки.");return Convert.ToInt64(value);}catch(Exception ex){throw new IOException("Сведения об установленной версии повреждены. Обновление не начато.",ex);}}
  static void WriteJournal(string root,UpdateJournal journal){string stage=Transaction(root),file=At(stage,"journal.json"),temp=At(stage,"journal.tmp");Put(temp,Json.Serialize(journal));if(File.Exists(file))System.IO.File.Replace(HubPath.Disk(temp),HubPath.Disk(file),null);else File.Move(temp,file);}
  static UpdateJournal ReadJournal(string root){
   string path=At(Transaction(root),"journal.json");InstallEngine.NoLinks(path);if(new FileInfo(HubPath.Disk(path)).Length>16*1024*1024)throw new IOException("Журнал обновления повреждён.");var journal=Json.Deserialize<UpdateJournal>(File.ReadAllText(path));
   if(journal==null||journal.ProductId!=InstallEngine.ProductId||journal.Root!=root||journal.InstallId!=InstallEngine.Hash(root)||journal.Core==null||journal.Widgets==null||journal.DataFiles==null||!new[]{"prepared","applying","health","committed","rolledback"}.Contains(journal.Phase))throw new IOException("Не удалось проверить журнал обновления.");
   foreach(var file in journal.Core){At(root,file.Relative);if(Reserved(file.Relative)&&file.Relative!=StateName)throw new IOException("Недопустимый файл в журнале.");}
   foreach(var widget in journal.Widgets){At(At(root,"Widgets"),widget.Id);foreach(var file in widget.OldFiles.Concat(widget.NewFiles))At(At(At(root,"Widgets"),widget.Id),file.Relative);}
   foreach(var file in journal.DataFiles)At(At(root,"Data"),file.Relative);return journal;
  }
  static bool Hold(Mutex gate){try{return gate.WaitOne(0);}catch(AbandonedMutexException){return true;}}
  static void Stop(string root){InstallEngine.StopApplications(root);for(int i=0;i<80;i++){try{InstallEngine.CheckStopped(root);return;}catch(IOException){if(i==79)throw;Thread.Sleep(250);}}}
  public static UpdateResult Apply(string requested,string feed,bool test,string[] resume,Func<string,string[],bool> health,Action<int,string> progress){
   string root=InstallEngine.ValidateDestination(requested);var marker=InstallEngine.ReadMarker(root);if(marker.TestInstallation!=test)throw new IOException("Тип установки не соответствует режиму обновления.");
   using(var gate=new Mutex(false,"Local\\WidgetLauncher.Install."+InstallEngine.Hash(root))){bool held=false;try{held=Hold(gate);if(!held)throw new IOException("Установка или обновление уже выполняется.");return ApplyCore(root,feed,test,resume,health,progress);}finally{if(held)gate.ReleaseMutex();}}
  }
  static UpdateResult ApplyCore(string root,string feed,bool test,string[] resume,Func<string,string[],bool> health,Action<int,string> progress){
   string stage=Transaction(root);if(Directory.Exists(stage)){if(Pending(root))throw new IOException("Сначала завершите восстановление предыдущего обновления.");MoveDirectory(stage,At(root,".update-preserved-"+Guid.NewGuid().ToString("N")));}
   progress(2,"Проверяем подпись и пакет…");var manifest=UpdateProtocol.Read(feed);string package=UpdateProtocol.VerifyPackage(feed,manifest);
   if(manifest.BuildNumber<=CurrentBuild(root))throw new IOException("Эта сборка уже установлена или старее текущей.");
   Directory.CreateDirectory(stage);var journal=new UpdateJournal{ProductId=InstallEngine.ProductId,Root=root,InstallId=InstallEngine.Hash(root),TestInstallation=test,BuildNumber=manifest.BuildNumber,Phase="prepared",Resume=resume??new string[0],Core=new List<UpdateFile>(),Widgets=new List<UpdateWidget>(),DataFiles=new List<UpdateHash>()};
   bool journalWritten=false;
   try{
    string newer=At(stage,"new");Directory.CreateDirectory(newer);
    using(var stream=new FileStream(package,FileMode.Open,FileAccess.Read,FileShare.Read)){
     using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")!=manifest.Sha256.ToUpperInvariant())throw new IOException("Пакет изменился во время проверки.");stream.Position=0;Extract(stream,newer,true);
    }
    foreach(string required in new[]{"Launcher.exe","Launcher.dll","Launcher.runtimeconfig.json","Update.exe","Uninstall.exe","Runtime\\dotnet.exe","Catalog\\widgets.json"})if(!File.Exists(At(newer,required)))throw new IOException("В обновлении отсутствует компонент: "+required);
    ManagedAssembly(At(newer,"Launcher.dll"));
    ManagedAssembly(At(newer,"Launcher.exe"));ManagedAssembly(At(newer,"Update.exe"));
    // Preparation is read-only with respect to the running installation.
    var marker=InstallEngine.ReadMarker(root);foreach(string relative in marker.Files)if(Reserved(relative)&&relative!=StateName)throw new IOException("Список файлов установки повреждён.");
    PrepareWidgets(root,stage,journal);progress(25,"Закрываем лаунчер и виджеты…");Stop(root);
    // Capture preferences only after old processes have saved their final state.
    foreach(var widget in journal.Widgets)if(!Same(Files(At(root,"Widgets\\"+widget.Id)),widget.OldFiles))throw new IOException("Файлы виджета изменились при подготовке. Повторите обновление.");
    journal.DataExisted=Directory.Exists(At(root,"Data"));if(journal.DataExisted){journal.DataFiles=Files(At(root,"Data"));CopyTree(At(root,"Data"),At(stage,"old\\Data"));if(!Same(Files(At(stage,"old\\Data")),journal.DataFiles))throw new IOException("Настройки изменились при подготовке обновления.");}
    Put(At(newer,StateName),Json.Serialize(new{BuildNumber=manifest.BuildNumber,Version=manifest.Version,Channel=manifest.Channel}));
    var newFiles=Files(newer).Where(x=>!x.Relative.StartsWith("Widgets\\",StringComparison.OrdinalIgnoreCase)).ToList();
    var paths=marker.Files.Concat(newFiles.Select(x=>x.Relative)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p=>p.Equals("Launcher.exe",StringComparison.OrdinalIgnoreCase)?2:p.Equals("Update.exe",StringComparison.OrdinalIgnoreCase)?1:0).ToArray();
    foreach(string relative in paths){string existing=At(root,relative);InstallEngine.NoLinks(existing);if(File.Exists(existing)&&!marker.Files.Contains(relative,StringComparer.OrdinalIgnoreCase))throw new IOException("Обновление заменяет посторонний файл: "+relative);var next=newFiles.SingleOrDefault(x=>String.Equals(x.Relative,relative,StringComparison.OrdinalIgnoreCase));journal.Core.Add(new UpdateFile{Relative=relative,HadOriginal=File.Exists(existing),OldHash=File.Exists(existing)?Digest(existing):null,NewHash=next==null?null:next.Hash});}
    journal.OldMarkerHash=Digest(At(root,InstallEngine.MarkerName));Copy(At(root,InstallEngine.MarkerName),At(stage,"old\\"+InstallEngine.MarkerName));
    marker.Files=newFiles.Select(x=>x.Relative).ToArray();marker.Version=manifest.Version;marker.WidgetIds=marker.WidgetIds.Union(journal.Widgets.Select(x=>x.Id)).ToArray();
    Put(At(newer,InstallEngine.MarkerName),Json.Serialize(marker));journal.NewMarkerHash=Digest(At(newer,InstallEngine.MarkerName));WriteJournal(root,journal);journalWritten=true;
    journal.Phase="applying";WriteJournal(root,journal);progress(40,"Устанавливаем обновление…");
    foreach(var file in journal.Core){
     if(file.HadOriginal&&file.NewHash!=null&&(file.Relative.Equals("Launcher.exe",StringComparison.OrdinalIgnoreCase)||file.Relative.Equals("Update.exe",StringComparison.OrdinalIgnoreCase))){string backup=At(stage,"old\\"+file.Relative);Directory.CreateDirectory(Path.GetDirectoryName(backup));System.IO.File.Replace(HubPath.Disk(At(newer,file.Relative)),HubPath.Disk(At(root,file.Relative)),HubPath.Disk(backup));}
     else{if(file.HadOriginal)Move(At(root,file.Relative),At(stage,"old\\"+file.Relative));if(file.NewHash!=null)Move(At(newer,file.Relative),At(root,file.Relative));}
     progress(50,"Заменяем файлы лаунчера…");
    }
    foreach(var widget in journal.Widgets){MoveDirectory(At(root,"Widgets\\"+widget.Id),At(stage,"old\\Widgets\\"+widget.Id));MoveDirectory(At(newer,"Widgets\\"+widget.Id),At(root,"Widgets\\"+widget.Id));}
    System.IO.File.Replace(HubPath.Disk(At(newer,InstallEngine.MarkerName)),HubPath.Disk(At(root,InstallEngine.MarkerName)),null);
    journal.Phase="health";WriteJournal(root,journal);progress(80,"Проверяем запуск новой версии…");
    if(!health(root,journal.Resume))throw new IOException("Новая версия не подтвердила успешный запуск.");
    journal.Phase="committed";WriteJournal(root,journal);progress(96,"Сохраняем рабочую версию…");Cleanup(root);return new UpdateResult{Updated=true,BuildNumber=manifest.BuildNumber,Message="Обновление установлено. Настройки сохранены."};
   }catch(Exception ex){
    if(journal.Phase=="committed")return new UpdateResult{Updated=true,BuildNumber=manifest.BuildNumber,Message="Обновление установлено. Осталась очистка резервной копии: "+ex.Message};
    if(journalWritten){progress(85,"Возвращаем предыдущую версию…");try{Rollback(root,journal);return new UpdateResult{RolledBack=true,BuildNumber=CurrentBuild(root),Message="Обновление отменено. Предыдущая версия восстановлена. "+ex.Message};}catch(Exception rollback){throw new IOException("Откат не завершён. Резервная копия сохранена; запустите лаунчер для восстановления. "+rollback.Message,ex);}}
    throw;
   }finally{if(!journalWritten&&Directory.Exists(stage))MoveDirectory(stage,At(root,".update-preserved-"+Guid.NewGuid().ToString("N")));}
  }
  static bool Same(IEnumerable<UpdateHash> a,IEnumerable<UpdateHash> b){return a.OrderBy(x=>x.Relative,StringComparer.OrdinalIgnoreCase).Select(x=>x.Relative+":"+x.Hash).SequenceEqual(b.OrderBy(x=>x.Relative,StringComparer.OrdinalIgnoreCase).Select(x=>x.Relative+":"+x.Hash));}
  static void Rollback(string root,UpdateJournal journal){
   string stage=Transaction(root),marker=At(root,InstallEngine.MarkerName),oldMarker=At(stage,"old\\"+InstallEngine.MarkerName);
   if(Digest(oldMarker)!=journal.OldMarkerHash)throw new IOException("Резервная копия установки повреждена.");
   if(!File.Exists(marker))Copy(oldMarker,marker);
   Stop(root);
   foreach(var widget in journal.Widgets){string backup=At(stage,"old\\Widgets\\"+widget.Id),current=At(root,"Widgets\\"+widget.Id);if(!Directory.Exists(backup))continue;if(!Same(Files(backup),widget.OldFiles))throw new IOException("Резервная копия виджета повреждена.");if(Directory.Exists(current))MoveDirectory(current,At(stage,"failed\\Widgets\\"+widget.Id));MoveDirectory(backup,current);}
   foreach(var file in journal.Core.AsEnumerable().Reverse()){
    string backup=At(stage,"old\\"+file.Relative),current=At(root,file.Relative);
    if(File.Exists(backup)){if(Digest(backup)!=file.OldHash)throw new IOException("Резервная копия повреждена: "+file.Relative);if(File.Exists(current)){if(file.NewHash==null||Digest(current)!=file.NewHash)throw new IOException("Файл изменён вне обновления: "+file.Relative);System.IO.File.Replace(HubPath.Disk(backup),HubPath.Disk(current),null);}else Move(backup,current);}
    else if(!file.HadOriginal&&File.Exists(current)){if(file.NewHash==null||Digest(current)!=file.NewHash)throw new IOException("Новый файл изменён извне: "+file.Relative);DeleteFile(current);}
   }
   if(Digest(oldMarker)!=journal.OldMarkerHash)throw new IOException("Резервная копия установки повреждена.");string restoredMarker=At(stage,"restore-marker.json");if(!File.Exists(restoredMarker))Copy(oldMarker,restoredMarker);else if(Digest(restoredMarker)!=journal.OldMarkerHash)throw new IOException("Временная копия установки повреждена.");if(File.Exists(marker))System.IO.File.Replace(HubPath.Disk(restoredMarker),HubPath.Disk(marker),null);else Move(restoredMarker,marker);
   string data=At(root,"Data"),baseline=At(stage,"old\\Data");if(Directory.Exists(baseline)){
    if(!Same(Files(baseline),journal.DataFiles))throw new IOException("Резервная копия настроек повреждена.");
    if(Directory.Exists(data))MoveDirectory(data,At(stage,"failed\\Data"));MoveDirectory(baseline,data);
   }
   string failed=At(stage,"failed");if(Directory.Exists(failed))MoveDirectory(failed,At(root,".update-preserved-"+Guid.NewGuid().ToString("N")));
   journal.Phase="rolledback";WriteJournal(root,journal);try{Cleanup(root);}catch{ /* The restored installation is valid; recovery will retry only cleanup. */ }
  }
  static void Cleanup(string root){
   string stage=Transaction(root);if(!Directory.Exists(stage))return;var journal=ReadJournal(root);var owned=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var file in journal.Core){if(file.NewHash!=null)owned["new\\"+file.Relative]=file.NewHash;if(file.OldHash!=null)owned["old\\"+file.Relative]=file.OldHash;}
   owned["old\\"+InstallEngine.MarkerName]=journal.OldMarkerHash;owned["new\\"+InstallEngine.MarkerName]=journal.NewMarkerHash;
   foreach(var file in journal.DataFiles)owned["old\\Data\\"+file.Relative]=file.Hash;
   foreach(var widget in journal.Widgets){foreach(var file in widget.OldFiles)owned["old\\Widgets\\"+widget.Id+"\\"+file.Relative]=file.Hash;foreach(var file in widget.NewFiles)owned["new\\Widgets\\"+widget.Id+"\\"+file.Relative]=file.Hash;}
   foreach(var file in Files(stage)){string expected;if((owned.TryGetValue(file.Relative,out expected)&&file.Hash==expected)||file.Relative=="journal.json"||file.Relative=="journal.tmp")DeleteFile(At(stage,file.Relative));}
   RemoveEmpty(stage);
   if(Directory.Exists(stage))MoveDirectory(stage,At(root,".update-preserved-"+Guid.NewGuid().ToString("N")));
  }
  static void RemoveEmpty(string path){InstallEngine.NoLinks(path);foreach(string directory in Directory.GetDirectories(path))RemoveEmpty(directory);if(!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);}
  public static UpdateResult Recover(string requested){
   string root=InstallEngine.Normalize(requested);InstallEngine.NoLinks(root);
   using(var gate=new Mutex(false,"Local\\WidgetLauncher.Install."+InstallEngine.Hash(root))){bool held=false;try{held=Hold(gate);if(!held)throw new IOException("Обновление ещё выполняется.");var journal=ReadJournal(root);if(journal.Phase=="committed"||journal.Phase=="rolledback"){Cleanup(root);return new UpdateResult{Updated=journal.Phase=="committed",RolledBack=journal.Phase=="rolledback",Message="Восстановление завершено.",BuildNumber=CurrentBuild(root),ResumeIds=journal.Resume};}Rollback(root,journal);return new UpdateResult{RolledBack=true,Message="Предыдущая версия восстановлена после прерванного обновления.",BuildNumber=CurrentBuild(root),ResumeIds=journal.Resume};}finally{if(held)gate.ReleaseMutex();}}
  }
  public static bool LaunchHealth(string root,string[] resume){
   string token=Guid.NewGuid().ToString("N");using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\WidgetLauncher.UpdateHealth."+token)){
    string command=Quote(At(root,"Launcher.dll"))+" --update-health "+Quote(token)+(resume.Length>0?" --resume-widgets "+Quote(String.Join(",",resume)):"");
    using(var process=Process.Start(new ProcessStartInfo(HubPath.Disk(At(root,"Runtime\\dotnet.exe")),command){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=HubPath.Disk(root)})){
     if(process==null)return false;bool ok=false;var timer=Stopwatch.StartNew();while(timer.ElapsedMilliseconds<15000){if(process.HasExited)break;if(ready.WaitOne(100)){Thread.Sleep(500);ok=!process.HasExited&&InstallEngine.IsRunning("Local\\WidgetLauncher."+InstallEngine.Hash(root));break;}}
     if(!ok){InstallEngine.StopApplications(root);if(!process.WaitForExit(2000)){process.Kill();process.WaitForExit(3000);}}return ok;
    }
   }
  }
  public static string Quote(string value){var command=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){command.Append('\\',slashes*2+1);command.Append(c);}else{command.Append('\\',slashes);command.Append(c);}slashes=0;}command.Append('\\',slashes*2);command.Append('"');return command.ToString();}
 }

 internal static class UpdateProgram
 {
  static string Option(string[] args,string name){int index=Array.IndexOf(args,name);return index>=0&&index+1<args.Length?args[index+1]:"";}
  [STAThread]static int Main(string[] args){
   InstallEngine.ConfigureRuntime();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try{
    if(args.Length>=3&&args[0]=="--test-update"){
     bool fail=Array.IndexOf(args,"--health-fail")>=0;var resume=Option(args,"--resume").Split(new[]{','},StringSplitOptions.RemoveEmptyEntries);var result=UpdateEngine.Apply(args[1],args[2],true,resume,fail?(Func<string,string[],bool>)delegate{return false;}:UpdateEngine.LaunchHealth,delegate{});if(result.RolledBack&&!UpdateEngine.LaunchHealth(args[1],resume))throw new IOException("Предыдущие файлы восстановлены, но их запуск не подтверждён.");Report(args,result);return result.Updated?0:2;
    }
    if(args.Length>=2&&(args[0]=="--update"||args[0]=="--recover"))return Dispatch(args);
    if(args.Length<3||args[0]!="--worker")throw new IOException("Запустите обновление из настроек лаунчера.");
    string root=InstallEngine.Normalize(args[1]),feed=args[2],token=Option(args,"--handoff");Guid transfer;if(!Guid.TryParseExact(token,"N",out transfer))throw new IOException("Не удалось проверить передачу обновления.");
    using(var running=EventWaitHandle.OpenExisting("Local\\WidgetLauncher.Update."+InstallEngine.Hash(root)+".Running")){
     using(var accepted=EventWaitHandle.OpenExisting("Local\\WidgetLauncher.UpdateTransfer."+token))accepted.Set();
     int pid;long ticks;if(Int32.TryParse(Option(args,"--parent"),out pid)&&Int64.TryParse(Option(args,"--parent-time"),out ticks)){try{using(var parent=Process.GetProcessById(pid))if(parent.StartTime.ToUniversalTime().Ticks==ticks&&!parent.WaitForExit(10000))throw new IOException("Не удалось завершить подготовку обновления.");}catch(ArgumentException){}}
     using(var window=new UpdateWindow(root,feed,Option(args,"--resume").Split(new[]{','},StringSplitOptions.RemoveEmptyEntries),Array.IndexOf(args,"--recovering")>=0)){
      Application.Run(window);Report(args,window.Result);return window.Result!=null&&window.Result.Updated?0:window.Result!=null&&window.Result.RolledBack?2:1;
     }
    }
   }catch(Exception ex){string report=Option(args,"--report");if(report.Length>0)File.WriteAllText(report,"FAIL "+ex.Message);if(args.Length==0||args[0]!="--test-update")MessageBox.Show(ex.Message,"Обновление VibeWidgetsHub",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
   finally{if(args.Length>0&&args[0]=="--worker")CleanupHelper();}
  }
  static void Report(string[] args,UpdateResult result){string file=Option(args,"--report");if(file.Length>0&&result!=null)File.WriteAllText(file,(result.Updated?"PASS ":result.RolledBack?"ROLLBACK ":"FAIL ")+result.Message);}
  static int Dispatch(string[] args){
   string root=InstallEngine.Normalize(args[1]);bool recovery=args[0]=="--recover";if(!recovery){var marker=InstallEngine.ReadMarker(root);if(marker.TestInstallation)throw new IOException("Тестовая установка требует тестовый режим обновления.");UpdateProtocol.VerifyPackage(args[2],UpdateProtocol.Read(args[2]));}
   bool first;using(var running=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\WidgetLauncher.Update."+InstallEngine.Hash(root)+".Running",out first)){
    if(!first)throw new IOException("Обновление уже выполняется.");
    string temporary=Path.Combine(Path.GetTempPath(),"VibeWidgetsHub-Update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);string helper=At(temporary,"Update.exe");File.Copy(Assembly.GetExecutingAssembly().Location,helper);
    string token=Guid.NewGuid().ToString("N");using(var accepted=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\WidgetLauncher.UpdateTransfer."+token))using(var current=Process.GetCurrentProcess()){
     var values=new List<string>{"--worker",root,recovery?"":args[2],"--handoff",token,"--parent",current.Id.ToString(),"--parent-time",current.StartTime.ToUniversalTime().Ticks.ToString()};if(recovery)values.Add("--recovering");foreach(string name in new[]{"--resume","--report"}){string value=Option(args,name);if(value.Length>0){values.Add(name);values.Add(value);}}
     using(var process=Process.Start(new ProcessStartInfo(helper,String.Join(" ",values.Select(UpdateEngine.Quote))){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=temporary})){var timer=Stopwatch.StartNew();while(!accepted.WaitOne(100)){if(process==null||process.HasExited||timer.ElapsedMilliseconds>10000)throw new IOException("Не удалось подготовить обновление. Файлы не изменены.");}}
    }
   }return 0;
  }
  static string At(string root,string relative){return InstallEngine.Child(root,relative);}
  static void CleanupHelper(){try{string own=Assembly.GetExecutingAssembly().Location,folder=Path.GetDirectoryName(own);if(!String.Equals(InstallEngine.Normalize(Path.GetDirectoryName(folder)),InstallEngine.Normalize(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("VibeWidgetsHub-Update-",StringComparison.Ordinal))return;string script="Wait-Process -Id "+Process.GetCurrentProcess().Id+" -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '"+own.Replace("'","''")+"' -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '"+folder.Replace("'","''")+"' -ErrorAction SilentlyContinue";string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");Process.Start(new ProcessStartInfo(powershell,"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});}catch{}}
 }
 internal sealed class UpdateWindow:Form
 {
  readonly string root,feed;readonly string[] resume;readonly bool recovery;readonly Label status;readonly Ui.Progress progress;bool busy=true;public UpdateResult Result;
  public UpdateWindow(string root,string feed,string[] resume,bool recovery){this.root=root;this.feed=feed;this.resume=resume;this.recovery=recovery;Text=InstallEngine.ProductName+" · Обновление";ClientSize=new Size(640,380);Ui.Prepare(this);Controls.Add(Ui.Label(recovery?"Возвращаем рабочую версию":"Обновляем VibeWidgetsHub",28,78,584,48,21,Ui.Text));status=Ui.Label("Проверяем файлы…",30,144,580,112,10,Ui.Muted);Controls.Add(status);progress=new Ui.Progress{Location=new Point(30,280),Size=new Size(580,8)};Controls.Add(progress);FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};Shown+=Run;}
  async void Run(object sender,EventArgs e){
   string[] restart=null;
   try{Result=await Task.Run(delegate{return recovery?UpdateEngine.Recover(root):UpdateEngine.Apply(root,feed,false,resume,UpdateEngine.LaunchHealth,delegate(int value,string text){try{BeginInvoke((Action)delegate{progress.Value=value;status.Text=text;});}catch(InvalidOperationException){}});});status.Text=Result.Message;progress.Value=100;if(Result.RolledBack||recovery)restart=resume.Length>0?resume:Result.ResumeIds??new string[0];}
   catch(Exception ex){Result=new UpdateResult{Message=ex.Message};status.Text="Обновление не завершено. "+ex.Message;try{if(!UpdateEngine.Pending(root)){InstallEngine.CheckStopped(root);restart=resume;}}catch{}}
   if(restart!=null&&!InstallEngine.IsRunning("Local\\WidgetLauncher."+InstallEngine.Hash(root)))try{if(!await Task.Run(delegate{return UpdateEngine.LaunchHealth(root,restart);}))status.Text+="\nОткройте лаунчер из его папки.";}catch(Exception ex){status.Text+="\nНе удалось открыть лаунчер: "+ex.Message;}
   busy=false;var done=Ui.Button("Закрыть",446,318,164,true);done.Click+=delegate{Close();};Controls.Add(done);AcceptButton=done;CancelButton=done;
  }
 }
}
