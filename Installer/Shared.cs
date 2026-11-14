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

namespace WidgetLauncherSetup
{
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
  public const string MarkerName="launcher-install.json";
  public static string Normalize(string value) { return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar); }
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
   for(var p=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)));p!=null;p=p.Parent)
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
   if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())ReadMarker(root);
   return root;
  }
  public static bool IsRunning(string name) {
   Mutex mutex;try { if(Mutex.TryOpenExisting(name,out mutex)){mutex.Dispose();return true;} }catch(UnauthorizedAccessException){return true;}return false;
  }
  public static void CheckStopped(string root) {
   if(IsRunning("Local\\WidgetLauncher."+Hash(root))||IsRunning("Local\\MiniPlayer."+Hash(Path.Combine(root,"Data","mini-player"))))
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
   var moved=new List<string>();var backed=new List<string>();var files=new List<string>();bool completed=false;
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
    progress(72,"Устанавливаем лаунчер…");
    foreach(string relative in files) {
     string target=Child(root,relative);NoLinks(target);Directory.CreateDirectory(Path.GetDirectoryName(target));
     if(File.Exists(target)&&(previous==null||!previous.Files.Contains(relative,StringComparer.OrdinalIgnoreCase)))throw new IOException("В папке найден посторонний файл с таким же именем: "+relative);
     if(File.Exists(target)){string old=Child(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(old));File.Move(target,old);backed.Add(relative);}
     File.Move(Child(extract,relative),target);moved.Add(relative);
    }
    NoLinks(Child(root,"Widgets"));NoLinks(Child(root,"Data"));Directory.CreateDirectory(Child(root,"Widgets"));Directory.CreateDirectory(Child(root,"Data"));
    var marker=new InstallMarker{ProductId=ProductId,InstallPath=root,InstallId=Hash(root),Files=files.Union(previous==null?new string[0]:previous.Files,StringComparer.OrdinalIgnoreCase).ToArray(),WidgetIds=ids.Union(previous==null||previous.WidgetIds==null?new string[0]:previous.WidgetIds).ToArray(),TestInstallation=test,Version="1.0.0"};
    string markerPath=Child(root,MarkerName),tempMarker=Child(stage,MarkerName);File.WriteAllText(tempMarker,Json.Serialize(marker),new UTF8Encoding(false));
    if(File.Exists(markerPath)){File.Copy(markerPath,Path.Combine(backup,MarkerName));File.Delete(markerPath);}
    File.Move(tempMarker,markerPath);completed=true;
    string warning="";progress(92,"Создаём ярлыки…");
    if(!test){try{CreateShortcuts(root);Register(root);}catch(Exception ex){warning="Лаунчер установлен. Не удалось создать один из ярлыков или запись удаления: "+ex.Message;}}
    progress(100,"Готово");return warning;
   }
   catch {
    if(!completed){foreach(var relative in Enumerable.Reverse(moved)){string p=Child(root,relative);if(File.Exists(p))File.Delete(p);}foreach(var relative in backed){string target=Child(root,relative);File.Move(Child(backup,relative),target);}string oldMarker=Path.Combine(backup,MarkerName);if(File.Exists(oldMarker))File.Copy(oldMarker,Child(root,MarkerName),true);}
    throw;
   }
   finally {if(Directory.Exists(stage))DeleteTree(stage,root);if(!completed&&!rootExisted)RemoveEmptyDirectories(root);}
  }
  public static string[] ReadWidgetIds(string catalog) {
   object data=Json.DeserializeObject(File.ReadAllText(catalog));object[] entries=data as object[];
   var dict=data as Dictionary<string,object>;if(dict!=null){object value;if(dict.TryGetValue("widgets",out value)||dict.TryGetValue("Widgets",out value))entries=value as object[];}
   if(entries==null)throw new IOException("Каталог виджетов повреждён.");
   var result=new List<string>();foreach(var entry in entries){var item=entry as Dictionary<string,object>;if(item==null)continue;object value;if(!item.TryGetValue("id",out value)&&!item.TryGetValue("Id",out value))continue;string id=value as string;if(ValidId(id))result.Add(id);}
   return result.Distinct().ToArray();
  }
  static bool ValidId(string id){return !String.IsNullOrEmpty(id)&&id.Length<=64&&id.All(c=>(c>='a'&&c<='z')||(c>='0'&&c<='9')||c=='-');}
  static string RegistryPath(string root){return @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WidgetLauncher-"+Hash(root);}
  static void Register(string root){using(var key=Registry.CurrentUser.CreateSubKey(RegistryPath(root))){key.SetValue("DisplayName","Widget Launcher");key.SetValue("DisplayVersion","1.0.0");key.SetValue("Publisher","Mini Player Team");key.SetValue("InstallLocation",root);key.SetValue("DisplayIcon",Path.Combine(root,"Launcher.exe"));key.SetValue("UninstallString",Quote(Path.Combine(root,"Uninstall.exe")));key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}}
  static string[] ShortcutPaths(string root){string name="Widget Launcher.lnk";string common=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Widget Launcher");return new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),name),Path.Combine(common,name)};}
  static string ShortcutTarget(object shortcut){return (string)shortcut.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null);}
  static object CreateShortcutObject(object shell,string path){return shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});}
  static void CreateShortcuts(string root){object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));try{foreach(string path in ShortcutPaths(root)){Directory.CreateDirectory(Path.GetDirectoryName(path));object shortcut=CreateShortcutObject(shell,path);try{string target=Path.Combine(root,"Launcher.exe");if(File.Exists(path)&&!String.Equals(ShortcutTarget(shortcut),target,StringComparison.OrdinalIgnoreCase))continue;shortcut.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{target});shortcut.GetType().InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{root});shortcut.GetType().InvokeMember("Description",BindingFlags.SetProperty,null,shortcut,new object[]{"Виджеты для рабочего стола"});shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);}finally{Marshal.FinalReleaseComObject(shortcut);}}}finally{Marshal.FinalReleaseComObject(shell);}}
  static void RemoveShortcuts(string root){object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));try{foreach(string path in ShortcutPaths(root)){if(!File.Exists(path))continue;object shortcut=CreateShortcutObject(shell,path);try{if(String.Equals(ShortcutTarget(shortcut),Path.Combine(root,"Launcher.exe"),StringComparison.OrdinalIgnoreCase))File.Delete(path);}finally{Marshal.FinalReleaseComObject(shortcut);}}}finally{Marshal.FinalReleaseComObject(shell);}}
  public static void StopApplications(string root){Signal("Local\\WidgetLauncher."+Hash(root)+".Stop");Signal("Local\\MiniPlayer."+Hash(Path.Combine(root,"Data","mini-player"))+".Stop");}
  static void Signal(string name){EventWaitHandle handle;if(EventWaitHandle.TryOpenExisting(name,out handle)){using(handle)handle.Set();}}
  static void DeleteTree(string path,string root){root=Normalize(root);path=Normalize(path);if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Удаление за пределами папки приложения запрещено.");NoLinks(path);if(!Directory.Exists(path))return;foreach(string file in Directory.GetFiles(path)){NoLinks(file);DeleteFile(file);}foreach(string dir in Directory.GetDirectories(path)){NoLinks(dir);DeleteTree(dir,root);}Directory.Delete(path);}
  static void DeleteFile(string path){for(int attempt=0;attempt<40;attempt++){try{if(File.Exists(path)){File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);}return;}catch(IOException){if(attempt==39)throw;Thread.Sleep(250);}catch(UnauthorizedAccessException){if(attempt==39)throw;Thread.Sleep(250);}}}
  static void RemoveEmptyDirectories(string path){if(!Directory.Exists(path))return;NoLinks(path);foreach(string sub in Directory.GetDirectories(path))if((File.GetAttributes(sub)&FileAttributes.ReparsePoint)==0)RemoveEmptyDirectories(sub);if(!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);}
  public static void Remove(string requested,bool test){
   using(var gate=new Mutex(false,"Local\\WidgetLauncher.Install."+Hash(requested))){bool held=false;try{try{held=gate.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Установка или удаление уже выполняется. Дождитесь завершения.");RemoveCore(requested,test);}finally{if(held)gate.ReleaseMutex();}}
  }
  static void RemoveCore(string requested,bool test){
   string root=Normalize(requested);var marker=ReadMarker(root);if(marker.TestInstallation!=test)throw new IOException("Тип установки не соответствует режиму удаления.");
   StopApplications(root);for(int i=0;i<40;i++){try{CheckStopped(root);break;}catch(IOException){if(i==39)throw;Thread.Sleep(250);}}
   // Preflight every path before the first delete. Never follow junctions or symlinks.
   foreach(string file in marker.Files)NoLinks(Child(root,file));
   foreach(string id in marker.WidgetIds??new string[0]){if(!ValidId(id))throw new IOException("Некорректное имя виджета.");foreach(string parent in new[]{"Widgets","Data"})CheckTree(Child(root,parent+"\\"+id));}
   if(!test){RemoveShortcuts(root);using(var key=Registry.CurrentUser.OpenSubKey(RegistryPath(root))){if(key!=null&&!String.Equals(key.GetValue("InstallLocation") as string,root,StringComparison.OrdinalIgnoreCase))throw new IOException("Запись установки относится к другой папке.");}Registry.CurrentUser.DeleteSubKeyTree(RegistryPath(root),false);}
   foreach(string id in marker.WidgetIds??new string[0])foreach(string parent in new[]{"Widgets","Data"})DeleteTree(Child(root,parent+"\\"+id),root);
   // Keep the uninstaller available if a locked file prevents completing the operation.
   foreach(string file in marker.Files.OrderBy(f=>f.Equals("Uninstall.exe",StringComparison.OrdinalIgnoreCase)?1:0)){string target=Child(root,file);DeleteFile(target);}
   File.Delete(Child(root,MarkerName));RemoveEmptyDirectories(root);
  }
  static void CheckTree(string path){NoLinks(path);if(!Directory.Exists(path))return;foreach(string file in Directory.GetFiles(path))NoLinks(file);foreach(string dir in Directory.GetDirectories(path))CheckTree(dir);}
 }

 internal static class Ui
 {
  static PrivateFontCollection fonts;static IntPtr fontMemory;
  [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr data,uint size,IntPtr reserved,ref uint count);
  public static readonly Color Background=Color.FromArgb(19,21,28),Panel=Color.FromArgb(29,32,42),Text=Color.FromArgb(239,239,246),Muted=Color.FromArgb(164,170,188),Accent=Color.FromArgb(180,154,255);
  public static Font Font(float size){if(fonts==null){fonts=new PrivateFontCollection();using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Onest.ttf")){if(stream!=null){byte[] bytes=new byte[stream.Length];stream.Read(bytes,0,bytes.Length);fontMemory=Marshal.AllocCoTaskMem(bytes.Length);Marshal.Copy(bytes,0,fontMemory,bytes.Length);fonts.AddMemoryFont(fontMemory,bytes.Length);uint count=0;AddFontMemResourceEx(fontMemory,(uint)bytes.Length,IntPtr.Zero,ref count);}}}return new Font(fonts.Families.Length>0?fonts.Families[0]:SystemFonts.MessageBoxFont.FontFamily,size,FontStyle.Bold);}
  public static Label Label(string text,int x,int y,int width,int height,float size,Color color){return new Label{Text=text,Location=new Point(x,y),Size=new Size(width,height),Font=Font(size),ForeColor=color,BackColor=Color.Transparent};}
  public static Button Button(string text,int x,int y,int width,bool primary){var b=new Button{Text=text,Location=new Point(x,y),Size=new Size(width,44),Font=Font(10),BackColor=primary?Accent:Panel,ForeColor=primary?Background:Text,FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=0;return b;}
  public static void Prepare(Form form){form.BackColor=Background;form.ForeColor=Text;form.Font=Font(10);form.AutoScaleMode=AutoScaleMode.Dpi;form.StartPosition=FormStartPosition.CenterScreen;form.FormBorderStyle=FormBorderStyle.FixedSingle;form.MaximizeBox=false;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Player.ico")){if(stream!=null)form.Icon=new Icon(stream);}}
 }
}
