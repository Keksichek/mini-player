param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('.build\UpdateProtocolChecks-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
$output=[IO.Path]::GetFullPath($OutputDirectory);New-Item -ItemType Directory -Force -Path $output|Out-Null
$private=Join-Path $output 'private.xml';$public=Join-Path $output 'public.xml';$rsa=[Security.Cryptography.RSA]::Create(2048)
try{[IO.File]::WriteAllText($private,$rsa.ToXmlString($true));[IO.File]::WriteAllText($public,$rsa.ToXmlString($false))}finally{$rsa.Dispose()}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $output 'UpdateProtocolChecks.exe'
& $compiler /nologo /target:exe /platform:x64 /utf8output ('/out:'+$exe) /r:System.Runtime.Serialization.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ('/resource:'+$public+',UpdatePublicKey.xml') (Join-Path $repo 'Shared\UpdateProtocol.cs') (Join-Path $PSScriptRoot 'UpdateProtocolChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Update protocol checks compilation failed.'}
& $exe (Join-Path $output 'Fixtures') $private
if($LASTEXITCODE -ne 0){throw 'Update protocol checks failed.'}
Write-Output ('Fixtures: '+$output)
