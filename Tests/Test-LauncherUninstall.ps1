param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repoRoot '.build\launcher-uninstall-checks'}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$dotnetRoot=Split-Path (Get-Command dotnet).Source
$core=Get-ChildItem (Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$desktop=Get-ChildItem (Join-Path $dotnetRoot 'shared\Microsoft.WindowsDesktop.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
[Microsoft.CodeAnalysis.MetadataReference[]]$references=@(foreach($file in Get-ChildItem -LiteralPath $core.FullName,$desktop.FullName -Filter '*.dll'){
 try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)}catch{continue}
 [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)
})
$sources=@(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Launcher\Source') -Filter '*.cs' -File | ForEach-Object FullName)+(Join-Path $repoRoot 'Shared\UpdateProtocol.cs')+(Join-Path $PSScriptRoot 'LauncherUninstallChecks.cs')
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@(foreach($file in $sources){[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file))})
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable).WithMainTypeName('LauncherUninstallChecks')
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('LauncherUninstallChecks',$trees,$references,$options)
$dll=Join-Path $OutputDirectory 'LauncherUninstallChecks.dll';$stream=[IO.File]::Create($dll)
try{$result=$compilation.Emit($stream);if(!$result.Success){$result.Diagnostics|ForEach-Object ToString;throw 'Launcher checks compilation failed.'}}finally{$stream.Dispose()}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'LauncherUninstallChecks.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net10.0-windows","framework":{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}}}')
& dotnet $dll (Join-Path $OutputDirectory 'fixture')
if($LASTEXITCODE -ne 0){throw 'Launcher uninstall checks failed.'}
