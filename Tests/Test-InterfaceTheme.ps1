param([string]$PlayerDirectory,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$PlayerDirectory){$PlayerDirectory=Join-Path $repo '.build\MiniPlayerPackage\App'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('.build\InterfaceTheme-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
$output=[IO.Path]::GetFullPath($OutputDirectory);New-Item -ItemType Directory -Force -Path $output | Out-Null
Get-ChildItem -LiteralPath $PlayerDirectory | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $output -Recurse -Force}
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$dotnet=Split-Path (Get-Command dotnet).Source
$core=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.NETCore.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$desktop=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.WindowsDesktop.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
[Microsoft.CodeAnalysis.MetadataReference[]]$references=@(foreach($file in Get-ChildItem -LiteralPath $core.FullName,$desktop.FullName -Filter '*.dll'){
 try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)}catch{continue}
 [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)
})
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'InterfaceThemeChecks.cs'))))
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('InterfaceThemeChecks',$trees,$references,$options)
$dll=Join-Path $output 'InterfaceThemeChecks.dll';$stream=[IO.File]::Create($dll)
try{$result=$compilation.Emit($stream);if(!$result.Success){$result.Diagnostics|ForEach-Object ToString;throw 'Interface theme checks compilation failed.'}}finally{$stream.Dispose()}
[IO.File]::WriteAllText((Join-Path $output 'InterfaceThemeChecks.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net10.0-windows","framework":{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}}}')
& dotnet $dll (Join-Path $output 'Scratch') (Join-Path $repo '.build\Payload')
if($LASTEXITCODE -ne 0){throw 'Interface theme checks failed.'}
Write-Host ('Theme previews: '+(Join-Path $output 'Scratch'))
