"""Optional VibeWidgetsHub context and its Windows lifecycle protocol."""
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
from pathlib import Path
import time


def context(arguments, executable_folder):
    root = data = None
    for flag in ("--launcher-root", "--data-dir"):
        if flag in arguments:
            index = arguments.index(flag)
            if index + 1 == len(arguments) or arguments[index + 1].startswith("--"):
                raise ValueError(f"Не указано значение {flag}")
            if flag == "--launcher-root":
                root = Path(arguments[index + 1]).absolute()
            else:
                data = Path(arguments[index + 1]).absolute()
    folder = Path(executable_folder).absolute()
    # Opening the installed EXE directly must use the same data and identity.
    if root is None and folder.name == "notes" and folder.parent.name == "Widgets":
        candidate = folder.parent.parent
        if (candidate / "launcher-install.json").is_file():
            root = candidate
    if root is None:
        if data is not None:
            raise ValueError("Папку данных задаёт лаунчер.")
        return None, folder / "data"
    expected = root / "Data" / "notes"
    if folder != root / "Widgets" / "notes" or (data is not None and data != expected):
        raise ValueError("Папка заметок не соответствует папке лаунчера.")
    for path in (folder, expected):
        for part in (path, *path.parents):
            if part.exists() and part.lstat().st_file_attributes & 0x400:
                raise ValueError("Папки виджета не должны быть ссылками.")
    marker = json.loads((root / "launcher-install.json").read_text(encoding="utf-8-sig"))
    if marker.get("ProductId") != "MiniPlayer.WidgetLauncher":
        raise ValueError("Не удалось проверить лаунчер.")
    return root, expected


def instance_name(data):
    key = str(Path(data).absolute()).rstrip("\\/").upper()
    return "Local\\MiniPlayer." + hashlib.sha256(key.encode("utf-8")).hexdigest()[:16].upper()


class HubConnection:
    def __init__(self, data):
        self.name = instance_name(data)
        self.kernel = c.WinDLL("kernel32", use_last_error=True)
        signatures = {
            "CreateMutexW": (w.HANDLE, (w.LPVOID, w.BOOL, w.LPCWSTR)),
            "CreateEventW": (w.HANDLE, (w.LPVOID, w.BOOL, w.BOOL, w.LPCWSTR)),
            "OpenEventW": (w.HANDLE, (w.DWORD, w.BOOL, w.LPCWSTR)),
            "WaitForSingleObject": (w.DWORD, (w.HANDLE, w.DWORD)),
            "SetEvent": (w.BOOL, (w.HANDLE,)),
            "CloseHandle": (w.BOOL, (w.HANDLE,)),
            "ReleaseMutex": (w.BOOL, (w.HANDLE,)),
        }
        for name, (result, arguments) in signatures.items():
            function = getattr(self.kernel, name)
            function.restype, function.argtypes = result, arguments
        self.events = {}
        self.owned = False
        self.mutex = self.kernel.CreateMutexW(None, False, self.name)
        if not self.mutex:
            raise c.WinError(c.get_last_error())
        status = self.kernel.WaitForSingleObject(self.mutex, 0)
        if status not in (0, 128, 258):
            self.close()
            raise c.WinError(c.get_last_error())
        self.owned = status in (0, 128)

    def show_existing(self):
        for _ in range(100):
            event = self.kernel.OpenEventW(2, False, self.name + ".Show")
            if event:
                try:
                    self.kernel.SetEvent(event)
                finally:
                    self.kernel.CloseHandle(event)
                return
            time.sleep(0.1)
        raise RuntimeError("Заметки ещё запускаются. Повторите попытку.")

    def bind(self, root, show, stop):
        for suffix in (".Show", ".Stop", ".Ready"):
            handle = self.kernel.CreateEventW(None, suffix == ".Ready", False, self.name + suffix)
            if not handle:
                raise c.WinError(c.get_last_error())
            self.events[suffix] = handle
        def poll():
            if self.kernel.WaitForSingleObject(self.events[".Stop"], 0) == 0:
                stop()
                return
            if self.kernel.WaitForSingleObject(self.events[".Show"], 0) == 0:
                show()
            root.after(100, poll)
        root.after(100, poll)
        self.kernel.SetEvent(self.events[".Ready"])

    def close(self):
        for handle in self.events.values():
            self.kernel.CloseHandle(handle)
        self.events.clear()
        if self.mutex:
            if self.owned:
                self.kernel.ReleaseMutex(self.mutex)
            self.kernel.CloseHandle(self.mutex)
            self.mutex = None
