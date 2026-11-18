$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime

$storageFile = [Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime]
$properties = [Windows.Storage.FileProperties.MusicProperties, Windows.Storage, ContentType=WindowsRuntime]
$profiles = [Windows.Media.MediaProperties.MediaEncodingProfile, Windows.Media, ContentType=WindowsRuntime]
$quality = [Windows.Media.MediaProperties.AudioEncodingQuality, Windows.Media, ContentType=WindowsRuntime]
$preparedType = [Windows.Media.Transcoding.PrepareTranscodeResult, Windows.Media, ContentType=WindowsRuntime]
$transcoderType = [Windows.Media.Transcoding.MediaTranscoder, Windows.Media, ContentType=WindowsRuntime]

$methods = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.IsGenericMethodDefinition
}
$operation = $methods | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' }
$progress = $methods | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncActionWithProgress`1' }

function Await-Result($async, $resultType) {
    $task = $operation.MakeGenericMethod($resultType).Invoke($null, @($async))
    return $task.GetAwaiter().GetResult()
}

$source = Await-Result ($storageFile::GetFileFromPathAsync($env:NOTES_AUDIO_SOURCE)) $storageFile
$destination = Await-Result ($storageFile::GetFileFromPathAsync($env:NOTES_AUDIO_DESTINATION)) $storageFile
$music = Await-Result ($source.Properties.GetMusicPropertiesAsync()) $properties
if ($music.Duration.TotalSeconds -gt 30) {
    Write-Output 'TOO_LONG'
    exit 2
}

$transcoder = $transcoderType::new()
$profile = $profiles::CreateWav($quality::High)
$prepared = Await-Result ($transcoder.PrepareFileTranscodeAsync($source, $destination, $profile)) $preparedType
if (!$prepared.CanTranscode) { throw "Audio conversion unavailable: $($prepared.FailureReason)" }
$task = $progress.MakeGenericMethod([double]).Invoke($null, @($prepared.TranscodeAsync()))
$task.GetAwaiter().GetResult() | Out-Null
