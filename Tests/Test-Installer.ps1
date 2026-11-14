param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $env:TEMP ('WidgetLauncher-InstallerChecks-' + [Guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$repoRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exePath = Join-Path $OutputDirectory 'InstallerChecks.exe'
& $compiler /nologo /target:exe /platform:x64 /utf8output /out:$exePath /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $repoRoot 'Installer\Shared.cs') (Join-Path $PSScriptRoot 'InstallerChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer check compilation failed.' }
$scratch = Join-Path $OutputDirectory ('scratch-' + [Guid]::NewGuid().ToString('N'))
$linkTarget = Join-Path $scratch 'junction-target'
New-Item -ItemType Directory -Path $linkTarget -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $linkTarget 'keep.txt'), 'keep')
New-Item -ItemType Junction -Path (Join-Path $scratch 'junction-root') -Target $linkTarget | Out-Null
& $exePath $scratch
if ($LASTEXITCODE -ne 0) { throw 'Installer checks failed.' }
