param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repoRoot ('.build\uninstall-confirm-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
$payload=Join-Path $output 'Payload'
& (Join-Path $repoRoot 'Installer\Build.ps1') -PayloadDirectory $payload -OutputDirectory (Join-Path $output 'Build') -BootstrapOnly
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$checks=Join-Path $output 'UninstallGateChecks.exe'
& $compiler /nologo /target:exe /platform:x64 /utf8output ('/out:'+$checks) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $repoRoot 'Installer\Shared.cs') (Join-Path $repoRoot 'Installer\UiControls.cs') (Join-Path $repoRoot 'Installer\UninstallGate.cs') (Join-Path $PSScriptRoot 'UninstallGateChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Gate check compilation failed'}
& $checks (Join-Path $output 'gate-fixture')
if($LASTEXITCODE -ne 0){throw 'Gate checks failed'}
function RootHash([string]$root){[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($root).TrimEnd('\').ToUpperInvariant()))).Substring(0,16)}
function TestRoot([string]$name){
 $root=Join-Path $output $name
 New-Item -ItemType Directory -Force -Path $root | Out-Null
 $marker=@{ProductId='MiniPlayer.WidgetLauncher';InstallPath=$root;InstallId=(RootHash $root);Files=@();WidgetIds=@();TestInstallation=$true;Version='test'}
 [IO.File]::WriteAllText((Join-Path $root 'launcher-install.json'),($marker|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
 return $root
}
function StartConfirm([string]$root,[string]$report,[int]$expected){
 $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $payload 'Uninstall.exe'))
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
 foreach($arg in @('--test-confirm',$root,'--report',$report,'--expect-attention',$expected.ToString())){$start.ArgumentList.Add($arg)}
 return [Diagnostics.Process]::Start($start)
}
function WaitGate([string]$root){
 $name='Local\WidgetLauncher.Uninstall.'+(RootHash $root)+'.Attention'
 for($i=0;$i -lt 100;$i++){$handle=$null;if([Threading.EventWaitHandle]::TryOpenExisting($name,[ref]$handle)){$handle.Dispose();return};Start-Sleep -Milliseconds 50}
 throw 'Confirmation gate did not appear'
}
function WaitResult($process,[string]$path){if(!$process.WaitForExit(25000)){throw 'Confirmation test timed out'};if($process.ExitCode -ne 0){throw 'Confirmation process failed'};return [IO.File]::ReadAllText($path)}
$first=TestRoot 'Первая установка [проверка]'
$second=TestRoot 'Вторая установка'
$ownerReport=Join-Path $output 'owner.txt'
$owner=StartConfirm $first $ownerReport 3
try{
 WaitGate $first
 $independentReport=Join-Path $output 'independent.txt'
 $independent=StartConfirm $second $independentReport 0
 if((WaitResult $independent $independentReport) -ne 'OWNED 0'){throw 'Different installation was treated as duplicate'}
 'PASS Different installations can show independent confirmations'
 for($i=0;$i -lt 3;$i++){
  $report=Join-Path $output ('duplicate-'+$i+'.txt')
  $duplicate=StartConfirm $first $report 0
  if((WaitResult $duplicate $report) -ne 'DUPLICATE'){throw 'Duplicate confirmation window was created'}
 }
 if((WaitResult $owner $ownerReport) -ne 'OWNED 3'){throw 'Owner did not receive each repeated request'}
 'PASS Repeated requests reuse one confirmation and reach its attention handler'
 $againReport=Join-Path $output 'reopened.txt';$again=StartConfirm $first $againReport 0
 if((WaitResult $again $againReport) -ne 'OWNED 0'){throw 'Confirmation did not reopen after cancellation'}
 'PASS Confirmation can reopen after cancellation'
 if(!(Test-Path -LiteralPath (Join-Path $first 'launcher-install.json')) -or !(Test-Path -LiteralPath (Join-Path $second 'launcher-install.json'))){throw 'Test confirmation changed installation files'}
 'PASS Confirmation checks never delete installation files'
}finally{if(!$owner.HasExited){$owner.CloseMainWindow()|Out-Null;$owner.WaitForExit(1000)|Out-Null}}
