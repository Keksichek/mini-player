"""Windows notification area and per-user launch at sign-in."""
import ctypes as c
from ctypes import wintypes as w
from collections import deque
from pathlib import Path
import subprocess
import sys
from tkinter import TclError
import winreg

RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
RUN_NAME = "CodexLocalNotes"
WINDOW_CLASS = "CodexLocalNotesTray"
TRAY_EVENT, SHOW_EVENT = 0x8001, 0x8002
USER = c.WinDLL("user32", use_last_error=True)
SHELL = c.WinDLL("shell32", use_last_error=True)
KERNEL = c.WinDLL("kernel32", use_last_error=True)
PROC = c.WINFUNCTYPE(c.c_ssize_t, w.HWND, w.UINT, w.WPARAM, w.LPARAM)


class WindowClass(c.Structure):
    _fields_ = [("style", w.UINT), ("proc", PROC), ("cls_extra", c.c_int),
                ("wnd_extra", c.c_int), ("instance", w.HINSTANCE), ("icon", w.HICON),
                ("cursor", w.HANDLE), ("brush", w.HBRUSH), ("menu", w.LPCWSTR),
                ("name", w.LPCWSTR)]


class NotifyIcon(c.Structure):
    _fields_ = [("size", w.DWORD), ("window", w.HWND), ("id", w.UINT),
                ("flags", w.UINT), ("message", w.UINT), ("icon", w.HICON),
                ("tip", c.c_wchar * 128), ("state", w.DWORD), ("state_mask", w.DWORD),
                ("info", c.c_wchar * 256), ("timeout", w.UINT),
                ("info_title", c.c_wchar * 64), ("info_flags", w.DWORD),
                ("guid", c.c_byte * 16), ("balloon_icon", w.HICON)]


def api(library, name, result, *arguments):
    function = getattr(library, name)
    function.restype, function.argtypes = result, arguments
    return function


find_window = api(USER, "FindWindowW", w.HWND, w.LPCWSTR, w.LPCWSTR)
post = api(USER, "PostMessageW", w.BOOL, w.HWND, w.UINT, w.WPARAM, w.LPARAM)
notify = api(SHELL, "Shell_NotifyIconW", w.BOOL, w.DWORD, c.POINTER(NotifyIcon))
destroy_window = api(USER, "DestroyWindow", w.BOOL, w.HWND)
def_window = api(USER, "DefWindowProcW", c.c_ssize_t, w.HWND, w.UINT, w.WPARAM, w.LPARAM)


def focus_existing():
    window = find_window(WINDOW_CLASS, None)
    return bool(window and post(window, SHOW_EVENT, 0, 0))


class Tray:
    def __init__(self, root, icon, show, quit_app):
        self.root, self.show, self.quit_app = root, show, quit_app
        self.visible = False
        self.events = deque()
        self.instance = api(KERNEL, "GetModuleHandleW", w.HMODULE, w.LPCWSTR)(None)
        self.proc = PROC(self.dispatch)
        self.taskbar_created = USER.RegisterWindowMessageW("TaskbarCreated")
        klass = WindowClass(proc=self.proc, instance=self.instance, name=WINDOW_CLASS)
        registered = api(USER, "RegisterClassW", w.ATOM, c.POINTER(WindowClass))(c.byref(klass))
        if not registered:
            raise c.WinError(c.get_last_error())
        create = api(USER, "CreateWindowExW", w.HWND, w.DWORD, w.LPCWSTR, w.LPCWSTR,
                     w.DWORD, c.c_int, c.c_int, c.c_int, c.c_int, w.HWND, w.HMENU,
                     w.HINSTANCE, w.LPVOID)
        self.window = create(0, WINDOW_CLASS, "Notes", 0, 0, 0, 0, 0, None, None,
                             self.instance, None)
        if not self.window:
            error = c.get_last_error()
            self.close()
            raise c.WinError(error)
        load = api(USER, "LoadImageW", w.HANDLE, w.HINSTANCE, w.LPCWSTR, w.UINT,
                   c.c_int, c.c_int, w.UINT)
        size = USER.GetSystemMetrics(49) or 16
        self.icon = load(None, str(icon), 1, size, size, 0x0010)
        self.data = NotifyIcon(size=c.sizeof(NotifyIcon), window=self.window, id=1, flags=7,
                               message=TRAY_EVENT, icon=self.icon, tip="Заметки")
        self.timer = root.after(75, self.process_events)

    def set_visible(self, visible):
        if self.visible == visible:
            return True
        if visible and (not self.icon or not notify(0, c.byref(self.data))):
            return False
        if not visible:
            notify(2, c.byref(self.data))
        self.visible = visible
        return True

    def dispatch(self, window, message, wp, lp):
        if message == SHOW_EVENT or (message == TRAY_EVENT and lp == 0x0202):
            self.events.append("show")
            return 0
        if message == TRAY_EVENT and lp == 0x0205:
            self.events.append("menu")
            return 0
        if message == self.taskbar_created and self.visible:
            self.events.append("restore_icon")
        return def_window(window, message, wp, lp)

    def process_events(self):
        self.timer = None
        while self.events:
            event = self.events.popleft()
            if event == "show":
                self.show()
            elif event == "menu" and self.visible:
                self.context_menu()
            elif event == "restore_icon" and self.visible:
                self.visible = False
                if not self.set_visible(True):
                    self.show()
        if self.window:
            self.timer = self.root.after(75, self.process_events)

    def context_menu(self):
        menu = api(USER, "CreatePopupMenu", w.HMENU)()
        if not menu:
            return
        try:
            append = api(USER, "AppendMenuW", w.BOOL, w.HMENU, w.UINT, c.c_size_t, w.LPCWSTR)
            append(menu, 0, 1, "Открыть заметки")
            append(menu, 0, 2, "Выйти")
            cursor = w.POINT()
            api(USER, "GetCursorPos", w.BOOL, c.POINTER(w.POINT))(c.byref(cursor))
            api(USER, "SetForegroundWindow", w.BOOL, w.HWND)(self.window)
            track = api(USER, "TrackPopupMenu", w.UINT, w.HMENU, w.UINT, c.c_int,
                        c.c_int, c.c_int, w.HWND, w.LPVOID)
            command = track(menu, 0x0102, cursor.x, cursor.y, 0, self.window, None)
            post(self.window, 0, 0, 0)
            if command in (1, 2):
                self.root.after_idle(self.show if command == 1 else self.quit_app)
        finally:
            api(USER, "DestroyMenu", w.BOOL, w.HMENU)(menu)

    def close(self):
        if getattr(self, "timer", None) is not None:
            try:
                self.root.after_cancel(self.timer)
            except (RuntimeError, TclError):
                pass
            self.timer = None
        self.set_visible(False)
        if self.window:
            destroy_window(self.window)
            self.window = None
        if getattr(self, "icon", None):
            api(USER, "DestroyIcon", w.BOOL, w.HICON)(self.icon)
            self.icon = None
        api(USER, "UnregisterClassW", w.BOOL, w.LPCWSTR, w.HINSTANCE)(
            WINDOW_CLASS, self.instance)


def startup_mode():
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
            command, _ = winreg.QueryValueEx(key, RUN_NAME)
    except FileNotFoundError:
        return "off"
    return "tray" if command.endswith(" --tray") else "window"


def set_startup(mode, script):
    if mode not in ("off", "window", "tray"):
        raise ValueError("Неизвестный режим автозапуска.")
    with winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, RUN_KEY, access=winreg.KEY_SET_VALUE) as key:
        if mode == "off":
            try:
                winreg.DeleteValue(key, RUN_NAME)
            except FileNotFoundError:
                pass
            return
        executable = Path(sys.executable)
        windowed = executable.with_name("pythonw.exe")
        command = [str(windowed if windowed.is_file() else executable), str(script)]
        if mode == "tray":
            command.append("--tray")
        winreg.SetValueEx(key, RUN_NAME, 0, winreg.REG_SZ, subprocess.list2cmdline(command))
