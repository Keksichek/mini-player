param([Parameter(Mandatory=$true)][string]$InstallDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($InstallDirectory))
function PathHash([string]$path){[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($path)).ToUpperInvariant()))).Substring(0,16)}
$marker=Get-Content -LiteralPath (Join-Path $root 'launcher-install.json') -Raw | ConvertFrom-Json
if($marker.TestInstallation -ne $true -or $marker.ProductId -ne 'MiniPlayer.WidgetLauncher' -or [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($marker.InstallPath)) -ine $root -or $marker.InstallId -ine (PathHash $root)){throw 'Only a verified isolated test installation is allowed.'}
$runtime=Join-Path $root 'Runtime\dotnet.exe';$launcher=Join-Path $root 'Launcher.dll';$widget=Join-Path $root 'Widgets\mini-player\App\MiniPlayer.dll';$data=Join-Path $root 'Data\mini-player'
foreach($required in @($runtime,$launcher,$widget,(Join-Path $root 'Widgets\mini-player\.widget-install.json'))){if(!(Test-Path -LiteralPath $required)){throw ('Test fixture is not installed: '+$required)}}
$launcherName='Local\WidgetLauncher.'+(PathHash $root);$widgetName='Local\MiniPlayer.'+(PathHash $data)
function IsRunning([string]$name){
 $handle=$null;if(![Threading.Mutex]::TryOpenExisting($name,[ref]$handle)){return $false}
 try{try{if($handle.WaitOne(0)){$handle.ReleaseMutex();return $false};return $true}catch [Threading.AbandonedMutexException]{$handle.ReleaseMutex();return $false}}finally{$handle.Dispose()}
}
function Ready([string]$name){$handle=$null;if(![Threading.EventWaitHandle]::TryOpenExisting($name,[ref]$handle)){return $false};try{return $handle.WaitOne(0)}finally{$handle.Dispose()}}
function EventExists([string]$name){$handle=$null;if(![Threading.EventWaitHandle]::TryOpenExisting($name,[ref]$handle)){return $false};$handle.Dispose();return $true}
function Signal([string]$name){$handle=$null;if([Threading.EventWaitHandle]::TryOpenExisting($name,[ref]$handle)){try{$handle.Set()|Out-Null}finally{$handle.Dispose()}}}
if((IsRunning $launcherName)-or(IsRunning $widgetName)){throw 'Close this test installation before starting the runtime checks.'}
New-Item -ItemType Directory -Path $data -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'settings.json'),'{"X":80,"Y":80,"Pin":false,"Auto":false,"Source":"","MovementLocked":true}')
[IO.File]::WriteAllText((Join-Path $data 'appearance.json'),'{"Theme":"Лаванда","Accent":"#B9A1FF"}')
[IO.File]::WriteAllText((Join-Path $data 'hotkeys.json'),'{}')
$script:checks=0;$commands=[Collections.Generic.List[Diagnostics.Process]]::new()
function Check([bool]$condition,[string]$message){if(!$condition){throw $message};$script:checks++;Write-Output ('PASS '+$message)}
function Preferences([int]$mode,[string[]]$selected){[IO.File]::WriteAllText((Join-Path $root 'Data\hub-settings.json'),(@{AutoStartMode=$mode;SelectedWidgets=@($selected);LightMode=$false;AllowOffscreen=$true}|ConvertTo-Json -Depth 4))}
function StartCommand([string[]]$values){
 $start=[Diagnostics.ProcessStartInfo]::new($runtime);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden;$start.WorkingDirectory=$root
 foreach($value in @($launcher)+$values){$start.ArgumentList.Add($value)}
 $process=[Diagnostics.Process]::Start($start);$commands.Add($process);return $process
}
function WaitState($process,[bool]$hub,[bool]$mini){
 $deadline=[DateTime]::UtcNow.AddSeconds(20)
 do{
  $hubRunning=IsRunning $launcherName;$miniRunning=IsRunning $widgetName
  $miniReady=(!$mini)-or(Ready ($widgetName+'.Ready'))
  $hubReady=(!$hub)-or(EventExists ($launcherName+'.Stop'))
  $commandReady=($hub-and!$process.HasExited)-or(!$hub-and$process.HasExited-and$process.ExitCode-eq 0)
  if($hubRunning-eq$hub-and$miniRunning-eq$mini-and$miniReady-and$hubReady-and$commandReady){return}
  if($process.HasExited-and$process.ExitCode-ne 0){throw ('Startup command failed: '+$process.ExitCode)}
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow-lt$deadline)
 throw ('Startup state timed out. Expected launcher='+$hub+', player='+$mini+'; actual launcher='+(IsRunning $launcherName)+', player='+(IsRunning $widgetName))
}
function FixturePids(){
 foreach($process in [Diagnostics.Process]::GetProcessesByName('dotnet')){try{if(!$process.HasExited-and$process.MainModule.FileName-ieq$runtime){$process.Id}}catch{}finally{$process.Dispose()}}
}
function StopFixtures(){
 $ids=@(FixturePids);$deadline=[DateTime]::UtcNow.AddSeconds(20)
 do{Signal ($launcherName+'.Stop');Signal ($widgetName+'.Stop');if(!(IsRunning $launcherName)-and!(IsRunning $widgetName)){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow-lt$deadline)
 if((IsRunning $launcherName)-or(IsRunning $widgetName)){throw 'Test applications did not stop gracefully.'}
 foreach($id in $ids){try{$process=[Diagnostics.Process]::GetProcessById($id);try{if(!$process.WaitForExit(5000)){throw 'Test process is still shutting down.'}}finally{$process.Dispose()}}catch [ArgumentException]{}}
 foreach($command in $commands){if(!$command.HasExited-and!$command.WaitForExit(5000)){throw 'Startup command is still shutting down.'}}
}
try{
 Preferences 0 @();$process=StartCommand @('--autostart');WaitState $process $false $false;Check $true 'Disabled startup opens no applications';StopFixtures
 Preferences 2 @();$process=StartCommand @('--autostart');WaitState $process $false $true;Check $true 'All-widgets mode starts the installed player without launcher'
 Start-Sleep -Milliseconds 300;$before=@(FixturePids);$stamp=[IO.File]::GetLastWriteTimeUtc((Join-Path $data 'settings.json')).Ticks
 $repeat=StartCommand @('--autostart');WaitState $repeat $false $true;Start-Sleep -Milliseconds 150;$after=@(FixturePids)
 Check (($before.Count-eq 1)-and($after.Count-eq 1)-and($before[0]-eq$after[0])) 'Repeated widget startup keeps the same player process'
 Check ([IO.File]::GetLastWriteTimeUtc((Join-Path $data 'settings.json')).Ticks-eq$stamp) 'Repeated automatic startup sends no manual Show command';StopFixtures
 Preferences 3 @();$process=StartCommand @('--autostart');WaitState $process $false $false;Check $true 'Selected-widgets mode with an empty selection opens nothing';StopFixtures
 Preferences 3 @('mini-player');$process=StartCommand @('--autostart');WaitState $process $false $true;Check $true 'Selected-widgets mode starts the selected player without launcher';StopFixtures
 Preferences 4 @();$process=StartCommand @('--autostart');WaitState $process $true $false;Check $true 'Launcher-and-selection mode with no selection starts only launcher';StopFixtures
 Preferences 4 @('mini-player');$process=StartCommand @('--autostart');WaitState $process $true $true;Check $true 'Launcher-and-selection mode starts launcher and selected player';StopFixtures
 Preferences 1 @();$process=StartCommand @('--autostart');WaitState $process $true $true;Check $true 'Full startup starts launcher and all installed widgets';StopFixtures
 Preferences 0 @();$process=StartCommand @('--resume-widgets','mini-player');WaitState $process $true $true;Check $true 'Relocation resume starts the player and launcher independently of disabled autostart';StopFixtures
 Write-Output ('TOTAL '+$script:checks+' PASS')
}finally{try{StopFixtures}finally{foreach($command in $commands){$command.Dispose()}}}
