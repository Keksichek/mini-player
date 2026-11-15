param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('.build\RelocationChecks-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
$output=[IO.Path]::GetFullPath($OutputDirectory);New-Item -ItemType Directory -Force -Path $output | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$test=Join-Path $output 'RelocationChecks.exe'
& $compiler /nologo /target:exe /platform:x64 /utf8output /main:RelocationChecks ('/out:'+$test) ('/win32manifest:'+(Join-Path $repo 'Installer\app.manifest')) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $repo 'Installer\Shared.cs') (Join-Path $repo 'Installer\UiControls.cs') (Join-Path $repo 'Installer\Relocate.cs') (Join-Path $PSScriptRoot 'RelocationChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Relocation checks did not compile'}
& $test (Join-Path $output 'Data')
if($LASTEXITCODE -ne 0){throw 'Relocation checks failed'}
