"""Standalone entry point for the Notes widget."""
import ctypes
from ctypes import wintypes
from pathlib import Path
import sys
import tkinter as tk
from tkinter import messagebox

from notes.model import NotesStore
from notes.reminders import ReminderService
from notes.ui import NotesApp
from notes.windows import Tray, focus_existing


def work_area(root: tk.Tk) -> tuple[int, int, int, int]:
    """Return the usable area of the primary Windows display."""
    rect = wintypes.RECT()
    if ctypes.windll.user32.SystemParametersInfoW(0x0030, 0, ctypes.byref(rect), 0):
        return rect.left, rect.top, rect.right, rect.bottom
    return 0, 0, root.winfo_screenwidth(), root.winfo_screenheight()


def center_window(root: tk.Tk) -> None:
    left, top, right, bottom = work_area(root)
    metric = ctypes.windll.user32.GetSystemMetrics
    padding = metric(92)  # SM_CXPADDEDBORDER
    frame_width = 2 * (metric(32) + padding)
    frame_height = 2 * (metric(33) + padding) + metric(4)
    available_width, available_height = right - left, bottom - top
    width = max(1, min(430, available_width - frame_width - 16))
    height = max(1, min(650, available_height - frame_height - 16))
    x = left + max(0, (available_width - width - frame_width) // 2)
    y = top + max(0, (available_height - height - frame_height) // 2)
    root.minsize(min(385, width), min(420, height))
    root.geometry(f"{width}x{height}+{x}+{y}")


def main() -> None:
    if focus_existing():
        return
    root = tk.Tk()
    root.withdraw()
    root.title("Заметки")
    project = Path(__file__).resolve().parent
    icon = project / "notes.ico"
    root.iconbitmap(default=str(icon))
    store = NotesStore(project / "data" / "notes.json")
    reminders = ReminderService(root, store)
    hidden = "--tray" in sys.argv[1:]
    app = NotesApp(root, store, reminders, hidden=hidden)
    waiting_for_tray = hidden
    root.protocol("WM_DELETE_WINDOW", app.close)
    center_window(root)

    def show():
        nonlocal waiting_for_tray
        waiting_for_tray = False
        if tray is not None:
            tray.set_visible(False)
        root.deiconify()
        root.lift()
        root.focus_force()
        app.stickers.suspended = False
        app.stickers.sync(store.tasks)

    def hide():
        if tray is None or not tray.set_visible(True):
            show()
            messagebox.showwarning("Заметки", "Не удалось показать значок рядом с часами.",
                                   parent=root)
            return
        app.stickers.suspended = True
        app.stickers.sync(store.tasks)
        root.withdraw()

    def start_hidden(attempts=10):
        if not waiting_for_tray:
            return
        if tray is not None and tray.set_visible(True):
            hide()
        elif tray is not None and attempts:
            root.after(500, lambda: start_hidden(attempts - 1))
        else:
            hide()

    try:
        tray = Tray(root, icon, show, app.close)
    except OSError:
        tray = None
    app.hide_to_tray = hide
    try:
        if hidden:
            start_hidden()
        else:
            show()
        root.mainloop()
    finally:
        if tray is not None:
            tray.close()


if __name__ == "__main__":
    main()
