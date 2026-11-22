param([string]$OutputDirectory,[Parameter(Mandatory=$true)][string]$PreviewPath)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo '.build\ObsEditorChecks'}
$output=[IO.Path]::GetFullPath($OutputDirectory);New-Item -ItemType Directory -Force -Path $output | Out-Null
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll');Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$dotnet=Split-Path (Get-Command dotnet).Source
$core=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.NETCore.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$desktop=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.WindowsDesktop.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
[Microsoft.CodeAnalysis.MetadataReference[]]$references=@(foreach($file in Get-ChildItem -LiteralPath $core.FullName,$desktop.FullName -Filter '*.dll'){try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)}catch{continue};[Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)})
$files=@(Get-ChildItem (Join-Path $repo 'Launcher\Source') -Filter '*.cs' -File | ForEach-Object FullName)+(Join-Path $repo 'Shared\UpdateProtocol.cs')+(Join-Path $PSScriptRoot 'ObsEditorChecks.cs')
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@(foreach($file in $files){[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file))})
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable).WithMainTypeName('ObsEditorChecks')
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('ObsEditorChecks',$trees,$references,$options)
$dll=Join-Path $output 'ObsEditorChecks.dll';$stream=[IO.File]::Create($dll);try{$result=$compilation.Emit($stream);if(!$result.Success){$result.Diagnostics|ForEach-Object ToString;throw 'GitHub checks compilation failed'}}finally{$stream.Dispose()}
[IO.File]::WriteAllText((Join-Path $output 'ObsEditorChecks.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net10.0-windows","framework":{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}}}')
& dotnet $dll $PreviewPath
if($LASTEXITCODE -ne 0){throw 'OBS editor checks failed'}
