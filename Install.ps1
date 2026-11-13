$ErrorActionPreference='Stop'
$destination=Join-Path $env:LOCALAPPDATA 'MiniPlayer\App'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -Path "$PSScriptRoot\App\*" -Destination $destination -Recurse -Force
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'mini payer.lnk'))
$shortcut.TargetPath=Join-Path $destination 'MiniPlayer.exe'
$shortcut.WorkingDirectory=$destination
$shortcut.Description='Мини-плеер: треки, обложка, закрепление поверх окон'
$shortcut.IconLocation=Join-Path $destination 'Player.ico'
$shortcut.Save()
Start-Process -FilePath $shortcut.TargetPath
