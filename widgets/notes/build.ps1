$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    python -m PyInstaller --noconfirm --clean --windowed --onedir --name Notes --icon notes.ico --add-data 'notes.ico;.' --add-data 'notes/reminder-default.wav;notes' --add-data 'notes/convert_audio.ps1;notes' app.py
    if ($LASTEXITCODE -ne 0) { throw 'PyInstaller build failed' }
} finally {
    Pop-Location
}
