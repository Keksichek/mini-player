"""Reminder polling and Windows toast notifications."""
from __future__ import annotations

from base64 import b64encode
import json
import subprocess
import tkinter as tk

from notes.model import NotesStore, Task
from notes.sound import ReminderSound
from notes.sticky import INK, PALETTES


class ReminderService:
    def __init__(self, root: tk.Misc, store: NotesStore) -> None:
        self.root = root
        self.store = store
        self.running = True
        self.palette_for = lambda _: PALETTES[0]
        self.sound = ReminderSound(store.path.parent)
        self.root.after(0, self._poll)

    def stop(self) -> None:
        self.running = False

    def _poll(self) -> None:
        if not self.running:
            return
        self.root.after(15000, self._poll)
        pending = self.store.due()
        if pending:
            self.sound.play_alert()
        blocks = {entry.id: block for block in self.store.tasks
                  for entry in (block, *block.entries)}
        for task in pending:
            self.notify(task, blocks[task.id])

    def notify(self, task: Task, block: Task) -> None:
        self._show_popup(task, block)
        # Use PowerShell's built-in Windows Runtime toast API; all dynamic text is
        # JSON-encoded first, then passed as base64 to avoid quoting/injection issues.
        toast = {
            "title": "Напоминание",
            "body": task.text[:220],
        }
        encoded = b64encode(json.dumps(toast, ensure_ascii=False).encode("utf-8")).decode("ascii")
        ps = r'''
$data = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("%s")) | ConvertFrom-Json
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null
$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$xml.LoadXml("<toast><visual><binding template='ToastGeneric'><text>$([Security.SecurityElement]::Escape($data.title))</text><text>$([Security.SecurityElement]::Escape($data.body))</text></binding></visual><audio silent='true'/></toast>")
$notification = [Windows.UI.Notifications.ToastNotification]::new($xml)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier("Заметки").Show($notification)
''' % encoded
        try:
            subprocess.Popen(
                ["powershell.exe", "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", ps],
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
        except OSError:
            pass
    def _show_popup(self, task: Task, block: Task) -> tk.Toplevel:
        paper, strip, edge = self.palette_for(block.id)
        popup = tk.Toplevel(self.root)
        popup.withdraw()
        popup.title("Напоминание")
        popup.overrideredirect(True)
        popup.attributes("-toolwindow", True)
        popup.attributes("-topmost", True)
        popup.resizable(False, False)
        frame = tk.Frame(popup, bg=paper, highlightthickness=1, highlightbackground=edge)
        frame.pack(fill="both", expand=True)
        heading = f"Напоминание · {block.title}" if block.title else "Напоминание"
        tk.Label(frame, text=heading, bg=strip, fg=INK, anchor="w", wraplength=292,
                 justify="left", padx=14, pady=7, font=("Segoe UI", 10, "bold")).pack(fill="x")
        tk.Label(frame, text=task.text, bg=paper, fg=INK, font=("Segoe UI", 11),
                 wraplength=292, justify="left").pack(anchor="w", padx=14, pady=(9, 10))
        tk.Button(frame, text="Понятно", command=popup.destroy, bg=strip, fg=INK,
                  activebackground=edge, relief="flat", padx=10, cursor="hand2").pack(
                      anchor="e", padx=14, pady=(0, 10))
        popup.update_idletasks()
        width, height = popup.winfo_reqwidth(), popup.winfo_reqheight()
        x = popup.winfo_screenwidth() - width - 24
        y = popup.winfo_screenheight() - height - 60
        popup.geometry(f"+{x}+{y}")
        popup.deiconify()
        popup.after(20000, lambda: popup.destroy() if popup.winfo_exists() else None)
        return popup
