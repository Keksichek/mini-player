param([string]$Installer)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$Installer){$Installer=Join-Path $repo 'dist\WidgetLauncher-Setup.exe'}
$output=Join-Path $repo ('.build\HubBackupChecks-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$dotnet=Split-Path (Get-Command dotnet).Source
$frameworks=foreach($name in 'Microsoft.NETCore.App','Microsoft.WindowsDesktop.App'){
 Get-ChildItem (Join-Path $dotnet ('shared\'+$name)) -Directory | Where-Object Name -like '10.*' | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
}
[Microsoft.CodeAnalysis.MetadataReference[]]$references=@(foreach($file in Get-ChildItem -LiteralPath $frameworks.FullName -Filter '*.dll'){
 try{[void][Reflection.AssemblyName]::GetAssemblyName($file.FullName)}catch{continue}
 [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)
})
$files=@(Get-ChildItem (Join-Path $repo 'Launcher\Source') -Filter '*.cs' -File | ForEach-Object FullName)+(Join-Path $repo 'Shared\UpdateProtocol.cs')+(Join-Path $PSScriptRoot 'HubBackupChecks.cs')
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees=@(foreach($file in $files){[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file))})
$options=[Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable).WithMainTypeName('HubBackupChecks')
$compilation=[Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('HubBackupChecks',$trees,$references,$options)
$dll=Join-Path $output 'HubBackupChecks.dll'
$stream=[IO.File]::Create($dll)
try{$result=$compilation.Emit($stream);if(!$result.Success){$result.Diagnostics|ForEach-Object ToString;throw 'Backup checks compilation failed.'}}finally{$stream.Dispose()}
[IO.File]::WriteAllText((Join-Path $output 'HubBackupChecks.runtimeconfig.json'),'{"runtimeOptions":{"tfm":"net10.0-windows","framework":{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}}}')

function RunInstaller([string]$exe,[string]$mode,[string]$directory,[string]$report) {
 $info=[Diagnostics.ProcessStartInfo]::new($exe)
 $info.UseShellExecute=$false
 $info.CreateNoWindow=$true
 $info.WindowStyle='Hidden'
 foreach($argument in @($mode,$directory,'--report',$report)){[void]$info.ArgumentList.Add($argument)}
 $process=[Diagnostics.Process]::Start($info)
 try{if(!$process.WaitForExit(90000) -or $process.ExitCode -ne 0){throw "Test installer failed. See $report"}}finally{$process.Dispose()}
}
$roots=@()
try{
 foreach($name in 'Source','Target'){
  $parent=Join-Path $output $name
  RunInstaller $Installer '--test-install' $parent (Join-Path $output "$name-install.log")
  $root=Join-Path $parent 'VibeWidgetsHub alpha'
  $marker=Get-Content (Join-Path $root 'launcher-install.json') -Raw|ConvertFrom-Json
  if(!$marker.TestInstallation -or $marker.InstallPath -ine $root){throw 'Isolated installation required.'}
  $roots+=$root
 }
 & dotnet $dll $output $roots[0] $roots[1]
 if($LASTEXITCODE -ne 0){throw 'Backup checks failed.'}
}finally{
 foreach($root in $roots){
  $parent=Split-Path $root -Parent
  $cleanup=Join-Path $parent 'Uninstall.exe'
  Copy-Item -LiteralPath (Join-Path $root 'Uninstall.exe') -Destination $cleanup
  RunInstaller $cleanup '--test-uninstall' $root (Join-Path $parent 'cleanup.log')
 }
}
