using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace MiniPlayer;

public sealed partial class Player
{
 bool uninstalling;

 internal sealed record UninstallPlan(string InstallDirectory,string LegacyDirectory,
  string? PortableDirectory,string DesktopDirectory,string Executable,string InstalledExecutable,
  int ProcessId,long ProcessStartTicks);

 static string NormalizedUninstallPath(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

 static bool IsWithinUninstallDirectory(string path,string directory){
  path=NormalizedUninstallPath(path);directory=NormalizedUninstallPath(directory);
  return path.Equals(directory,StringComparison.OrdinalIgnoreCase)
   ||path.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
 }

 internal static bool IsPortableAppDirectory(string directory){
  directory=NormalizedUninstallPath(directory);
  if(!Path.GetFileName(directory).Equals("App",StringComparison.OrdinalIgnoreCase))return false;
  foreach(var file in new[]{"MiniPlayer.exe","MiniPlayer.dll","MiniPlayer.deps.json","MiniPlayer.runtimeconfig.json","Player.ico"})
   if(!File.Exists(Path.Combine(directory,file)))return false;
  for(var parent=new DirectoryInfo(directory);parent!=null;parent=parent.Parent)
   if(File.Exists(Path.Combine(parent.FullName,".git"))||Directory.Exists(Path.Combine(parent.FullName,".git")))return false;
  return true;
 }

 internal static UninstallPlan CreateUninstallPlan(string localAppData,string currentExecutable,
  string desktopDirectory,int processId,long processStartTicks){
  string install=NormalizedUninstallPath(Path.Combine(localAppData,"MiniPlayer"));
  string executable=NormalizedUninstallPath(currentExecutable);
  if(!Path.GetFileName(executable).Equals("MiniPlayer.exe",StringComparison.OrdinalIgnoreCase))
   throw new InvalidOperationException("Не удалось определить файлы Mini Player.");
  string currentDirectory=Path.GetDirectoryName(executable)!;
  string? portable=!IsWithinUninstallDirectory(currentDirectory,install)&&IsPortableAppDirectory(currentDirectory)
   ?currentDirectory:null;
  return new UninstallPlan(install,NormalizedUninstallPath(Path.Combine(localAppData,"YandexMiniPlayer")),
   portable,NormalizedUninstallPath(desktopDirectory),executable,Path.Combine(install,"App","MiniPlayer.exe"),processId,processStartTicks);
 }

 internal static bool ShortcutTargetsApp(string target,UninstallPlan plan){
  if(string.IsNullOrWhiteSpace(target))return false;
  try{
   string resolved=NormalizedUninstallPath(Environment.ExpandEnvironmentVariables(target));
   return resolved.Equals(plan.Executable,StringComparison.OrdinalIgnoreCase)
    ||resolved.Equals(plan.InstalledExecutable,StringComparison.OrdinalIgnoreCase);
  }catch(ArgumentException){return false;}catch(NotSupportedException){return false;}
 }

 async void ConfirmUninstall(){
  if(uninstalling)return;
  if(LaunchContext.LauncherRoot!=null){
   try{
    var start=new ProcessStartInfo(Path.Combine(LaunchContext.LauncherRoot,"Launcher.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=LaunchContext.LauncherRoot};
    start.ArgumentList.Add("--remove-widget");start.ArgumentList.Add("mini-player");
    Process.Start(start)?.Dispose();
   }catch(Exception ex){MessageBox.Show(settingsWindow??this,"Не удалось открыть лаунчер.\n\n"+ex.Message,"Удаление виджета",MessageBoxButton.OK,MessageBoxImage.Error);}
   return;
  }
  var owner=settingsWindow??this;
  string? taskDirectory=null;
  try{
   string localAppData=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
   using var current=Process.GetCurrentProcess();
   var plan=CreateUninstallPlan(localAppData,Path.Combine(AppContext.BaseDirectory,"MiniPlayer.exe"),
    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),current.Id,current.StartTime.ToUniversalTime().Ticks);
   string description="Удалить Mini Player полностью?\n\nБудут удалены установленный плеер, настройки, горячие клавиши и ярлык на рабочем столе. Плеер закроется.";
   if(plan.PortableDirectory!=null)description+="\n\nФайлы запущенной распакованной копии в папке App также будут удалены.";
   else if(!IsWithinUninstallDirectory(plan.Executable,plan.InstallDirectory))
    description+="\n\nЭта копия запущена вне папки установки. Её файлы удаляются вручную.";
   description+="\n\nZIP-архивы и папки с исходниками удаляются вручную.";
   if(MessageBox.Show(owner,description,"Удаление Mini Player",MessageBoxButton.YesNo,
    MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
   uninstalling=true;
   taskDirectory=Path.Combine(Path.GetTempPath(),"MiniPlayer-Uninstall-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(taskDirectory);
   File.WriteAllText(Path.Combine(taskDirectory,"plan.json"),JsonSerializer.Serialize(plan),new UTF8Encoding(false));
   File.WriteAllText(Path.Combine(taskDirectory,"cleanup.ps1"),UninstallScript,new UTF8Encoding(true));
   var start=new ProcessStartInfo{
    FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),
    UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
    WorkingDirectory=taskDirectory
   };
   start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");
   start.ArgumentList.Add("-ExecutionPolicy");start.ArgumentList.Add("Bypass");
   start.ArgumentList.Add("-File");start.ArgumentList.Add(Path.Combine(taskDirectory,"cleanup.ps1"));
   start.Environment["LOCALAPPDATA"]=localAppData;
   using var helper=Process.Start(start)??throw new InvalidOperationException("Не удалось запустить удаление.");
   string ready=Path.Combine(taskDirectory,"ready");
   var timeout=Stopwatch.StartNew();
   while(!File.Exists(ready)){
    if(helper.HasExited||timeout.Elapsed>TimeSpan.FromSeconds(10)){
     string log=taskDirectory+".log";
     throw new InvalidOperationException(File.Exists(log)?File.ReadAllText(log):"Не удалось подготовить удаление. Попробуйте ещё раз.");
    }
    await Task.Delay(50);
   }
   // Closing writes the latest settings. Cleanup waits for the process to exit before removing them.
   closing=true;
   Application.Current.Shutdown();
  }catch(Exception ex){
   if(taskDirectory!=null&&Directory.Exists(taskDirectory)){
    try{File.WriteAllText(Path.Combine(taskDirectory,"cancel"),"");}catch(IOException){}catch(UnauthorizedAccessException){}
   }
   uninstalling=false;
   MessageBox.Show(owner,"Не удалось удалить Mini Player.\n\n"+ex.Message,"Удаление Mini Player",MessageBoxButton.OK,MessageBoxImage.Error);
  }
 }

 internal static string UninstallScript=>"""
$ErrorActionPreference='Stop'
$exitCode=0
$ownerExited=$false
$taskValidated=$false

function FullPath([string]$value){
    return [IO.Path]::GetFullPath($value).TrimEnd([IO.Path]::DirectorySeparatorChar)
}
function SamePath([string]$first,[string]$second){
    return [string]::Equals((FullPath $first),(FullPath $second),[StringComparison]::OrdinalIgnoreCase)
}
function Within([string]$path,[string]$root){
    $path=FullPath $path
    $root=FullPath $root
    return (SamePath $path $root) -or $path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
}
function PortableApp([string]$path){
    $path=FullPath $path
    if([IO.Path]::GetFileName($path) -ine 'App'){return $false}
    foreach($file in @('MiniPlayer.exe','MiniPlayer.dll','MiniPlayer.deps.json','MiniPlayer.runtimeconfig.json','Player.ico')){
        if(-not [IO.File]::Exists((Join-Path $path $file))){return $false}
    }
    $parent=[IO.DirectoryInfo]::new($path)
    while($null -ne $parent){
        $git=Join-Path $parent.FullName '.git'
        if([IO.File]::Exists($git) -or [IO.Directory]::Exists($git)){return $false}
        $parent=$parent.Parent
    }
    return $true
}
function RemoveOwnedPath([string]$path,[string]$root){
    $path=FullPath $path
    if(-not (Within $path $root)){throw 'Недопустимый путь удаления.'}
    try{$attributes=[IO.File]::GetAttributes($path)}
    catch [IO.FileNotFoundException]{return}
    catch [IO.DirectoryNotFoundException]{return}
    $directory=($attributes -band [IO.FileAttributes]::Directory) -ne 0
    $reparse=($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    if($directory){
        if(-not $reparse){
            foreach($child in [IO.Directory]::EnumerateFileSystemEntries($path)){
                RemoveOwnedPath $child $root
            }
        }
        [IO.Directory]::Delete($path,$false)
    }else{
        if(-not $reparse -and ($attributes -band [IO.FileAttributes]::ReadOnly) -ne 0){
            [IO.File]::SetAttributes($path,($attributes -band (-bnot [IO.FileAttributes]::ReadOnly)))
        }
        [IO.File]::Delete($path)
    }
}

try{
    $task=FullPath $PSScriptRoot
    if(-not (SamePath ([IO.Path]::GetDirectoryName($task)) ([IO.Path]::GetTempPath())) -or
       [IO.Path]::GetFileName($task) -notmatch '^MiniPlayer-Uninstall-[a-f0-9]{32}$'){
        throw 'Не удалось проверить папку помощника удаления.'
    }
    $taskValidated=$true
    $plan=Get-Content -LiteralPath (Join-Path $task 'plan.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $install=FullPath (Join-Path $env:LOCALAPPDATA 'MiniPlayer')
    $legacy=FullPath (Join-Path $env:LOCALAPPDATA 'YandexMiniPlayer')
    $desktop=[Environment]::GetFolderPath('DesktopDirectory')
    if(-not (SamePath $plan.InstallDirectory $install) -or
       -not (SamePath $plan.LegacyDirectory $legacy) -or
       -not (SamePath $plan.DesktopDirectory $desktop) -or
       -not (SamePath $plan.InstalledExecutable (Join-Path $install 'App\MiniPlayer.exe')) -or
       [IO.Path]::GetFileName($plan.Executable) -ine 'MiniPlayer.exe' -or
       $plan.ProcessId -le 0 -or $plan.ProcessStartTicks -le 0){
        throw 'Не удалось проверить расположение Mini Player.'
    }
    $portable=$null
    if(-not [string]::IsNullOrEmpty($plan.PortableDirectory)){
        $portable=FullPath $plan.PortableDirectory
        if(-not (SamePath $portable ([IO.Path]::GetDirectoryName($plan.Executable))) -or
           (Within $portable $install) -or -not (PortableApp $portable)){
            throw 'Не удалось проверить распакованную копию Mini Player.'
        }
    }
    $owner=$null
    try{$owner=[Diagnostics.Process]::GetProcessById([int]$plan.ProcessId)}catch [ArgumentException]{}
    $ownerMatches=$null -ne $owner -and $owner.StartTime.ToUniversalTime().Ticks -eq [long]$plan.ProcessStartTicks
    [IO.File]::WriteAllText((Join-Path $task 'ready'),'ready')
    if($null -ne $owner){
        try{
            if($ownerMatches){
                $wait=[Diagnostics.Stopwatch]::StartNew()
                while(-not $owner.WaitForExit(100)){
                    if([IO.File]::Exists((Join-Path $task 'cancel'))){return}
                    if($wait.Elapsed.TotalSeconds -gt 30){throw 'Плеер ещё работает. Закройте его и повторите удаление.'}
                }
            }
        }finally{$owner.Dispose()}
    }
    if([IO.File]::Exists((Join-Path $task 'cancel'))){return}
    $ownerExited=$true
    $shell=New-Object -ComObject WScript.Shell
    foreach($name in @('Mini Player.lnk','mini payer.lnk')){
        $shortcut=Join-Path $desktop $name
        if([IO.File]::Exists($shortcut)){
            $target=$shell.CreateShortcut($shortcut).TargetPath
            if(-not [string]::IsNullOrEmpty($target) -and
               ((SamePath ([Environment]::ExpandEnvironmentVariables($target)) $plan.Executable) -or
                (SamePath ([Environment]::ExpandEnvironmentVariables($target)) $plan.InstalledExecutable))){
                RemoveOwnedPath $shortcut $shortcut
            }
        }
    }
    $attempt=0
    while($true){
        try{
            RemoveOwnedPath $install $install
            if([IO.Directory]::Exists($legacy)){
                if(([IO.File]::GetAttributes($legacy) -band [IO.FileAttributes]::ReparsePoint) -ne 0){
                    [IO.Directory]::Delete($legacy,$false)
                }else{
                    foreach($name in @('settings.json','appearance.json')){
                        $file=Join-Path $legacy $name
                        RemoveOwnedPath $file $legacy
                    }
                    if([IO.Directory]::GetFileSystemEntries($legacy).Length -eq 0){[IO.Directory]::Delete($legacy,$false)}
                }
            }
            if($null -ne $portable){RemoveOwnedPath $portable $portable}
            break
        }catch{
            $attempt++
            if($attempt -ge 30){throw}
            Start-Sleep -Milliseconds 500
        }
    }
}catch{
    $exitCode=1
    $message=$_.Exception.Message
    try{[IO.File]::WriteAllText($PSScriptRoot+'.log',$message)}catch{}
    if($ownerExited){
        Add-Type -AssemblyName System.Windows.Forms
        [void][Windows.Forms.MessageBox]::Show('Не удалось завершить удаление Mini Player.'+[Environment]::NewLine+[Environment]::NewLine+$message,'Удаление Mini Player')
    }
}finally{
    if($taskValidated){try{RemoveOwnedPath $PSScriptRoot $PSScriptRoot}catch{}}
}
exit $exitCode
""";
}
