using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using File=WidgetLauncherSetup.HubFile;
using Directory=WidgetLauncherSetup.HubDirectory;

namespace WidgetLauncherSetup
{
 public sealed class RelocationResult
 {
  public string Destination {get;set;}
  public string Warning {get;set;}
 }
 public static class RelocationEngine
 {
  sealed class Entry {public string Relative,Hash;}
  static string Relative(string root,string full){string path=InstallEngine.Normalize(full),prefix=InstallEngine.Normalize(root)+Path.DirectorySeparatorChar;if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Путь выходит за пределы переносимой папки.");return path.Substring(prefix.Length);}
  public static string Destination(string source,string selected,bool test){
   source=InstallEngine.ValidateDestination(source);var marker=InstallEngine.ReadMarker(source);if(marker.TestInstallation!=test)throw new IOException("Тип установки не соответствует режиму переноса.");
   string target=InstallEngine.ResolveDestination(selected);
   if(String.Equals(source,target,StringComparison.OrdinalIgnoreCase)||target.StartsWith(source+"\\",StringComparison.OrdinalIgnoreCase)||source.StartsWith(target+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Выберите другую папку вне текущей папки лаунчера.");
   if(Directory.Exists(target)&&Directory.EnumerateFileSystemEntries(target).Any())throw new IOException("В новой папке уже есть файлы. Выберите пустую папку для переноса.");
   return target;
  }
  static string Digest(string path){InstallEngine.NoLinks(path);using(var stream=new FileStream(HubPath.Disk(path),FileMode.Open,FileAccess.Read,FileShare.Read))using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(stream));}
  static void Collect(string root,string path,List<Entry> files,List<string> directories){
   InstallEngine.NoLinks(path);
   foreach(string file in Directory.GetFiles(path)){InstallEngine.NoLinks(file);files.Add(new Entry{Relative=Relative(root,file),Hash=Digest(file)});}
   foreach(string directory in Directory.GetDirectories(path)){InstallEngine.NoLinks(directory);directories.Add(Relative(root,directory));Collect(root,directory,files,directories);}
  }
  static bool Held(Mutex mutex){try{return mutex.WaitOne(0);}catch(AbandonedMutexException){return true;}}
  public static RelocationResult Relocate(string source,string selected,bool test,Action<int,string> progress,Action<string> resumeOnFailure=null){
   source=InstallEngine.Normalize(source);string target=Destination(source,selected,test);
   string[] identities=new[]{source,target}.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
   string restartRoot=null;
   try{
    using(var first=new Mutex(false,"Local\\WidgetLauncher.Install."+InstallEngine.Hash(identities[0])))
    using(var second=new Mutex(false,"Local\\WidgetLauncher.Install."+InstallEngine.Hash(identities[1]))){
     bool a=false,b=false;
     try{
      a=Held(first);b=Held(second);
      if(!a||!b)throw new IOException("Установка, удаление или перенос уже выполняется.");
      progress(2,"Закрываем лаунчер и виджеты…");restartRoot=source;
      InstallEngine.StopApplications(source);
      return RelocateCore(source,target,selected,test,progress,delegate{restartRoot=target;});
     }finally{if(b)second.ReleaseMutex();if(a)first.ReleaseMutex();}
    }
   }catch(Exception failure){
    if(restartRoot!=null&&resumeOnFailure!=null){
     try{resumeOnFailure(restartRoot);}
     catch(Exception restart){throw new IOException(failure.Message+" Не удалось возобновить лаунчер и виджеты из "+restartRoot+": "+restart.Message,failure);}
    }
    throw;
   }
  }
  static RelocationResult RelocateCore(string source,string target,string selected,bool test,Action<int,string> progress,Action destinationCommitted){
   for(int i=0;i<80;i++){try{InstallEngine.CheckStopped(source);break;}catch(IOException){if(i==79)throw;Thread.Sleep(250);}}
   // Revalidate after waiting; a destination can change while applications close.
   if(!String.Equals(target,Destination(source,selected,test),StringComparison.OrdinalIgnoreCase))throw new IOException("Папка назначения изменилась.");
   var files=new List<Entry>();var directories=new List<string>();Collect(source,source,files,directories);
   foreach(string required in new[]{"Launcher.exe","Launcher.dll","Uninstall.exe","Runtime\\dotnet.exe","Catalog\\widgets.json"})if(!files.Any(x=>String.Equals(x.Relative,required,StringComparison.OrdinalIgnoreCase)))throw new IOException("Не хватает файлов лаунчера: "+required);
   string parent=Path.GetDirectoryName(target);InstallEngine.NoLinks(parent);Directory.CreateDirectory(parent);
   string stage=Path.Combine(parent,".vibe-relocate-"+Guid.NewGuid().ToString("N"));InstallEngine.NoLinks(stage);Directory.CreateDirectory(stage);bool committed=false,registered=false;var stagedFiles=new List<Entry>();var stagedDirectories=new List<string>();
   try{
    foreach(string directory in directories)Directory.CreateDirectory(InstallEngine.Child(stage,directory));
    int n=0;foreach(var file in files){string path=InstallEngine.Child(stage,file.Relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(InstallEngine.Child(source,file.Relative),path);if(Digest(path)!=file.Hash)throw new IOException("Не удалось проверить копию файла: "+file.Relative);progress(5+(++n*65/Math.Max(1,files.Count)),"Копируем лаунчер, виджеты и настройки…");}
    progress(74,"Проверяем исходные файлы и новую копию…");
    // Detect writes, additions and removals before committing the new installation.
    var latest=new List<Entry>();var latestDirectories=new List<string>();Collect(source,source,latest,latestDirectories);
    if(latest.Count!=files.Count||!latest.OrderBy(x=>x.Relative,StringComparer.OrdinalIgnoreCase).Select(x=>x.Relative+":"+x.Hash).SequenceEqual(files.OrderBy(x=>x.Relative,StringComparer.OrdinalIgnoreCase).Select(x=>x.Relative+":"+x.Hash)))throw new IOException("Файлы изменились во время переноса. Исходная папка сохранена; повторите перенос.");
    var marker=InstallEngine.ReadMarker(source);marker.InstallPath=target;marker.InstallId=InstallEngine.Hash(target);File.WriteAllText(InstallEngine.Child(stage,InstallEngine.MarkerName),new JavaScriptSerializer().Serialize(marker),new UTF8Encoding(false));
    Collect(stage,stage,stagedFiles,stagedDirectories);
    // Widget manifests contain relative package paths and therefore remain unchanged.
    InstallEngine.NoLinks(target);if(Directory.Exists(target)){if(Directory.EnumerateFileSystemEntries(target).Any())throw new IOException("В папке назначения появились файлы.");Directory.Delete(target);}
    System.IO.Directory.Move(HubPath.Disk(stage),HubPath.Disk(target));committed=true;
    InstallEngine.ReadMarker(target);progress(85,"Обновляем пути запуска…");
    if(!test){InstallEngine.RelocateRegistration(source,target);registered=true;}
   }catch{
    if(committed&&!registered){try{DeleteVerifiedFiles(target,stagedFiles,stagedDirectories);}catch{}}
    throw;
   }finally{if(Directory.Exists(stage)){try{DeleteOwnTree(stage,parent);}catch{}}}
   destinationCommitted();
   progress(92,"Убираем проверенные файлы из старой папки…");
   bool leftovers=false;
   foreach(var file in files.OrderBy(x=>x.Relative.Equals(InstallEngine.MarkerName,StringComparison.OrdinalIgnoreCase)?1:0)){
    string path=InstallEngine.Child(source,file.Relative);
    try{if(!File.Exists(path))continue;if(Digest(path)!=file.Hash){leftovers=true;continue;}bool removed=false;for(int i=0;i<40;i++){try{InstallEngine.NoLinks(path);if(Digest(path)!=file.Hash)break;File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);removed=true;break;}catch(IOException){Thread.Sleep(100);}catch(UnauthorizedAccessException){break;}}if(!removed)leftovers=true;}catch{leftovers=true;}
   }
   foreach(string directory in directories.OrderByDescending(x=>x.Length)){string path=InstallEngine.Child(source,directory);try{InstallEngine.NoLinks(path);if(Directory.Exists(path)&&!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);}catch{leftovers=true;}}
   try{InstallEngine.NoLinks(source);if(Directory.Exists(source)&&!Directory.EnumerateFileSystemEntries(source).Any())Directory.Delete(source);else if(Directory.Exists(source))leftovers=true;}catch{leftovers=true;}
   progress(100,"Перенос завершён");return new RelocationResult{Destination=target,Warning=leftovers?"В старой папке остались изменённые или занятые файлы. Они сохранены; рабочая копия находится по новому пути.":""};
  }
  static void DeleteOwnTree(string path,string parent){
   path=InstallEngine.Normalize(path);parent=InstallEngine.Normalize(parent);if(!path.StartsWith(parent+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Неверная папка временной копии.");InstallEngine.NoLinks(path);
   foreach(string file in Directory.GetFiles(path)){InstallEngine.NoLinks(file);File.SetAttributes(file,FileAttributes.Normal);File.Delete(file);}foreach(string directory in Directory.GetDirectories(path))DeleteOwnTree(directory,parent);Directory.Delete(path);
  }
  static void DeleteVerifiedFiles(string root,List<Entry> files,List<string> directories){
   InstallEngine.NoLinks(root);foreach(var file in files){string path=InstallEngine.Child(root,file.Relative);if(File.Exists(path)&&Digest(path)==file.Hash){File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);}}
   foreach(string directory in directories.OrderByDescending(x=>x.Length)){string path=InstallEngine.Child(root,directory);InstallEngine.NoLinks(path);if(Directory.Exists(path)&&!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);}
   if(Directory.Exists(root)&&!Directory.EnumerateFileSystemEntries(root).Any())Directory.Delete(root);
  }
 }
 internal static class RelocateProgram
 {
  internal static void ResumeLauncher(string root,string[] widgets){
   string command=QuoteArgument(InstallEngine.Child(root,"Launcher.dll"));
   if(widgets.Length>0)command+=" --resume-widgets "+QuoteArgument(String.Join(",",widgets));
   string runtime=InstallEngine.Child(root,@"Runtime\dotnet.exe");
   using(var process=Process.Start(new ProcessStartInfo(HubPath.Disk(runtime),command){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=HubPath.Disk(root)})){
    if(process==null)throw new IOException("Не удалось запустить лаунчер.");
   }
  }
  static string Option(string[] args,string name){int index=Array.IndexOf(args,name);return index>=0&&index+1<args.Length?args[index+1]:"";}
  internal static string QuoteArgument(string value){
   var command=new StringBuilder("\"");int slashes=0;
   foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){command.Append('\\',slashes*2+1);command.Append(c);}else{command.Append('\\',slashes);command.Append(c);}slashes=0;}
   command.Append('\\',slashes*2);command.Append('"');return command.ToString();
  }
  [STAThread]static int Main(string[] args){
   InstallEngine.ConfigureRuntime();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try{
    if(args.Length>=2&&args[0]=="--preview"){
     using(var preview=new RelocateWindow(@"C:\Приложения\VibeWidgetsHub alpha",@"D:\Приложения",new string[0],true)){preview.ShowInTaskbar=false;preview.Opacity=0;preview.Show();Application.DoEvents();using(var bitmap=new Bitmap(preview.Width,preview.Height)){preview.DrawToBitmap(bitmap,new Rectangle(0,0,preview.Width,preview.Height));bitmap.Save(args[1]);}preview.Close();}return 0;
    }
    if(args.Length>=3&&args[0]=="--test-relocate"){
     var result=RelocationEngine.Relocate(args[1],args[2],true,delegate{});string report=Option(args,"--report");if(report.Length>0)File.WriteAllText(report,"PASS "+result.Destination+"\n"+result.Warning);return 0;
    }
    if(args.Length<3||(args[0]!="--relocate"&&args[0]!="--worker"))throw new IOException("Выберите перенос в настройках лаунчера.");
    string source=InstallEngine.Normalize(args[1]),selected=args[2];RelocationEngine.Destination(source,selected,false);
    if(args[0]=="--relocate")return Dispatch(args);
    string transfer=Option(args,"--handoff");Guid token;if(!Guid.TryParseExact(transfer,"N",out token))throw new IOException("Не удалось проверить передачу переноса.");
    using(var window=new RelocateWindow(source,selected,Option(args,"--resume").Split(new[]{','},StringSplitOptions.RemoveEmptyEntries),false)){
     using(var ready=EventWaitHandle.OpenExisting("Local\\WidgetLauncher.Relocate."+InstallEngine.Hash(source)+"."+transfer))ready.Set();
     int id;long ticks;if(Int32.TryParse(Option(args,"--parent"),out id)&&Int64.TryParse(Option(args,"--parent-time"),out ticks)){try{using(var parent=Process.GetProcessById(id))if(parent.StartTime.ToUniversalTime().Ticks==ticks&&!parent.WaitForExit(10000))throw new IOException("Не удалось завершить подготовку переноса.");}catch(ArgumentException){}}
     Application.Run(window);string report=Option(args,"--report");if(report.Length>0)File.WriteAllText(report,(window.Success?"PASS ":"FAIL ")+window.Result);return window.Success?0:1;
    }
   }catch(Exception ex){string report=Option(args,"--report");if(report.Length>0)File.WriteAllText(report,ex.ToString());if(args.Length==0||args[0]!="--test-relocate")MessageBox.Show(ex.Message,"Перенос VibeWidgetsHub",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
   finally{if(args.Length>0&&args[0]=="--worker")CleanupHelper();}
  }
  static int Dispatch(string[] args){
   string source=InstallEngine.Normalize(args[1]),temporary=Path.Combine(Path.GetTempPath(),"VibeWidgetsHub-Relocate-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
   string helper=Path.Combine(temporary,"Relocate.exe");File.Copy(Assembly.GetExecutingAssembly().Location,helper);string token=Guid.NewGuid().ToString("N");
   using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\WidgetLauncher.Relocate."+InstallEngine.Hash(source)+"."+token))using(var current=Process.GetCurrentProcess()){
    var values=new List<string>{"--worker",source,args[2],"--handoff",token,"--parent",current.Id.ToString(),"--parent-time",current.StartTime.ToUniversalTime().Ticks.ToString()};foreach(string name in new[]{"--resume","--report"}){string value=Option(args,name);if(value.Length>0){values.Add(name);values.Add(value);}}
    using(var process=Process.Start(new ProcessStartInfo(helper,String.Join(" ",values.Select(QuoteArgument))){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=temporary})){
     var time=Stopwatch.StartNew();while(!ready.WaitOne(100)){if(process==null||process.HasExited||time.ElapsedMilliseconds>10000)throw new IOException("Не удалось подготовить перенос. Исходные файлы не изменены.");}
    }
   }
   return 0;
  }
  static void CleanupHelper(){try{
   string own=Assembly.GetExecutingAssembly().Location,folder=Path.GetDirectoryName(own);
   if(!String.Equals(InstallEngine.Normalize(Path.GetDirectoryName(folder)),InstallEngine.Normalize(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("VibeWidgetsHub-Relocate-",StringComparison.Ordinal))return;
   int id=Process.GetCurrentProcess().Id;string script="Wait-Process -Id "+id+" -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '"+own.Replace("'","''")+"' -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '"+folder.Replace("'","''")+"' -ErrorAction SilentlyContinue";
   string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");Process.Start(new ProcessStartInfo(powershell,"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
  }catch{}}
 }
 internal sealed class RelocateWindow:Form
 {
  readonly string source,selected;readonly string[] resume;readonly bool preview;readonly Label status;readonly Ui.Progress progress;bool busy;
  public bool Success {get;private set;}public string Result {get;private set;}
  public RelocateWindow(string source,string selected,string[] resume,bool preview){
   this.source=source;this.selected=selected;this.resume=resume;this.preview=preview;Text=InstallEngine.ProductName+" · Перенос";ClientSize=new Size(640,378);Ui.Prepare(this);
   Controls.Add(Ui.Label("Переносим всё вместе",30,76,580,48,22,Ui.Text));
   Controls.Add(Ui.Label("Лаунчер, установленные виджеты и ваши настройки",32,128,576,30,10,Ui.Muted));
   status=Ui.Label("Подготавливаем новую папку…",32,182,576,80,10,Ui.Muted);Controls.Add(status);progress=new Ui.Progress{Location=new Point(32,280),Size=new Size(576,8),Value=preview?45:0};Controls.Add(progress);
   busy=!preview;FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};if(!preview)Shown+=Run;
  }
  void ResumeLauncher(string root){RelocateProgram.ResumeLauncher(root,resume);}
  async void Run(object sender,EventArgs e){
   try{var moved=await Task.Run(delegate{return RelocationEngine.Relocate(source,selected,false,delegate(int percent,string text){try{BeginInvoke((Action)delegate{progress.Value=percent;status.Text=text;});}catch(InvalidOperationException){}},ResumeLauncher);});
    Result=moved.Destination+"\n"+moved.Warning;Success=true;status.Text="Готово. Новая папка:\n"+moved.Destination+(moved.Warning.Length>0?"\n"+moved.Warning:"");
    ResumeLauncher(moved.Destination);
   }catch(Exception ex){if(Success){Result+="\nНе удалось открыть лаунчер: "+ex.Message;status.Text="Файлы перенесены. Откройте Launcher.exe из новой папки:\n"+Result;}else{Result=ex.Message;status.Text="Перенос не завершён. "+ex.Message;}}
   finally{busy=false;var done=Ui.Button("Закрыть",442,316,166,true);done.Click+=delegate{Close();};Controls.Add(done);AcceptButton=done;CancelButton=done;}
  }
 }
}
