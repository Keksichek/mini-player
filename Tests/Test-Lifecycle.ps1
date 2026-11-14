param([Parameter(Mandatory=$true)][string]$InstallDirectory,[switch]$KeepWidget)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($InstallDirectory)
$marker=Get-Content -LiteralPath (Join-Path $root 'launcher-install.json') -Raw | ConvertFrom-Json
if(!$marker.TestInstallation){throw 'Only test installations are allowed.'}
$projectRoot=Split-Path $PSScriptRoot -Parent
$output=Join-Path $projectRoot '.build\lifecycle-checks'
New-Item -ItemType Directory -Force -Path $output | Out-Null
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$runtime=Join-Path $root 'Runtime\shared\Microsoft.NETCore.App\10.0.15'
[Microsoft.CodeAnalysis.MetadataReference[]]$references=@(foreach($file in Get-ChildItem -LiteralPath $runtime -Filter '*.dll'){
 try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)}catch{continue}
 [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)
})
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@(foreach($file in @((Join-Path $PSScriptRoot 'LifecycleChecks.cs'),(Join-Path $projectRoot 'Launcher\Source\WidgetManager.cs'))){[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file))})
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('LifecycleChecks',$trees,$references,$options)
$dll=Join-Path $output 'LifecycleChecks.dll'
$stream=[IO.File]::Create($dll)
try{$result=$compilation.Emit($stream);if(!$result.Success){$result.Diagnostics | ForEach-Object ToString;throw 'Lifecycle checks compilation failed.'}}finally{$stream.Dispose()}
[IO.File]::WriteAllText((Join-Path $output 'LifecycleChecks.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}')
$testArguments=@($dll,$root)
if($KeepWidget){$testArguments+='--keep-installed'}
& (Join-Path $root 'Runtime\dotnet.exe') @testArguments
if($LASTEXITCODE -ne 0){throw 'Lifecycle checks failed.'}
