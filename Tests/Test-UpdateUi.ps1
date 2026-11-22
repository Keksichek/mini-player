param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo '.build\UpdateUiChecks'}
$output=[IO.Path]::GetFullPath($OutputDirectory);New-Item -ItemType Directory -Force -Path $output | Out-Null
$probeSource=Join-Path $output 'UpdateProbe.cs';$probe=Join-Path $output 'UpdateProbe.exe'
[IO.File]::WriteAllText($probeSource,'using System;using System.IO;using System.Threading;static class Probe{static int Main(){File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launches.txt"),"launch\n");Thread.Sleep(650);return 1;}}')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 ('/out:'+$probe) $probeSource
if($LASTEXITCODE -ne 0){throw 'Update probe compilation failed.'}
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll');Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$dotnet=Split-Path (Get-Command dotnet).Source
$core=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.NETCore.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$desktop=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.WindowsDesktop.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
[Microsoft.CodeAnalysis.MetadataReference[]]$references=@(foreach($file in Get-ChildItem -LiteralPath $core.FullName,$desktop.FullName -Filter '*.dll'){
 try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)}catch{continue}
 [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)
})
$private=Join-Path $output 'private.xml';$public=Join-Path $output 'public.xml';$testKey=[Security.Cryptography.RSA]::Create(2048)
try{[IO.File]::WriteAllText($private,$testKey.ToXmlString($true));[IO.File]::WriteAllText($public,$testKey.ToXmlString($false))}finally{$testKey.Dispose()}
# Compile the actual launcher against an isolated test public key; production keys stay untouched.
[Microsoft.CodeAnalysis.SyntaxTree[]]$appTrees=@(foreach($file in @((Get-ChildItem (Join-Path $repo 'Launcher\Source') -Filter '*.cs'|ForEach-Object FullName))+(Join-Path $repo 'Shared\UpdateProtocol.cs')){[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file))})
$appOptions=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::WindowsApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$app=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('Launcher',$appTrees,$references,$appOptions)
$provider=[Func[IO.Stream]]{[IO.File]::OpenRead($public)}.GetNewClosure();[Microsoft.CodeAnalysis.ResourceDescription[]]$resources=@([Microsoft.CodeAnalysis.ResourceDescription]::new('UpdatePublicKey.xml',$provider,$true))
$appPath=Join-Path $output 'Launcher.dll';$appStream=[IO.File]::Create($appPath);try{$appResult=$app.Emit($appStream,$null,$null,$null,$resources);if(!$appResult.Success){$appResult.Diagnostics|ForEach-Object ToString;throw 'Test launcher compilation failed'}}finally{$appStream.Dispose()}
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'UpdateUiChecks.cs'))))
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('UpdateUiChecks',$trees,$references,$options)
$dll=Join-Path $output 'UpdateUiChecks.dll';$stream=[IO.File]::Create($dll)
try{$result=$compilation.Emit($stream);if(!$result.Success){$result.Diagnostics|ForEach-Object ToString;throw 'Update UI checks compilation failed.'}}finally{$stream.Dispose()}
[IO.File]::WriteAllText((Join-Path $output 'UpdateUiChecks.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net10.0-windows","framework":{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}}}')
& dotnet $dll (Join-Path $output ('Scratch-'+[Guid]::NewGuid().ToString('N').Substring(0,8))) $appPath $private $probe
if($LASTEXITCODE -ne 0){throw 'Update UI checks failed.'}
