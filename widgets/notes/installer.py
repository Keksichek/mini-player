"""Per-user Windows installer for the packaged Notes application."""
from base64 import b64encode
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tkinter as tk
from tkinter import messagebox
from notes.install_paths import shortcuts

VERSION = "0.1.2"


def install_to(payload, target, links):
    target = target.resolve()
    if target.exists() and any(target.iterdir()) and not (target / "Notes.exe").is_file():
        raise ValueError("Выбранная папка содержит другие файлы.")
    target.mkdir(parents=True, exist_ok=True)
    for name in ("Notes.exe", "_internal", "README.md"):
        source = payload / name
        destination = target / name
        if source.is_dir():
            shutil.copytree(source, destination, dirs_exist_ok=True)
        elif source.is_file():
            shutil.copyfile(source, destination)
    for link in links:
        link.parent.mkdir(parents=True, exist_ok=True)
    script = r"""
$ErrorActionPreference = 'Stop'
$data = $env:NOTES_INSTALL_DATA | ConvertFrom-Json
$shell = New-Object -ComObject WScript.Shell
foreach ($path in $data.links) {
    $shortcut = $shell.CreateShortcut($path)
    $shortcut.TargetPath = Join-Path $data.target 'Notes.exe'
    $shortcut.WorkingDirectory = $data.target
    $shortcut.IconLocation = (Join-Path $data.target 'Notes.exe') + ',0'
    $shortcut.Description = 'Заметки'
    $shortcut.Save()
}
"""
    env = {**os.environ, "NOTES_INSTALL_DATA": json.dumps(
        {"target": str(target), "links": [str(link) for link in links]}, ensure_ascii=False)}
    command = b64encode(script.encode("utf-16-le")).decode("ascii")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", command],
                            env=env, creationflags=subprocess.CREATE_NO_WINDOW,
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
    if result.returncode:
        raise OSError("Не удалось создать ярлыки.")
    (target / "installation.json").write_text(json.dumps({"version": VERSION, "links": [
        str(link) for link in links]}, ensure_ascii=False), encoding="utf-8")


def main():
    base = Path(__file__).resolve().parent
    payload = base / "payload"
    root = tk.Tk()
    root.title("Установка Заметок")
    root.resizable(False, False)
    root.configure(bg="#FFF9ED")
    root.iconbitmap(str(payload / "_internal" / "notes.ico"))
    target = Path(os.environ["LOCALAPPDATA"]) / "Programs" / "Notes"
    panel = tk.Frame(root, padx=24, pady=22, bg="#FFF9ED")
    panel.pack()
    tk.Label(panel, text="Заметки", font=("Segoe UI", 22, "bold"),
             bg="#FFF9ED", fg="#302923").pack(anchor="w")
    tk.Label(panel, text="Установка для текущего пользователя", bg="#FFF9ED",
             fg="#625247").pack(anchor="w", pady=(4, 10))
    tk.Label(panel, text=str(target), wraplength=360, justify="left",
             bg="#FFF9ED", fg="#302923").pack(anchor="w")
    tk.Label(panel, text="Ярлыки появятся на рабочем столе и в меню «Пуск».",
             wraplength=360, justify="left", bg="#FFF9ED", fg="#625247").pack(anchor="w", pady=10)
    status = tk.Label(panel, text="", bg="#FFF9ED", fg="#302923")
    status.pack(anchor="w")

    def perform():
        button.configure(state="disabled")
        status.configure(text="Установка…")
        root.update_idletasks()
        try:
            install_to(payload, target, shortcuts())
        except (OSError, ValueError, subprocess.TimeoutExpired) as error:
            messagebox.showerror("Установка", "Закройте запущенные Заметки и повторите установку.\n" + str(error), parent=root)
            button.configure(state="normal")
            status.configure(text="")
            return
        status.configure(text="Установка завершена")
        button.configure(text="Открыть Заметки", state="normal", command=launch)

    def launch():
        subprocess.Popen([str(target / "Notes.exe")], cwd=target)
        root.destroy()

    button = tk.Button(panel, text="Установить", command=perform, bg="#925039", fg="white",
                       disabledforeground="white", activebackground="#713B28",
                       activeforeground="white", bd=0, padx=18, pady=8)
    button.pack(anchor="e", pady=(10, 0))
    root.update_idletasks()
    width, height = root.winfo_reqwidth(), root.winfo_reqheight()
    root.geometry(f"+{max(0,(root.winfo_screenwidth()-width)//2)}+{max(0,(root.winfo_screenheight()-height)//2)}")
    root.mainloop()


if __name__ == "__main__":
    main()
