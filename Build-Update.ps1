param(
 [string]$PayloadDirectory=(Join-Path $PSScriptRoot '.build\Payload'),
 [string]$OutputDirectory=(Join-Path $PSScriptRoot 'dist\local-update'),
 [long]$BuildNumber=0,
 [string]$Version='0.19999999999999999999999999999999',
 [string]$Notes='Локальная тестовая сборка.',
 [string]$SigningKeyPath=(Join-Path $PSScriptRoot '.build\update-signing\private.xml'),
 [string]$PublicKeyPath=(Join-Path $PSScriptRoot 'Shared\UpdatePublicKey.xml'),
 [switch]$InitializeKeyOnly
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'Run this script with PowerShell 7.'}
$privateFile=[IO.Path]::GetFullPath($SigningKeyPath)
$publicFile=[IO.Path]::GetFullPath($PublicKeyPath)
$feedLocation=[IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\','/')
if($privateFile.StartsWith($feedLocation+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'The private signing key must be stored outside the update feed directory.'}
$utf8=[Text.UTF8Encoding]::new($false)
function NoLinks([string]$path){
 for($directory=[IO.Path]::GetFullPath($path);$directory;$directory=[IO.Path]::GetDirectoryName($directory)){
  if((Test-Path -LiteralPath $directory) -and ([IO.File]::GetAttributes($directory) -band [IO.FileAttributes]::ReparsePoint)){throw 'Symbolic links and junctions are not allowed in update sources.'}
 }
}
NoLinks $privateFile;NoLinks $publicFile
$rsa=[Security.Cryptography.RSA]::Create()
try{
 if(Test-Path -LiteralPath $privateFile){$rsa.FromXmlString([IO.File]::ReadAllText($privateFile))}
 elseif(Test-Path -LiteralPath $publicFile){throw 'The public update key exists but its private signing key is missing. Restore the original private key; this script will not silently rotate the trusted key.'}
 else{
  $rsa.KeySize=3072
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($privateFile))|Out-Null
  [IO.File]::WriteAllText($privateFile,$rsa.ToXmlString($true),$utf8)
 }
 if($rsa.KeySize -lt 2048){throw 'Update signing requires an RSA key of at least 2048 bits.'}
 $public=$rsa.ToXmlString($false)
 if(Test-Path -LiteralPath $publicFile){
  $existing=[Security.Cryptography.RSA]::Create()
  try{$existing.FromXmlString([IO.File]::ReadAllText($publicFile));if($existing.ToXmlString($false) -ne $public){throw 'The signing key does not match the trusted public update key.'}}finally{$existing.Dispose()}
 }else{[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($publicFile))|Out-Null;[IO.File]::WriteAllText($publicFile,$public,$utf8)}
 if($InitializeKeyOnly){Write-Output ('Public update key ready: '+$publicFile);return}
 if($BuildNumber -eq 0){$BuildNumber=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()}
 if($BuildNumber -le 0 -or !$Version -or $Version.Length -gt 128 -or $Version -match '[\x00-\x1F]' -or $Notes.Length -gt 8192){throw 'Invalid update build number, version or release notes.'}
 $payload=[IO.Path]::GetFullPath($PayloadDirectory).TrimEnd('\','/')
 $output=$feedLocation
 NoLinks $payload;NoLinks $output
 if(!$payload -or !(Test-Path -LiteralPath $payload -PathType Container)){throw 'Build the launcher payload before building an update package.'}
 if($output.Equals($payload,[StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($payload+'\',[StringComparison]::OrdinalIgnoreCase) -or $payload.StartsWith($output+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'The feed and payload directories must be separate.'}
 if($privateFile.StartsWith($payload+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'A private signing key must never be included in the payload.'}
 foreach($required in @('Launcher.exe','Launcher.dll','Launcher.runtimeconfig.json','Uninstall.exe','Runtime\dotnet.exe','Catalog\widgets.json','Packages\mini-player.zip')){
  if(!(Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)){throw ('Missing payload component: '+$required)}
 }
 foreach($forbidden in @('Data','Widgets','.git','.build')){if(Test-Path -LiteralPath (Join-Path $payload $forbidden)){throw ('A distributed payload must not include '+$forbidden)}}
 function CheckTree([string]$folder){
  NoLinks $folder
  foreach($file in Get-ChildItem -LiteralPath $folder -Force -File){NoLinks $file.FullName;if($file.Name -eq 'private.xml' -or $file.Extension -in '.key','.pfx','.p12'){throw 'Private signing material must not be included in the payload.'}}
  foreach($child in Get-ChildItem -LiteralPath $folder -Force -Directory){CheckTree $child.FullName}
 }
 CheckTree $payload
 [IO.Directory]::CreateDirectory($output)|Out-Null
 $stage=Join-Path $output ('.package-'+[Guid]::NewGuid().ToString('N'))
 [IO.Directory]::CreateDirectory($stage)|Out-Null
 try{
  $zip=Join-Path $stage 'payload.zip'
  [IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
  $size=(Get-Item -LiteralPath $zip).Length;if($size -le 0 -or $size -gt 2000000000){throw 'The update package exceeds its allowed size.'}
  $manifest=[ordered]@{ProductId='MiniPlayer.WidgetLauncher';Channel='secondary-local';BuildNumber=$BuildNumber;Version=$Version;Package='payload.zip';Sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant();Size=$size;Notes=$Notes}
  $bytes=$utf8.GetBytes(($manifest|ConvertTo-Json -Compress -Depth 4))
  [IO.File]::WriteAllBytes((Join-Path $stage 'update.json'),$bytes)
  $signature=$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
  [IO.File]::WriteAllText((Join-Path $stage 'update.sig'),[Convert]::ToBase64String($signature),$utf8)
  foreach($name in @('payload.zip','update.sig','update.json')){NoLinks (Join-Path $output $name);[IO.File]::Move((Join-Path $stage $name),(Join-Path $output $name),$true)}
  Write-Output ('Local signed update ready: '+$output)
  Write-Output ('Build number: '+$BuildNumber)
 }finally{
  # Only this script's three staging files are cleaned; unrelated files are preserved.
  foreach($name in @('payload.zip','update.sig','update.json')){$file=Join-Path $stage $name;if(Test-Path -LiteralPath $file){[IO.File]::Delete($file)}}
  if([IO.Directory]::Exists($stage) -and [IO.Directory]::GetFileSystemEntries($stage).Length -eq 0){[IO.Directory]::Delete($stage)}
 }
}finally{$rsa.Dispose()}
