"""Remove this widget after its process exits."""
from base64 import b64encode
from pathlib import Path
import json
import os
import subprocess
import sys
import tempfile
from notes.windows import set_startup
from notes.install_paths import shortcuts

FILES = ("app.py", "launch.bat", "Запустить заметки.bat", "Заметки.lnk", "notes.ico",
         "README.md", ".gitignore", "build.ps1", "installer.py")
MODULES = ("__init__.py", "convert_audio.ps1", "date_picker.py", "drawing.py", "model.py",
           "reminder-default.wav", "reminders.py", "scrollbar.py", "sound.py", "sticky.py",
           "ui.py", "windows.py", "uninstall.py", "install_paths.py")


def targets(folder, frozen):
    folder = folder.resolve()
    if folder == Path(folder.anchor) or not folder.is_dir():
        raise ValueError("Не удалось определить папку приложения.")
    if frozen:
        exe = Path(sys.executable).resolve()
        if exe.parent != folder or not (folder / "_internal" / "notes.ico").is_file():
            raise ValueError("Папка EXE не похожа на установленный виджет.")
        paths = [exe, folder / "_internal", folder / "data", folder / "README.md",
                 folder / "installation.json"]
    else:
        if not (folder / "app.py").is_file() or not (folder / "notes" / "model.py").is_file():
            raise ValueError("Не удалось найти файлы виджета.")
        paths = [*(folder / name for name in FILES),
                 *(folder / "notes" / name for name in MODULES),
                 folder / "notes" / "__pycache__", folder / "__pycache__", folder / "data"]
    for path in paths:
        if not path.resolve().is_relative_to(folder):
            raise ValueError("Путь удаления выходит за пределы папки виджета.")
    return paths


def uninstall(folder):
    folder = folder.resolve()
    paths = targets(folder, getattr(sys, "frozen", False))
    links = []
    marker = folder / "installation.json"
    if marker.is_file():
        installed = json.loads(marker.read_text(encoding="utf-8"))
        allowed = {str(link) for link in shortcuts()}
        links = [path for path in installed.get("links", []) if path in allowed]
    payload = b64encode(json.dumps({"folder": str(folder), "paths": [str(p) for p in paths],
                                   "links": links},
                                  ensure_ascii=False).encode("utf-8")).decode("ascii")
    helper = r"""
$ErrorActionPreference = 'Stop'
$data = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__PAYLOAD__')) | ConvertFrom-Json
$folder = [IO.Path]::GetFullPath($data.folder).TrimEnd('\')
$prefix = $folder + '\'
$paths = @($data.paths | ForEach-Object { [IO.Path]::GetFullPath($_) })
foreach ($path in $paths) {
    if (!$path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { exit 2 }
}
if (Get-Process -Id __PID__ -ErrorAction SilentlyContinue) {
    Wait-Process -Id __PID__ -Timeout 30 -ErrorAction SilentlyContinue
}
if (Get-Process -Id __PID__ -ErrorAction SilentlyContinue) { exit 3 }
foreach ($link in $data.links) {
    if (Test-Path -LiteralPath $link -PathType Leaf) {
        Remove-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
    }
}
$success = $false
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    if (@($paths | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0) {
        $success = $true
        break
    }
    Start-Sleep -Milliseconds 400
}
$directory = Join-Path $folder 'notes'
if ([IO.Path]::GetFullPath($directory).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    if ((Test-Path -LiteralPath $directory) -and
        @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $directory -Force
    }
}
if (!$success) {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('Не все файлы удалось удалить. Закройте другие копии Заметок и удалите оставшиеся файлы вручную.', 'Заметки') | Out-Null
}
""".replace("__PAYLOAD__", payload).replace("__PID__", str(os.getpid()))
    encoded = b64encode(helper.encode("utf-16-le")).decode("ascii")
    set_startup("off", folder / "app.py")
    subprocess.Popen(
        ["powershell.exe", "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden",
         "-EncodedCommand", encoded],
        cwd=tempfile.gettempdir(), creationflags=subprocess.CREATE_NO_WINDOW,
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
    )
