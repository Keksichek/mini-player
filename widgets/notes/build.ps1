$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    python -m PyInstaller --noconfirm --clean --windowed --onedir --name Notes --icon notes.ico --add-data 'notes.ico;.' --add-data 'notes/reminder-default.wav;notes' --add-data 'notes/convert_audio.ps1;notes' app.py
    if ($LASTEXITCODE -ne 0) { throw 'PyInstaller build failed' }
    Copy-Item README.md dist/Notes/README.md -Force
    if (Test-Path dist/Notes/data) { throw 'Remove personal data from the build output before packaging.' }
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'dist/Notes/hub-widget.json'),'{"Id":"notes","Protocol":1,"Version":"0.1.5"}')
    Compress-Archive -Path dist/Notes/* -DestinationPath dist/notes.zip -Force
    python -m PyInstaller --noconfirm --clean --windowed --onefile --name Notes-Setup --icon notes.ico --add-data 'dist/Notes;payload' installer.py
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
} finally {
    Pop-Location
}
