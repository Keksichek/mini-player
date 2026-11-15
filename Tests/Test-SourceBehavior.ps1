param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repoRoot ('.build\source-behavior-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
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
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@(foreach($file in @((Join-Path $repoRoot 'Source\SourceBehavior.cs'),(Join-Path $PSScriptRoot 'SourceBehaviorChecks.cs'))){[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file))})
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('SourceBehaviorChecks',$trees,$references,$options)
$dll=Join-Path $OutputDirectory 'SourceBehaviorChecks.dll';$stream=[IO.File]::Create($dll)
try{$result=$compilation.Emit($stream);if(!$result.Success){$result.Diagnostics | ForEach-Object ToString;throw 'Source behavior checks compilation failed.'}}finally{$stream.Dispose()}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SourceBehaviorChecks.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net10.0-windows","framework":{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}}}')
& dotnet $dll
if($LASTEXITCODE -ne 0){throw 'Source behavior checks failed.'}
