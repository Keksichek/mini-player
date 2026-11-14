param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $env:TEMP ('WidgetLauncher-Checks-' + [Guid]::NewGuid().ToString('N')) }
$repoRoot = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$runtimePath = Get-ChildItem 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App' -Directory | Where-Object Name -like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $runtimePath) { throw '.NET 10 runtime is required to run these development checks.' }
[Microsoft.CodeAnalysis.MetadataReference[]]$references = @(foreach ($file in Get-ChildItem -LiteralPath $runtimePath -Filter '*.dll') { try { [void][Reflection.AssemblyName]::GetAssemblyName($file.FullName) } catch { continue }; [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName) })
[Microsoft.CodeAnalysis.SyntaxTree[]]$trees = @(foreach ($file in @((Join-Path $repoRoot 'Launcher\Source\WidgetManager.cs'), (Join-Path $PSScriptRoot 'WidgetManagerChecks.cs'))) { [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file)) })
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication).WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('WidgetManagerChecks', $trees, $references, $options)
$dllPath = Join-Path $OutputDirectory 'WidgetManagerChecks.dll'
$output = [IO.File]::Create($dllPath)
try { $result = $compilation.Emit($output); if (-not $result.Success) { $result.Diagnostics | ForEach-Object ToString; throw 'Test compilation failed.' } } finally { $output.Dispose() }
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'WidgetManagerChecks.runtimeconfig.json'), '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}')
$scratch = Join-Path $OutputDirectory ('scratch-' + [Guid]::NewGuid().ToString('N'))
$linkTarget = Join-Path $scratch 'junction-target'
New-Item -ItemType Directory -Path $linkTarget -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $linkTarget 'keep.txt'), 'keep')
New-Item -ItemType Junction -Path (Join-Path $scratch 'junction-root') -Target $linkTarget | Out-Null
& dotnet $dllPath $scratch
if ($LASTEXITCODE -ne 0) { throw 'Widget engine checks failed.' }
