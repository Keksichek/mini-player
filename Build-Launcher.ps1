param(
 [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
 [string]$RuntimeCache = (Join-Path $PSScriptRoot '.build\downloads'),
 [switch]$SkipInstaller
)
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Run this build script with PowerShell 7.' }
$projectRoot=$PSScriptRoot
$buildRoot=Join-Path $projectRoot '.build'
$payload=Join-Path $buildRoot 'Payload'
$widget=Join-Path $buildRoot 'MiniPlayerPackage'
foreach ($directory in @($payload,$widget,$RuntimeCache,$OutputDirectory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$dotnetRoot=Split-Path (Get-Command dotnet).Source
$coreRuntime=Get-ChildItem (Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App') -Directory | Where-Object {$_.Name -like '10.0.*'} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$desktopRuntime=Get-ChildItem (Join-Path $dotnetRoot 'shared\Microsoft.WindowsDesktop.App') -Directory | Where-Object {$_.Name -like '10.0.*'} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
if (!$coreRuntime -or !$desktopRuntime) { throw '.NET Desktop Runtime 10 is required on the build machine.' }
[Microsoft.CodeAnalysis.MetadataReference[]]$baseReferences=@(
 foreach($file in Get-ChildItem -LiteralPath $coreRuntime.FullName,$desktopRuntime.FullName -Filter '*.dll' -File){
  try { [void][Reflection.AssemblyName]::GetAssemblyName($file.FullName) } catch { continue }
  [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)
 }
)
function Compile-App([string]$Name,[string]$SourceDirectory,[string]$Destination,[switch]$Player){
 $parse=[Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::Preview)
 [Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@(foreach($file in Get-ChildItem -LiteralPath $SourceDirectory -Filter '*.cs' -File){
  [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([Microsoft.CodeAnalysis.Text.SourceText]::From([IO.File]::ReadAllText($file.FullName),[Text.Encoding]::UTF8),$parse,$file.FullName)
 })
 [Microsoft.CodeAnalysis.MetadataReference[]]$references=$baseReferences
 if($Player){foreach($name in @('Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll')){$references+=[Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $projectRoot ('App\'+$name)))}}
 $options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::WindowsApplication).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
 $compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($Name,$trees,$references,$options)
 [Microsoft.CodeAnalysis.ResourceDescription[]]$resources=@(if($Player){foreach($file in Get-ChildItem (Join-Path $projectRoot 'screenshots') -Filter '*.png' | Where-Object {$_.Name -notlike '*-expanded.png' -and $_.Name -notlike '*preview.png'}){
  $resourceFile=$file.FullName
  $provider=[Func[IO.Stream]]{[IO.File]::OpenRead($resourceFile)}.GetNewClosure()
  [Microsoft.CodeAnalysis.ResourceDescription]::new(('preview.'+$file.Name),$provider,$true)
 }})
 $stream=[IO.File]::Create($Destination)
 try{$result=$compilation.Emit($stream,$null,$null,$null,$resources)}finally{$stream.Dispose()}
 foreach($diagnostic in $result.Diagnostics | Where-Object {$_.Severity -in 'Error','Warning' -and $_.Id -ne 'CS1701'} | Select-Object -Unique){Write-Host $diagnostic}
 if(!$result.Success){throw "$Name compilation failed."}
}

New-Item -ItemType Directory -Force -Path (Join-Path $widget 'App') | Out-Null
foreach($name in @('MiniPlayer.exe','MiniPlayer.deps.json','MiniPlayer.runtimeconfig.json','Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll','Player.ico')){
 Copy-Item -LiteralPath (Join-Path $projectRoot ('App\'+$name)) -Destination (Join-Path $widget 'App') -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'Source\Fonts') -Destination (Join-Path $widget 'App') -Recurse -Force
Compile-App 'MiniPlayer' (Join-Path $projectRoot 'Source') (Join-Path $widget 'App\MiniPlayer.dll') -Player
Compile-App 'Launcher' (Join-Path $projectRoot 'Launcher\Source') (Join-Path $payload 'Launcher.dll')
Copy-Item -LiteralPath (Join-Path $projectRoot 'App\MiniPlayer.runtimeconfig.json') -Destination (Join-Path $payload 'Launcher.runtimeconfig.json') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Source\Fonts') -Destination $payload -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Source\Player.ico') -Destination (Join-Path $payload 'Player.ico') -Force
foreach($name in @('Assets','Catalog','Packages','Runtime')){New-Item -ItemType Directory -Force -Path (Join-Path $payload $name) | Out-Null}
Copy-Item -LiteralPath (Join-Path $projectRoot 'screenshots\Лаванда.png') -Destination (Join-Path $payload 'Assets\mini-player.png') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Launcher\catalog.json') -Destination (Join-Path $payload 'Catalog\widgets.json') -Force
$widgetZip=Join-Path $payload 'Packages\mini-player.zip'
if(Test-Path -LiteralPath $widgetZip){Remove-Item -LiteralPath $widgetZip -Force}
[IO.Compression.ZipFile]::CreateFromDirectory($widget,$widgetZip,[IO.Compression.CompressionLevel]::Optimal,$false)

# Official Microsoft redistributable runtime archives, pinned and verified before extraction.
$packages=@(
 @{Name='dotnet-runtime-win-x64.zip';Url='https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.15/dotnet-runtime-10.0.15-win-x64.zip';Hash='06f0bedd59ae4ac722ada805a6d6bbf605bcdc790c71f59b7003878072902b5cde5dec8c40c0baa81192660d4748e9d79afdbdcb0c0ea9d6b821d0a044c4c6dd'},
 @{Name='windowsdesktop-runtime-win-x64.zip';Url='https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.15/windowsdesktop-runtime-10.0.15-win-x64.zip';Hash='d82ee2e70f0c1ee7af3a908a944541993782aa80a3fd2b2e02a3fdf27edc81c0ad946f14a3fe34bae295f1b7a23f5d35a466239726e4457bb200de0344e077c4'}
)
foreach($package in $packages){
 $archive=Join-Path $RuntimeCache $package.Name
 if(!(Test-Path -LiteralPath $archive)){Invoke-WebRequest -Uri $package.Url -OutFile $archive}
 if((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ine $package.Hash){throw ('Runtime checksum mismatch: '+$package.Name)}
 [IO.Compression.ZipFile]::ExtractToDirectory($archive,(Join-Path $payload 'Runtime'),$true)
}
if(!$SkipInstaller){& (Join-Path $projectRoot 'Installer\Build.ps1') -PayloadDirectory $payload -OutputDirectory $OutputDirectory}
Write-Host ('Payload: '+$payload)
