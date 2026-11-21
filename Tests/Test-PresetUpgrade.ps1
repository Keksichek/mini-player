param([Parameter(Mandatory=$true)][string]$InstallDirectory,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('.build\PresetUpgrade-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
$output=[IO.Path]::GetFullPath($OutputDirectory);New-Item -ItemType Directory -Force -Path $output|Out-Null
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$dotnet=Split-Path (Get-Command dotnet).Source
$core=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.NETCore.App') -Directory|Where-Object Name -like '10.*'|Sort-Object {[version]$_.Name} -Descending|Select-Object -First 1
$desktop=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.WindowsDesktop.App') -Directory|Where-Object Name -like '10.*'|Sort-Object {[version]$_.Name} -Descending|Select-Object -First 1
[Microsoft.CodeAnalysis.MetadataReference[]]$references=@(foreach($file in Get-ChildItem -LiteralPath $core.FullName,$desktop.FullName -Filter '*.dll'){try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)}catch{continue};[Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)})
$files=@((Join-Path $repo 'Launcher\Source\PresetWidgetUpgrade.cs'),(Join-Path $repo 'Launcher\Source\WidgetManager.cs'),(Join-Path $PSScriptRoot 'PresetUpgradeChecks.cs'))
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@(foreach($file in $files){[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file))})
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('PresetUpgradeChecks',$trees,$references,$options)
$dll=Join-Path $output 'PresetUpgradeChecks.dll';$stream=[IO.File]::Create($dll)
try{$result=$compilation.Emit($stream);if(!$result.Success){$result.Diagnostics|ForEach-Object ToString;throw 'Preset upgrade checks compilation failed.'}}finally{$stream.Dispose()}
[IO.File]::WriteAllText((Join-Path $output 'PresetUpgradeChecks.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net10.0-windows","framework":{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}}}')
$badSource=Join-Path $output 'MiniPlayer.cs';$badDll=Join-Path $output 'MiniPlayer.dll'
[IO.File]::WriteAllText($badSource,'class BrokenPlayer { static int Main() { return 7; } }')
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe ('/out:'+$badDll) $badSource
if($LASTEXITCODE -ne 0){throw 'Broken fixture build failed'}
& dotnet $dll ([IO.Path]::GetFullPath($InstallDirectory)) $badDll
if($LASTEXITCODE -ne 0){throw 'Preset upgrade checks failed'}