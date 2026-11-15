using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WidgetLauncher;

public static class Autostart
{
 const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
 public static string RootHash(string root)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant())))[..16];
 public static string ValueName(string root)=>"VibeWidgetsHub-"+RootHash(root);
 public static string Command(string root)=>"\""+Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),"Launcher.exe")+"\" --autostart";
 public static bool ShowLauncher(HubPreferences preferences)=>preferences.AutoStartMode is 1 or 4;
 public static IReadOnlyList<Widget> Plan(WidgetManager manager,HubPreferences preferences){
  if(preferences.AutoStartMode is <1 or >4)return Array.Empty<Widget>();
  var selected=new HashSet<string>(preferences.SelectedWidgets??Array.Empty<string>(),StringComparer.OrdinalIgnoreCase);
  return manager.ReadCatalog().Where(widget=>widget.Available&&manager.IsInstalled(widget)
   &&(preferences.AutoStartMode is 1 or 2||selected.Contains(widget.Id))).ToArray();
 }
 static bool VerifyInstallation(string root){
  root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));WidgetManager.RejectLinks(root);
  string marker=Path.Combine(root,"launcher-install.json");WidgetManager.RejectLinks(marker);
  using var document=JsonDocument.Parse(File.ReadAllText(marker));var data=document.RootElement;
  if(data.GetProperty("ProductId").GetString()!="MiniPlayer.WidgetLauncher"
   ||!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(data.GetProperty("InstallPath").GetString()??"")),root,StringComparison.OrdinalIgnoreCase)
   ||!string.Equals(data.GetProperty("InstallId").GetString(),RootHash(root),StringComparison.OrdinalIgnoreCase))
   throw new IOException("Не удалось проверить папку установки для настройки автозапуска.");
  return data.TryGetProperty("TestInstallation",out var test)&&test.GetBoolean();
 }
 public static void Apply(string root,HubPreferences preferences)=>Apply(root,preferences,null);
 internal static void Apply(string root,HubPreferences preferences,Action<string,string?>? write){
  if(VerifyInstallation(root))return;
  string name=ValueName(root);string? command=preferences.AutoStartMode is >=1 and <=4?Command(root):null;
  if(write!=null){write(name,command);return;}
  if(command==null){using var key=Registry.CurrentUser.OpenSubKey(RunKey,true);key?.DeleteValue(name,false);}
  else{using var key=Registry.CurrentUser.CreateSubKey(RunKey,true);key.SetValue(name,command,RegistryValueKind.String);}
 }
 public static async Task<bool> RunAsync(string root){
  var preferences=HubPreferences.Load(root);
  if(preferences.AutoStartMode is <1 or >4)return false;
  using var gate=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\WidgetLauncher.Autostart."+RootHash(root),out bool first);
  if(!first)return false;
  var manager=new WidgetManager(root);var errors=new List<string>();
  try{
   foreach(var widget in Plan(manager,preferences)){
    try{if(!manager.IsRunning(widget))await manager.LaunchAsync(widget).ConfigureAwait(false);}
    catch(Exception ex){errors.Add(widget.Name+": "+ex.Message);}
   }
  }catch(Exception ex){errors.Add(ex.Message);}
  if(errors.Count>0)try{
   string file=Path.Combine(root,"Data","autostart-error.txt");WidgetManager.RejectLinks(file);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
   File.WriteAllText(file,string.Join(Environment.NewLine,errors));
  }catch{}
  return ShowLauncher(preferences);
 }
}
