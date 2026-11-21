param([string]$PlayerDirectory,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if(!$PlayerDirectory){$PlayerDirectory=Join-Path $projectRoot '.build\MiniPlayerPackage\App'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $projectRoot ('.build\source-checks-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $PlayerDirectory | ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $OutputDirectory -Recurse -Force}
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$dotnetRoot=Split-Path (Get-Command dotnet).Source
$core=Get-ChildItem (Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$desktop=Get-ChildItem (Join-Path $dotnetRoot 'shared\Microsoft.WindowsDesktop.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
[Microsoft.CodeAnalysis.MetadataReference[]]$references=@(
 foreach($file in Get-ChildItem -LiteralPath $core.FullName,$desktop.FullName -Filter '*.dll'){
  try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)}catch{continue}
  [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)
 }
 foreach($name in @('Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll','MiniPlayer.dll')){[Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $OutputDirectory $name))}
)
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'SourceSelectionChecks.cs'))))
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('SourceSelectionChecks',$trees,$references,$options)
$dll=Join-Path $OutputDirectory 'SourceSelectionChecks.dll';$stream=[IO.File]::Create($dll)
try{$result=$compilation.Emit($stream);if(!$result.Success){$result.Diagnostics | ForEach-Object ToString;throw 'Source selection checks compilation failed.'}}finally{$stream.Dispose()}
Copy-Item -LiteralPath (Join-Path $OutputDirectory 'MiniPlayer.runtimeconfig.json') -Destination (Join-Path $OutputDirectory 'SourceSelectionChecks.runtimeconfig.json')
$fixture=Join-Path $OutputDirectory 'test-data'
$catalog=New-Item -ItemType Directory -Path (Join-Path $fixture 'Catalog') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Launcher\catalog.json') -Destination (Join-Path $catalog.FullName 'widgets.json') -Force
& dotnet $dll $fixture (Join-Path $projectRoot '.build\Payload\Launcher.dll')
if($LASTEXITCODE -ne 0){throw 'Source selection checks failed.'}
