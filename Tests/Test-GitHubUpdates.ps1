param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo '.build\GitHubUpdatesChecks'}
$output=[IO.Path]::GetFullPath($OutputDirectory);New-Item -ItemType Directory -Force -Path $output | Out-Null
$source=Join-Path $output 'Probe.cs';$probe=Join-Path $output 'Probe.exe';[IO.File]::WriteAllText($source,'using System;using System.IO;using System.Threading;static class Probe{static int Main(){File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launches.txt"),"launch\n");Thread.Sleep(300);return 1;}}')
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /platform:x64 ('/out:'+$probe) $source
if($LASTEXITCODE -ne 0){throw 'Probe compilation failed'}
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll');Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$dotnet=Split-Path (Get-Command dotnet).Source
$core=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.NETCore.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$desktop=Get-ChildItem (Join-Path $dotnet 'shared\Microsoft.WindowsDesktop.App') -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
[Microsoft.CodeAnalysis.MetadataReference[]]$references=@(foreach($file in Get-ChildItem -LiteralPath $core.FullName,$desktop.FullName -Filter '*.dll'){try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)}catch{continue};[Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)})
$files=@(Get-ChildItem (Join-Path $repo 'Launcher\Source') -Filter '*.cs' -File | ForEach-Object FullName)+(Join-Path $repo 'Shared\UpdateProtocol.cs')+(Join-Path $PSScriptRoot 'GitHubUpdateChecks.cs')
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@(foreach($file in $files){[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file))})
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable).WithMainTypeName('GitHubUpdateChecks')
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('GitHubUpdateChecks',$trees,$references,$options)
# Fixture keys are generated for this test run; no release signing key is required.
$testKey=Join-Path $output 'fixture-private.xml'
$publicFile=Join-Path $output 'fixture-public.xml'
$rsa=[Security.Cryptography.RSA]::Create(2048)
try{[IO.File]::WriteAllText($testKey,$rsa.ToXmlString($true));[IO.File]::WriteAllText($publicFile,$rsa.ToXmlString($false))}finally{$rsa.Dispose()}[Microsoft.CodeAnalysis.ResourceDescription[]]$resources=@([Microsoft.CodeAnalysis.ResourceDescription]::new('UpdatePublicKey.xml',[Func[IO.Stream]]{[IO.File]::OpenRead($publicFile)}.GetNewClosure(),$true))
$dll=Join-Path $output 'GitHubUpdateChecks.dll';$stream=[IO.File]::Create($dll);try{$result=$compilation.Emit($stream,$null,$null,$null,$resources);if(!$result.Success){$result.Diagnostics|ForEach-Object ToString;throw 'GitHub checks compilation failed'}}finally{$stream.Dispose()}
[IO.File]::WriteAllText((Join-Path $output 'GitHubUpdateChecks.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net10.0-windows","framework":{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}}}')
& dotnet $dll (Join-Path $output ('Fixture-'+[Guid]::NewGuid().ToString('N').Substring(0,8))) $testKey $probe
if($LASTEXITCODE -ne 0){throw 'GitHub update checks failed'}
