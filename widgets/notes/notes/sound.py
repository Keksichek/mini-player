"""Import WAV or MP3 and keep the chosen reminder sound as PCM WAV."""
from base64 import b64encode
import os
from pathlib import Path
import shutil
import subprocess
from tempfile import TemporaryDirectory
import wave
import winsound


class ReminderSound:
    def __init__(self, data_folder):
        self.default_path = Path(__file__).with_name("reminder-default.wav")
        self.custom_path = data_folder / "reminder-custom.wav"

    def choose(self, source):
        source = Path(source)
        extension = source.suffix.lower()
        if extension not in {".wav", ".mp3"}:
            raise ValueError("Выберите файл WAV или MP3.")
        temporary = self.custom_path.with_suffix(".wav.tmp")
        if source.stat().st_size > 20 * 1024 * 1024:
            raise ValueError("Размер файла должен быть не больше 20 МБ.")
        try:
            if extension == ".mp3":
                self.convert_mp3(source, temporary)
            else:
                shutil.copyfile(source, temporary)
            with wave.open(str(temporary), "rb") as sound:
                frames = sound.getnframes()
                if not frames or frames > sound.getframerate() * 30:
                    raise ValueError("Выберите звук длительностью от 0 до 30 секунд.")
                if sound.getnchannels() not in (1, 2) or sound.getsampwidth() not in (1, 2):
                    raise ValueError("Нужен WAV PCM, моно или стерео, 8 или 16 бит.")
                if len(sound.readframes(frames)) != frames * sound.getnchannels() * sound.getsampwidth():
                    raise ValueError("Файл WAV повреждён.")
            winsound.PlaySound(None, 0)
            temporary.replace(self.custom_path)
        except (wave.Error, EOFError) as error:
            raise ValueError("Не удалось прочитать звук. Выберите исправный WAV PCM или MP3.") from error
        finally:
            temporary.unlink(missing_ok=True)

    @staticmethod
    def convert_mp3(source, destination):
        script = Path(__file__).with_name("convert_audio.ps1").read_text(encoding="ascii")
        command = b64encode(script.encode("utf-16-le")).decode("ascii")
        with TemporaryDirectory(prefix="notes-audio-") as folder:
            original = Path(folder) / "input.mp3"
            converted = Path(folder) / "output.wav"
            shutil.copyfile(source, original)
            converted.touch()
            env = {**os.environ, "NOTES_AUDIO_SOURCE": str(original),
                   "NOTES_AUDIO_DESTINATION": str(converted)}
            try:
                result = subprocess.run(
                    ["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", command],
                    env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                    creationflags=subprocess.CREATE_NO_WINDOW, timeout=20,
                )
            except (OSError, subprocess.TimeoutExpired) as error:
                raise ValueError("Не удалось преобразовать MP3. Попробуйте файл WAV.") from error
            if result.returncode == 2:
                raise ValueError("Выберите звук длительностью до 30 секунд.")
            if result.returncode != 0 or not converted.stat().st_size:
                raise ValueError("Не удалось прочитать MP3. Попробуйте другой MP3 или WAV.")
            shutil.copyfile(converted, destination)

    def reset(self):
        winsound.PlaySound(None, 0)
        self.custom_path.unlink(missing_ok=True)

    def preview(self):
        source = self.custom_path if self.custom_path.is_file() else self.default_path
        self.play_file(source)

    @staticmethod
    def play_file(path):
        winsound.PlaySound(str(path), winsound.SND_FILENAME | winsound.SND_ASYNC | winsound.SND_NODEFAULT)

    def play_alert(self):
        try:
            self.preview()
        except (OSError, RuntimeError):
            try:
                self.play_file(self.default_path)
            except (OSError, RuntimeError):
                winsound.MessageBeep(winsound.MB_OK)
