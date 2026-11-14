param(
 [Parameter(Mandatory=$true)][string]$PayloadDirectory,
 [Parameter(Mandatory=$true)][string]$OutputDirectory,
 [switch]$BootstrapOnly
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $compiler)){throw '.NET Framework C# compiler was not found.'}
$payload=[IO.Path]::GetFullPath($PayloadDirectory)
$output=[IO.Path]::GetFullPath($OutputDirectory)
if($output.TrimEnd('\').Equals($payload.TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($payload.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'OutputDirectory must be outside PayloadDirectory.'}
New-Item -ItemType Directory -Force -Path $payload,$output | Out-Null
$common=@('/nologo','/target:winexe','/platform:x64','/optimize+',('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/win32icon:'+(Join-Path $projectRoot 'Source\Player.ico')),'/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',('/resource:'+(Join-Path $projectRoot 'Source\Fonts\Onest-Bold.ttf')+',Onest.ttf'),('/resource:'+(Join-Path $projectRoot 'Source\Player.ico')+',Player.ico'))
foreach($entry in @(@{Source='Bootstrap.cs';Name='Launcher.exe'},@{Source='Uninstall.cs';Name='Uninstall.exe'})){
 & $compiler @common ('/out:'+(Join-Path $payload $entry.Name)) (Join-Path $PSScriptRoot 'Shared.cs') (Join-Path $PSScriptRoot $entry.Source)
 if($LASTEXITCODE -ne 0){throw ('Failed to compile '+$entry.Name)}
}
if($BootstrapOnly){return}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=Join-Path $output ('Payload-'+[Guid]::NewGuid().ToString('N')+'.zip')
try{
 [IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
 $setup=Join-Path $output 'WidgetLauncher-Setup.exe'
 & $compiler @common ('/out:'+$setup) ('/resource:'+$zip+',Payload.zip') (Join-Path $PSScriptRoot 'Shared.cs') (Join-Path $PSScriptRoot 'Setup.cs')
 if($LASTEXITCODE -ne 0){throw 'Failed to compile installer.'}
 Get-Item -LiteralPath $setup | Select-Object FullName,Length
}finally{if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip -Force}}
