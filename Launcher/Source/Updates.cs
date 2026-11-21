using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using VibeWidgetsUpdates;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 UpdateManifest? checkedUpdate;
 string checkedUpdateFeed="",updateMessage="",updateReport="";
 bool updatePending,updateMonitorInitialized;
 Process? updateProcess;
 readonly DispatcherTimer updateMonitor=new(){Interval=TimeSpan.FromMilliseconds(300)};
 GitHubUpdateCandidate? checkedGitHubUpdate;
 GitHubUpdateSource? gitHubUpdates;
 CancellationTokenSource? networkUpdateCancellation;
 GitHubUpdateSource GitHubUpdates=>gitHubUpdates??=new GitHubUpdateSource(Path.Combine(manager.Root,".update-downloads"));

 static long ReadInstalledUpdateBuild(string root){
  string path=Path.Combine(root,"update-state.json");WidgetManager.RejectLinks(path);
  if(!File.Exists(path))return 0;
  if(new FileInfo(path).Length>65536)throw new IOException("Сведения об установленной сборке повреждены.");
  try{
   using var data=JsonDocument.Parse(File.ReadAllText(path));
   if(data.RootElement.ValueKind!=JsonValueKind.Object||!data.RootElement.TryGetProperty("BuildNumber",out var number)||number.ValueKind!=JsonValueKind.Number||!number.TryGetInt64(out long build)||build<=0)throw new IOException("Сведения об установленной сборке повреждены.");
   return build;
  }catch(JsonException ex){throw new IOException("Не удалось прочитать сведения об установленной сборке.",ex);}
 }

 void RenderUpdateSettings(){
  var panel=new StackPanel();panel.Children.Add(Label("Обновления",17));
  panel.Children.Add(Label("Новые версии лаунчера и каталог виджетов из GitHub. Подпись пакета проверяется перед установкой.",12,MutedBrush,new Thickness(0,8,0,12)));
  var sources=new WrapPanel{Margin=new Thickness(0,0,0,12)};panel.Children.Add(sources);
  foreach(int mode in new[]{0,1}){var source=ActionButton(mode==0?"GitHub":"Локальная папка",()=>{int previous=preferences.UpdateSourceMode;preferences.UpdateSourceMode=mode;try{SaveHubPreferences();checkedUpdate=null;checkedGitHubUpdate=null;updateMessage="";}catch(Exception ex){preferences.UpdateSourceMode=previous;message=ex.Message;}Render();});source.Margin=new Thickness(0,0,8,0);source.Background=preferences.UpdateSourceMode==mode?ColorBrush("#30283F"):ColorBrush("#232530");source.Foreground=preferences.UpdateSourceMode==mode?AccentBrush:TextBrush;sources.Children.Add(source);}
  AddAutomaticUpdateControl(panel);
  try{long build=ReadInstalledUpdateBuild(manager.Root);panel.Children.Add(Label(build==0?"Установлена исходная локальная сборка.":"Установленная сборка: "+build,11,MutedBrush));}
  catch(IOException){panel.Children.Add(Label("Сведения об установленной сборке недоступны.",11,ColorBrush("#F0A2AC")));}
  panel.Children.Add(PathText(preferences.UpdateSourceMode==0?GitHubUpdateSource.RepositoryUrl:string.IsNullOrWhiteSpace(preferences.UpdateFeedDirectory)?"Папка обновлений пока не выбрана.":preferences.UpdateFeedDirectory));
  var actions=new WrapPanel{Margin=new Thickness(0,14,0,0)};panel.Children.Add(actions);
  if(preferences.UpdateSourceMode==1){var choose=ActionButton("Выбрать папку…",ChooseUpdateFolder);choose.Margin=new Thickness(0,0,8,8);actions.Children.Add(choose);}
  var check=ActionButton("Проверить обновления",()=>RunAction(CheckForUpdatesAsync,""));check.Margin=new Thickness(0,0,0,8);check.IsEnabled=!busy&&!updatePending&&(preferences.UpdateSourceMode==0||!string.IsNullOrWhiteSpace(preferences.UpdateFeedDirectory));actions.Children.Add(check);
  if(networkUpdateCancellation!=null){var cancel=ActionButton("Отменить загрузку",()=>networkUpdateCancellation?.Cancel());cancel.IsEnabled=true;cancel.Margin=new Thickness(8,0,0,8);actions.Children.Add(cancel);}
  if(!string.IsNullOrWhiteSpace(updateMessage))panel.Children.Add(Label(updateMessage,12,checkedUpdate!=null?AccentBrush:MutedBrush,new Thickness(0,6,0,0)));
  if(checkedUpdate!=null){
   if(!string.IsNullOrWhiteSpace(checkedUpdate.Notes)){string notes=checkedUpdate.Notes.Trim();if(notes.Length>900)notes=notes[..900]+"…";panel.Children.Add(Label(notes,12,MutedBrush,new Thickness(0,9,0,0)));}
   var install=ActionButton(updatePending?"Подготавливаем обновление…":"Обновить",RequestCheckedUpdate,true);install.HorizontalAlignment=HorizontalAlignment.Left;install.Margin=new Thickness(0,14,0,0);panel.Children.Add(install);
   panel.Children.Add(Label("Лаунчер и работающие виджеты перезапустятся. Настройки сохранятся; при сбое будет восстановлена предыдущая версия.",11,MutedBrush,new Thickness(0,10,0,0)));
  }
  if(!updatePending&&updateReport.Length>0&&File.Exists(updateReport)){
   var log=ActionButton("Открыть журнал обновления",()=>RunAction(()=>{Process.Start(new ProcessStartInfo(updateReport){UseShellExecute=true});return Task.CompletedTask;},""));log.HorizontalAlignment=HorizontalAlignment.Left;log.Margin=new Thickness(0,12,0,0);panel.Children.Add(log);
  }
  content.Children.Add(Card(panel,new Thickness(25)));
 }

 void ChooseUpdateFolder(){
  if(busy||updatePending||preview)return;
  using var picker=new System.Windows.Forms.FolderBrowserDialog{Description="Папка локального обновления VibeWidgetsHub",UseDescriptionForTitle=true,ShowNewFolderButton=false};
  if(Directory.Exists(preferences.UpdateFeedDirectory))picker.InitialDirectory=preferences.UpdateFeedDirectory;
  if(picker.ShowDialog(new FolderOwner(new WindowInteropHelper(this).Handle))!=System.Windows.Forms.DialogResult.OK)return;
  RunAction(()=>{
   string next=UpdateProtocol.FeedPath(picker.SelectedPath),previous=preferences.UpdateFeedDirectory;preferences.UpdateFeedDirectory=next;
   try{SaveHubPreferences();}catch{preferences.UpdateFeedDirectory=previous;throw;}
   checkedUpdate=null;checkedUpdateFeed="";updateMessage="Папка выбрана. Нажмите «Проверить обновления».";return Task.CompletedTask;
  },"");
 }

 async Task CheckForUpdatesAsync(){
  checkedUpdate=null;checkedUpdateFeed="";checkedGitHubUpdate=null;
  if(preferences.UpdateSourceMode==0){
   using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(60));networkUpdateCancellation=cancellation;
   try{var result=await GitHubUpdates.FindAsync(ReadInstalledUpdateBuild(manager.Root),cancellation.Token);checkedGitHubUpdate=result;
    if(result==null){updateMessage="На GitHub пока нет новой подписанной сборки для keksichek/launcher.";return;}
    checkedUpdate=result.Manifest;checkedUpdateFeed=result.FeedDirectory;updateMessage="Найдена версия "+result.Manifest.Version+". Подпись проверена. Пакет будет скачан при обновлении.";
   }catch(OperationCanceledException){updateMessage="Проверка обновлений отменена или превышено время ожидания.";}
   catch(Exception ex){updateMessage="Не удалось проверить GitHub: "+ex.Message;}
   finally{networkUpdateCancellation=null;}return;
  }
  if(string.IsNullOrWhiteSpace(preferences.UpdateFeedDirectory)){updateMessage="Сначала выберите папку с локальным обновлением.";return;}
  try{
   string feed=preferences.UpdateFeedDirectory;
   var result=await Task.Run(()=>{long current=ReadInstalledUpdateBuild(manager.Root);string path=UpdateProtocol.FeedPath(feed);var candidate=UpdateProtocol.Read(path);UpdateProtocol.VerifyPackage(path,candidate);return(Current:current,Feed:path,Manifest:candidate);});
   if(result.Manifest.BuildNumber<=result.Current){updateMessage="Новых обновлений нет. Проверенный пакет относится к установленной или более ранней сборке.";return;}
   checkedUpdate=result.Manifest;checkedUpdateFeed=result.Feed;
   updateMessage="Доступна версия "+result.Manifest.Version+" · сборка "+result.Manifest.BuildNumber+". Пакет проверен.";
  }catch(Exception ex){updateMessage="Не удалось проверить обновления: "+ex.Message;}
 }

 string UpdateRunningName=>"Local\\WidgetLauncher.Update."+Autostart.RootHash(manager.Root)+".Running";
 bool UpdateHelperRunning(){if(!EventWaitHandle.TryOpenExisting(UpdateRunningName,out var running))return false;running.Dispose();return true;}

 void RequestCheckedUpdate(){
  if(busy||updatePending||checkedUpdate==null||preview)return;
  RunAction(InstallCheckedUpdateAsync,"");
 }
 async Task InstallCheckedUpdateAsync(){
   if(checkedUpdate==null)return;
   if(UpdateHelperRunning())throw new IOException("Обновление уже выполняется. Дождитесь его завершения.");
   var expected=checkedUpdate;string feed=checkedUpdateFeed;
   if(checkedGitHubUpdate!=null){
    using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(15));networkUpdateCancellation=cancellation;
    try{await GitHubUpdates.DownloadAsync(checkedGitHubUpdate,new Progress<int>(percent=>{updateMessage="Загружаем обновление: "+percent+"%";Render();}),cancellation.Token);}
    finally{networkUpdateCancellation=null;}
   }
   try{await Task.Run(()=>{
     var fresh=UpdateProtocol.Read(feed);UpdateProtocol.VerifyPackage(feed,fresh);
     if(fresh.BuildNumber!=expected.BuildNumber||fresh.Version!=expected.Version||fresh.Package!=expected.Package||!string.Equals(fresh.Sha256,expected.Sha256,StringComparison.OrdinalIgnoreCase)||fresh.Size!=expected.Size)throw new IOException("Пакет изменился. Повторите проверку обновлений.");
     if(fresh.BuildNumber<=ReadInstalledUpdateBuild(manager.Root))throw new IOException("Эта сборка уже установлена. Повторите проверку обновлений.");
    });
   }catch{checkedUpdate=null;checkedUpdateFeed="";updateMessage="Пакет больше не прошёл проверку. Нажмите «Проверить обновления».";throw;}
   string helper=Path.Combine(manager.Root,"Update.exe");WidgetManager.RejectLinks(helper);
   if(!File.Exists(helper))throw new IOException("Программа обновления отсутствует. Установите текущую версию лаунчера поверх этой папки.");
   var start=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=manager.Root};
   updateReport=Path.Combine(Path.GetTempPath(),"VibeWidgetsHub-update-"+Guid.NewGuid().ToString("N")+".txt");
   foreach(string value in new[]{"--update",manager.Root,feed,"--resume",string.Join(',',catalog.Where(x=>x.Available&&manager.IsRunning(x)).Select(x=>x.Id)),"--report",updateReport})start.ArgumentList.Add(value);
   long previousAttempt=preferences.AutoUpdateAttemptBuild;preferences.AutoUpdateAttemptBuild=Math.Max(previousAttempt,expected.BuildNumber);
   try{SaveHubPreferences();}catch{preferences.AutoUpdateAttemptBuild=previousAttempt;throw;}
   updatePending=true;
   try{updateProcess=Process.Start(start)??throw new IOException("Не удалось запустить обновление.");EnsureUpdateMonitor();updateMonitor.Start();updateMessage="Подготавливаем обновление. Дождитесь перезапуска лаунчера.";}
   catch{updatePending=false;throw;}
 }

 void EnsureUpdateMonitor(){
  if(updateMonitorInitialized)return;updateMonitorInitialized=true;
  Closed+=(_,_)=>{updateMonitor.Stop();updateProcess?.Dispose();updateProcess=null;};
  updateMonitor.Tick+=(_,_)=>{
   if(!updatePending){updateMonitor.Stop();return;}
   try{
    if(UpdateHelperRunning()||updateProcess is {HasExited:false})return;
    int code=updateProcess?.ExitCode??1;updateProcess?.Dispose();updateProcess=null;updateMonitor.Stop();updatePending=false;
    string result="";if(File.Exists(updateReport)&&new FileInfo(updateReport).Length<=65536)result=File.ReadAllText(updateReport);
    if(result.Length>0)updateMessage=result.StartsWith("ROLLBACK",StringComparison.Ordinal)?"Не удалось установить обновление. Предыдущая версия восстановлена.":result.StartsWith("PASS",StringComparison.Ordinal)?"Обновление установлено. Нажмите «Проверить обновления», чтобы обновить сведения.":"Обновление не завершено. Подробности доступны в журнале.";
    else updateMessage=code==0?"Программа обновления завершилась. Повторите проверку, чтобы узнать установленную сборку.":"Не удалось начать обновление. Проверьте пакет и повторите попытку.";
    checkedUpdate=null;checkedUpdateFeed="";Render();
   }catch(Exception ex){updateMonitor.Stop();updatePending=false;updateProcess?.Dispose();updateProcess=null;updateMessage="Не удалось получить результат обновления: "+ex.Message;Render();}
  };
 }
}
