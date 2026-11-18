param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('.build\LauncherCustomization-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$fixture=Join-Path $output ('Hub-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'Catalog'),(Join-Path $fixture 'Data'),(Join-Path $fixture 'Assets') | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'Launcher\catalog.json') -Destination (Join-Path $fixture 'Catalog\widgets.json')
Copy-Item -LiteralPath (Join-Path $repo 'Source\Fonts') -Destination $fixture -Recurse
if(Test-Path -LiteralPath (Join-Path $repo 'Launcher\Assets\Presets')){Copy-Item -LiteralPath (Join-Path $repo 'Launcher\Assets\Presets') -Destination (Join-Path $fixture 'Assets') -Recurse}
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$dotnet=Split-Path (Get-Command dotnet).Source
$core=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.NETCore.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$desktop=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.WindowsDesktop.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
[Microsoft.CodeAnalysis.MetadataReference[]]$references=@(foreach($file in Get-ChildItem -LiteralPath $core.FullName,$desktop.FullName -Filter '*.dll'){
 try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)}catch{continue}
 [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)
})
$files=@(Get-ChildItem (Join-Path $repo 'Launcher\Source') -Filter '*.cs' -File | ForEach-Object FullName)+(Join-Path $repo 'Shared\UpdateProtocol.cs')+(Join-Path $PSScriptRoot 'LauncherCustomizationChecks.cs')
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@(foreach($file in $files){[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file))})
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable).WithMainTypeName('LauncherCustomizationChecks')
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('LauncherCustomizationChecks',$trees,$references,$options)
$publicFile=Join-Path $repo 'Shared\UpdatePublicKey.xml'
[Microsoft.CodeAnalysis.ResourceDescription[]]$resources=@([Microsoft.CodeAnalysis.ResourceDescription]::new('UpdatePublicKey.xml',[Func[IO.Stream]]{[IO.File]::OpenRead($publicFile)}.GetNewClosure(),$true))
$dll=Join-Path $output 'LauncherCustomizationChecks.dll'
$stream=[IO.File]::Create($dll)
try{$result=$compilation.Emit($stream,$null,$null,$null,$resources);if(!$result.Success){$result.Diagnostics|ForEach-Object ToString;throw 'Launcher customization checks compilation failed'}}finally{$stream.Dispose()}
[IO.File]::WriteAllText((Join-Path $output 'LauncherCustomizationChecks.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net10.0-windows","framework":{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}}}')
& dotnet $dll $fixture (Join-Path $output 'Previews')
if($LASTEXITCODE -ne 0){throw 'Launcher customization checks failed'}
Write-Host ('Customization previews: '+(Join-Path $output 'Previews'))
