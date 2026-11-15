param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('.build\UpdatePackageChecks-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
$root=[IO.Path]::GetFullPath($OutputDirectory);$payload=Join-Path $root 'Payload';$feed=Join-Path $root 'Feed';$private=Join-Path $root 'Signing\private.xml';$public=Join-Path $root 'Signing\public.xml';$script:checks=0
function Check([bool]$condition,[string]$message){if(!$condition){throw $message};$script:checks++;Write-Output ('PASS '+$message)}
function Pack([long]$build=10,[string]$out=$feed){& (Join-Path $repo 'Build-Update.ps1') -PayloadDirectory $payload -OutputDirectory $out -BuildNumber $build -Notes 'Собственная тестовая сборка' -SigningKeyPath $private -PublicKeyPath $public | Out-Null}
function Reject([scriptblock]$action,[string]$message){$rejected=$false;try{& $action}catch{$rejected=$true};Check $rejected $message}
foreach($relative in @('Launcher.exe','Launcher.dll','Launcher.runtimeconfig.json','Uninstall.exe','Runtime\dotnet.exe','Catalog\widgets.json','Packages\mini-player.zip')){
 $path=Join-Path $payload $relative;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))|Out-Null;[IO.File]::WriteAllText($path,'owned fixture')
}
Pack
Check (([IO.Directory]::GetFiles($feed)|ForEach-Object{[IO.Path]::GetFileName($_)}|Sort-Object) -join ',' -eq 'payload.zip,update.json,update.sig') 'Feed includes only its ZIP, manifest and detached signature'
$manifestFile=Join-Path $feed 'update.json';$manifest=Get-Content -LiteralPath $manifestFile -Raw|ConvertFrom-Json
Check ($manifest.BuildNumber -eq 10 -and $manifest.Channel -eq 'secondary-local' -and $manifest.Version -eq '0.19999999999999999999999999999999') 'Build number is independent of the long display version'
$rsa=[Security.Cryptography.RSA]::Create()
try{$rsa.FromXmlString([IO.File]::ReadAllText($public));Check ($rsa.VerifyData([IO.File]::ReadAllBytes($manifestFile),[Convert]::FromBase64String([IO.File]::ReadAllText((Join-Path $feed 'update.sig'))),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)) 'Packager signs the exact manifest bytes'}finally{$rsa.Dispose()}
Check ((Get-FileHash -LiteralPath (Join-Path $feed 'payload.zip')).Hash -eq $manifest.Sha256 -and (Get-Item -LiteralPath (Join-Path $feed 'payload.zip')).Length -eq $manifest.Size) 'Signed package digest and size match the produced ZIP'
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $feed 'payload.zip'))
try{Check ($zip.Entries.Count -eq 7 -and !($zip.Entries.FullName -match '(?i)private\.xml|\.pfx|\.key|update-signing')) 'Private signing material never enters the distributed package'}finally{$zip.Dispose()}
$privateBytes=[IO.File]::ReadAllBytes($private);$publicBytes=[IO.File]::ReadAllBytes($public);$manifestHash=(Get-FileHash -LiteralPath $manifestFile).Hash
[IO.File]::Delete($private)
try{Reject {Pack 11} 'A missing private key cannot silently rotate an existing public key';Check ([Convert]::ToBase64String([IO.File]::ReadAllBytes($public)) -eq [Convert]::ToBase64String($publicBytes)) 'Missing-key failure leaves the trusted public key unchanged'}finally{[IO.File]::WriteAllBytes($private,$privateBytes)}
$other=[Security.Cryptography.RSA]::Create(2048)
try{[IO.File]::WriteAllText($private,$other.ToXmlString($true));Reject {Pack 11} 'A mismatched private key is rejected'}finally{$other.Dispose();[IO.File]::WriteAllBytes($private,$privateBytes)}
$forbidden=Join-Path $payload 'private.xml';[IO.File]::WriteAllText($forbidden,'own fixture signing marker')
try{Reject {Pack 11} 'A payload containing a private key filename is rejected'}finally{[IO.File]::Delete($forbidden)}
$data=Join-Path $payload 'Data';[IO.Directory]::CreateDirectory($data)|Out-Null
try{Reject {Pack 11} 'A payload cannot distribute user Data'}finally{[IO.Directory]::Delete($data)}
Reject {Pack 11 (Join-Path $payload 'nested-feed')} 'A feed cannot be created inside its own payload'
Reject {Pack 11 ([IO.Path]::GetDirectoryName($private))} 'A feed cannot contain the private signing key directory'
Check ((Get-FileHash -LiteralPath $manifestFile).Hash -eq $manifestHash) 'Rejected builds preserve the previously valid feed'
[IO.File]::WriteAllText((Join-Path $feed 'keep.txt'),'preserve this unrelated file');Pack 11
Check ((Get-Content -LiteralPath (Join-Path $feed 'keep.txt') -Raw) -eq 'preserve this unrelated file' -and (Get-Content -LiteralPath $manifestFile -Raw|ConvertFrom-Json).BuildNumber -eq 11) 'A new feed build preserves unrelated files in its output folder'
Write-Output ('TOTAL '+$script:checks+' PASS')
