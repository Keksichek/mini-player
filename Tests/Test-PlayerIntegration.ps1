param([string]$OutputDirectory,[string]$PlayerDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $PlayerDirectory) { $PlayerDirectory = Join-Path $repoRoot '.build\MiniPlayerPackage\App' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $env:TEMP 'WidgetLauncher-PlayerChecks' }
$scratch = Join-Path $OutputDirectory ('p-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$appDirectory = Join-Path $scratch 'Widgets\mini-player\App'
New-Item -ItemType Directory -Force -Path $appDirectory | Out-Null
Get-ChildItem -LiteralPath $PlayerDirectory | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $appDirectory -Recurse }
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$runtimePath = Get-ChildItem 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App' -Directory | Where-Object Name -like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
[Microsoft.CodeAnalysis.MetadataReference[]]$references = @(foreach ($file in Get-ChildItem -LiteralPath $runtimePath -Filter '*.dll') { try { [void][Reflection.AssemblyName]::GetAssemblyName($file.FullName) } catch { continue }; [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName) })
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees = @([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PlayerIntegrationChecks.cs'))))
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('PlayerIntegrationChecks',$trees,$references,$options)
$dllPath = Join-Path $appDirectory 'PlayerIntegrationChecks.dll'
$output = [IO.File]::Create($dllPath)
try { $result = $compilation.Emit($output); if (-not $result.Success) { $result.Diagnostics | ForEach-Object ToString; throw 'Player integration check compilation failed.' } } finally { $output.Dispose() }
[IO.File]::WriteAllText((Join-Path $appDirectory 'PlayerIntegrationChecks.runtimeconfig.json'), '{"runtimeOptions":{"tfm":"net10.0-windows","framework":{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}}}')
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /platform:x64 /utf8output ('/out:' + (Join-Path $scratch 'Launcher.exe')) (Join-Path $PSScriptRoot 'LauncherRequestRecorder.cs')
if ($LASTEXITCODE -ne 0) { throw 'Request recorder compilation failed.' }
$target = Join-Path $scratch 'outside-target'
New-Item -ItemType Directory -Path $target | Out-Null
[IO.File]::WriteAllText((Join-Path $target 'keep.txt'),'keep')
New-Item -ItemType Junction -Path (Join-Path $scratch 'data-junction-fixture') -Target $target | Out-Null
& dotnet $dllPath $scratch
if ($LASTEXITCODE -ne 0) { throw 'Player integration checks failed.' }
