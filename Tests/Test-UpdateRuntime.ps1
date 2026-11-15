param(
 [Parameter(Mandatory=$true)][string]$InstallDirectory,
 [Parameter(Mandatory=$true)][string]$GoodFeedDirectory,
 [string]$OutputDirectory,
 [string]$SigningKeyPath
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$root=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($InstallDirectory))
$feed=[IO.Path]::GetFullPath($GoodFeedDirectory)
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo '.build\UpdateRuntimeChecks'}
$output=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('run-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
if(!$SigningKeyPath){$SigningKeyPath=Join-Path $repo '.build\update-signing\private.xml'}
function PathHash([string]$path){[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($path)).ToUpperInvariant()))).Substring(0,16)}
function NoLinks([string]$path){for($next=[IO.Path]::GetFullPath($path);$next;$next=[IO.Path]::GetDirectoryName($next)){if((Test-Path -LiteralPath $next)-and([IO.File]::GetAttributes($next)-band[IO.FileAttributes]::ReparsePoint)){throw 'Runtime checks refuse symbolic links or junctions.'}}}
NoLinks $root;NoLinks $feed;NoLinks $output;NoLinks $SigningKeyPath
$marker=Get-Content -LiteralPath (Join-Path $root 'launcher-install.json') -Raw|ConvertFrom-Json
if($marker.TestInstallation-ne$true-or$marker.ProductId-ne'MiniPlayer.WidgetLauncher'-or[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($marker.InstallPath))-ine$root-or$marker.InstallId-ine(PathHash $root)){throw 'Only a verified isolated test installation may be updated by these checks.'}
foreach($file in @('Update.exe','Runtime\dotnet.exe','Launcher.dll','Widgets\mini-player\App\MiniPlayer.dll','Widgets\mini-player\.widget-install.json')){NoLinks (Join-Path $root $file);if(!(Test-Path -LiteralPath (Join-Path $root $file))){throw ('Missing installed test component: '+$file)}}
$runtime=Join-Path $root 'Runtime\dotnet.exe';$data=Join-Path $root 'Data';$hubName='Local\WidgetLauncher.'+(PathHash $root);$widgetName='Local\MiniPlayer.'+(PathHash (Join-Path $data 'mini-player'))
function IsRunning([string]$name){$mutex=$null;if(![Threading.Mutex]::TryOpenExisting($name,[ref]$mutex)){return $false};try{try{if($mutex.WaitOne(0)){$mutex.ReleaseMutex();return $false};return $true}catch [Threading.AbandonedMutexException]{$mutex.ReleaseMutex();return $false}}finally{$mutex.Dispose()}}
function Signal([string]$name){$handle=$null;if([Threading.EventWaitHandle]::TryOpenExisting($name,[ref]$handle)){try{$handle.Set()|Out-Null}finally{$handle.Dispose()}}}
function Ready([string]$name){$handle=$null;if(![Threading.EventWaitHandle]::TryOpenExisting($name,[ref]$handle)){return $false};try{return $handle.WaitOne(0)}finally{$handle.Dispose()}}
function FixturePids(){foreach($process in [Diagnostics.Process]::GetProcessesByName('dotnet')){try{if(!$process.HasExited-and$process.MainModule.FileName-ieq$runtime){$process.Id}}catch{}finally{$process.Dispose()}}}
function StopFixtures(){
 $ids=@(FixturePids);$deadline=[DateTime]::UtcNow.AddSeconds(20)
 do{Signal ($hubName+'.Stop');Signal ($widgetName+'.Stop');if(!(IsRunning $hubName)-and!(IsRunning $widgetName)){break};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow-lt$deadline)
 if((IsRunning $hubName)-or(IsRunning $widgetName)){throw 'Test applications did not stop gracefully.'}
 foreach($id in $ids){try{$process=[Diagnostics.Process]::GetProcessById($id);try{if(!$process.WaitForExit(5000)){throw 'A test process is still shutting down.'}}finally{$process.Dispose()}}catch [ArgumentException]{}}
}
function WaitResumed(){
 $deadline=[DateTime]::UtcNow.AddSeconds(20)
 do{if((IsRunning $hubName)-and(IsRunning $widgetName)-and(Ready ($widgetName+'.Ready'))){return};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow-lt$deadline)
 throw 'The launcher and selected player did not resume from the updated/restored installation.'
}
function TreeSnapshot([string]$directory){
 $result=[ordered]@{}
 function Visit([string]$folder){NoLinks $folder;foreach($file in Get-ChildItem -LiteralPath $folder -File -Force){NoLinks $file.FullName;$relative=[IO.Path]::GetRelativePath($directory,$file.FullName);$result[$relative]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash};foreach($child in Get-ChildItem -LiteralPath $folder -Directory -Force){Visit $child.FullName}}
 Visit $directory
 return ($result.GetEnumerator()|Sort-Object Key|ForEach-Object{$_.Key+':'+$_.Value}) -join "`n"
}
$script:checks=0
function Check([bool]$condition,[string]$message){if(!$condition){throw $message};$script:checks++;Write-Output ('PASS '+$message)}
function RunUpdate([string]$source,[string]$name,[int]$expected){
 $report=Join-Path $output ($name+'.txt');$start=[Diagnostics.ProcessStartInfo]::new($helper);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden;$start.WorkingDirectory=$output
 foreach($value in @('--test-update',$root,$source,'--resume','mini-player','--report',$report)){$start.ArgumentList.Add($value)}
 $process=[Diagnostics.Process]::Start($start)
 try{if(!$process.WaitForExit(60000)){throw ('Update helper timed out: '+$name)};if($process.ExitCode-ne$expected){throw ('Unexpected update exit code '+$process.ExitCode+': '+(Get-Content -LiteralPath $report -Raw))};return [IO.File]::ReadAllText($report)}finally{$process.Dispose()}
}
if((IsRunning $hubName)-or(IsRunning $widgetName)){throw 'Close this isolated test hub and player before running update checks.'}
$good=Get-Content -LiteralPath (Join-Path $feed 'update.json') -Raw|ConvertFrom-Json
$state=Join-Path $root 'update-state.json';$beforeBuild=if(Test-Path -LiteralPath $state){[long](Get-Content -LiteralPath $state -Raw|ConvertFrom-Json).BuildNumber}else{0}
if([long]$good.BuildNumber-le$beforeBuild){throw 'Use a fresh test installation or a newer good feed for this runtime check.'}
New-Item -ItemType Directory -Force -Path $output|Out-Null
$helper=Join-Path $output 'Update.exe';[IO.File]::Copy((Join-Path $root 'Update.exe'),$helper)
$extra=Join-Path $root 'Widgets\mini-player\user-update-check.txt';[IO.File]::WriteAllText($extra,'preserved own fixture file')
$beforeData=TreeSnapshot $data
try{
 $result=RunUpdate $feed 'good-update' 0
 Check ($result.StartsWith('PASS ') -and [long](Get-Content -LiteralPath $state -Raw|ConvertFrom-Json).BuildNumber-eq[long]$good.BuildNumber) 'A signed good update commits its build number after a real startup handshake'
 WaitResumed;Check $true 'The updated launcher and installed player resume from the private runtime'
 StopFixtures
 Check ((TreeSnapshot $data)-ceq$beforeData) 'Successful update preserves all Data files byte for byte'
 Check ((Get-Content -LiteralPath $extra -Raw)-eq'preserved own fixture file') 'An extra file inside an installed widget is preserved'
 $outer=[IO.Compression.ZipFile]::OpenRead((Join-Path $feed $good.Package))
 try{
  $catalogStream=$outer.GetEntry('Catalog/widgets.json');if(!$catalogStream){$catalogStream=$outer.GetEntry('Catalog\widgets.json')}
  $reader=[IO.StreamReader]::new($catalogStream.Open());try{$catalog=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
  $widget=$catalog|Where-Object Id -eq 'mini-player'|Select-Object -First 1
  $entry=$outer.GetEntry(('Packages/'+$widget.Package.Replace('\','/')))
  if(!$entry){throw 'The good feed has no mini-player package.'}
  $memory=[IO.MemoryStream]::new();$input=$entry.Open();try{$input.CopyTo($memory)}finally{$input.Dispose()};$memory.Position=0
  $inner=[IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Read)
  try{
   $matched=0
   foreach($item in $inner.Entries){if($item.FullName.EndsWith('/')){continue};$target=Join-Path (Join-Path $root 'Widgets\mini-player') $item.FullName;NoLinks $target;$stream=$item.Open();$sha=[Security.Cryptography.SHA256]::Create();try{$expected=[Convert]::ToHexString($sha.ComputeHash($stream))}finally{$sha.Dispose();$stream.Dispose()};if(!(Test-Path -LiteralPath $target)-or(Get-FileHash -LiteralPath $target).Hash-ne$expected){throw ('Installed widget differs from its update package: '+$item.FullName)};$matched++}
   Check ($matched-gt 0) 'The installed widget was replaced directly from the new package without reinstalling it manually'
  }finally{$inner.Dispose();$memory.Dispose()}
 }finally{$outer.Dispose()}
 $goodCore=(Get-FileHash -LiteralPath (Join-Path $root 'Launcher.dll')).Hash;$goodWidget=(Get-FileHash -LiteralPath (Join-Path $root 'Widgets\mini-player\App\MiniPlayer.dll')).Hash;$goodState=(Get-FileHash -LiteralPath $state).Hash;$goodData=TreeSnapshot $data
 $bad=Join-Path $output 'bad-health-feed';[IO.Directory]::CreateDirectory($bad)|Out-Null
 $badBuild=Join-Path $output 'unhealthy-launcher';[IO.Directory]::CreateDirectory($badBuild)|Out-Null
 $badSource=Join-Path $badBuild 'Launcher.cs';$badAssembly=Join-Path $badBuild 'Launcher.dll'
 [IO.File]::WriteAllText($badSource,'internal static class DeliberatelyUnhealthyLauncher { [System.STAThread] static int Main(string[] args) { return 7; } }')
 & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /platform:x64 /optimize+ ('/out:'+$badAssembly) $badSource
 if($LASTEXITCODE-ne 0){throw 'Could not build the deliberately unhealthy managed launcher fixture.'}
 $badZip=Join-Path $bad $good.Package;[IO.File]::Copy((Join-Path $feed $good.Package),$badZip)
 $zip=[IO.Compression.ZipFile]::Open($badZip,[IO.Compression.ZipArchiveMode]::Update)
 try{$broken=$zip.GetEntry('Launcher.dll');if(!$broken){throw 'Launcher.dll missing from test feed'};$broken.Delete();$target=$zip.CreateEntry('Launcher.dll').Open();$source=[IO.File]::OpenRead($badAssembly);try{$source.CopyTo($target)}finally{$source.Dispose();$target.Dispose()}}finally{$zip.Dispose()}
 $badManifest=[ordered]@{ProductId=$good.ProductId;Channel=$good.Channel;BuildNumber=([long]$good.BuildNumber+1);Version=$good.Version;Package=$good.Package;Sha256=(Get-FileHash -LiteralPath $badZip).Hash.ToLowerInvariant();Size=(Get-Item -LiteralPath $badZip).Length;Notes='Own test: valid signature with a launcher that fails startup.'}
 $utf8=[Text.UTF8Encoding]::new($false);$bytes=$utf8.GetBytes(($badManifest|ConvertTo-Json -Compress));[IO.File]::WriteAllBytes((Join-Path $bad 'update.json'),$bytes)
 $signer=[Security.Cryptography.RSA]::Create()
 try{$signer.FromXmlString([IO.File]::ReadAllText($SigningKeyPath));$signature=$signer.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1);[IO.File]::WriteAllText((Join-Path $bad 'update.sig'),[Convert]::ToBase64String($signature),$utf8)}finally{$signer.Dispose()}
 $result=RunUpdate $bad 'bad-health-rollback' 2
 Check ($result.StartsWith('ROLLBACK ')) 'A correctly signed build that cannot start triggers an actual rollback'
 WaitResumed;Check $true 'The previous launcher and player resume after rollback'
 StopFixtures
 Check ((Get-FileHash -LiteralPath (Join-Path $root 'Launcher.dll')).Hash-eq$goodCore-and(Get-FileHash -LiteralPath (Join-Path $root 'Widgets\mini-player\App\MiniPlayer.dll')).Hash-eq$goodWidget-and(Get-FileHash -LiteralPath $state).Hash-eq$goodState) 'Rollback restores launcher, installed widget and previous update state'
 Check ((TreeSnapshot $data)-ceq$goodData-and(Get-Content -LiteralPath $extra -Raw)-eq'preserved own fixture file') 'Rollback preserves saved settings, hotkeys, appearance and extra widget files'
 $beforeRepeat=TreeSnapshot $root;$result=RunUpdate $feed 'repeat-same-update' 1
 Check ($result.StartsWith('FAIL ')-and(TreeSnapshot $root)-ceq$beforeRepeat) 'Repeating the already installed build is rejected without altering the installation'
 Write-Output ('TOTAL '+$script:checks+' PASS')
 Write-Output ('Update runtime reports: '+$output)
}finally{StopFixtures}
