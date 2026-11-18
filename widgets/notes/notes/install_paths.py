"""Windows destinations owned by the Notes installer."""
import ctypes
from pathlib import Path


def shell_folder(identifier):
    buffer = ctypes.create_unicode_buffer(32768)
    if ctypes.windll.shell32.SHGetFolderPathW(None, identifier, None, 0, buffer):
        raise OSError("Не удалось определить папку Windows.")
    return Path(buffer.value)


def shortcuts():
    return [shell_folder(0x10) / "Заметки.lnk",
            shell_folder(0x0B) / "Заметки.lnk"]
