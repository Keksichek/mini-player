param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo '.build\UpdateEngineChecks'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $output 'UpdateEngineChecks.exe'
& $compiler /nologo /target:exe /platform:x64 /utf8output ('/out:'+$exe) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Runtime.Serialization.dll ('/resource:'+(Join-Path $repo 'Shared\UpdatePublicKey.xml')+',UpdatePublicKey.xml') /main:UpdateEngineChecks (Join-Path $repo 'Installer\Shared.cs') (Join-Path $repo 'Installer\UiControls.cs') (Join-Path $repo 'Shared\UpdateProtocol.cs') (Join-Path $repo 'Installer\Update.cs') (Join-Path $PSScriptRoot 'UpdateEngineChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Update engine check compilation failed.'}
& $exe (Join-Path $output ('Fixtures-'+[Guid]::NewGuid().ToString('N'))) (Join-Path $repo '.build\update-signing\private.xml')
if($LASTEXITCODE -ne 0){throw 'Update engine checks failed.'}
