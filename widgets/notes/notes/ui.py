"""The notes window, its task editor and its two color palettes."""
from dataclasses import replace
from datetime import datetime, timedelta
import json
from queue import Empty, Queue
from threading import Thread
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from notes.date_picker import pick_datetime
from notes.drawing import drawing_editor
from notes.scrollbar import HoverScrollbar
from notes.sticky import StickyNotes
from notes.windows import set_startup, startup_mode
from notes.uninstall import uninstall

PALETTES = {
    "light": dict(back="#EFE8DE", paper="#FFFDF7", card="#F5EEE4", ink="#2F2925",
                  muted="#665E56", line="#E0D4C6", accent="#925039", white="#FFFFFF", scroll="#BBA797"),
    "dark": dict(back="#211D1B", paper="#302A27", card="#3B322D", ink="#F8EFE6",
                 muted="#C7B8AB", line="#51453D", accent="#E9AC93", white="#2C1C15", scroll="#77675E"),
}
FILTERS = ("Все", "Активные", "Готовые")


class NotesApp:
    def __init__(self, root, store, reminders, *, hidden=False):
        self.root, self.store, self.reminders = root, store, reminders
        for option, value in {
            "*Button.background": "#F1ECE6", "*Button.foreground": "#302923",
            "*Button.disabledForeground": "#645A51", "*Button.activeBackground": "#E2D7CA",
            "*Button.activeForeground": "#302923", "*Label.foreground": "#302923",
            "*Entry.background": "#FFFFFF", "*Entry.foreground": "#302923",
            "*Entry.insertBackground": "#302923", "*Spinbox.background": "#FFFFFF",
            "*Spinbox.foreground": "#302923", "*Menu.foreground": "#302923",
            "*Menu.background": "#F1ECE6", "*Text.foreground": "#302923",
            "*Text.background": "#FFFFFF",
        }.items():
            root.option_add(option, value)
        self.settings = store.path.with_name("settings.json")
        self.theme = "light"
        try:
            saved = json.loads(self.settings.read_text(encoding="utf-8"))
            if isinstance(saved, dict) and saved.get("theme") in ("dark", "light"):
                self.theme = saved["theme"]
        except (OSError, ValueError):
            pass
        self.draft = tk.StringVar(master=root)
        self.filter = "Все"
        self.sheet = None
        self.stickers = StickyNotes(root, store.path.with_name("stickers.json"), self.toggle_pin, store, self.refresh)
        self.stickers.suspended = hidden
        self.hide_to_tray = None
        self.reminders.palette_for = self.stickers.palette_for
        self.build()
        self.root.after_idle(self.entry.focus_set)

    def label(self, parent, text, *, surface="paper", color="ink", size=10, family="Segoe UI", weight="normal", **options):
        return tk.Label(parent, text=text, bg=self.c[surface], fg=self.c[color],
                        font=(family, size, weight), **options)

    def button(self, parent, text, command, *, surface="card", color="ink", padx=10):
        button = tk.Button(parent, text=text, command=command, font=("Segoe UI", 9),
                           bg=self.c[surface], fg=self.c[color], disabledforeground=self.c[color],
                           activebackground=self.c["line"],
                           activeforeground=self.c["ink"], relief="flat", bd=0, padx=padx, pady=6,
                           cursor="hand2", highlightthickness=1, highlightbackground=self.c[surface],
                           highlightcolor=self.c["accent"])
        button.bind("<Enter>", lambda _: button.configure(
            bg=self.c[surface] if surface == "accent" else self.c["line"],
            fg=self.c[color] if surface == "accent" else self.c["ink"])
                    if button.cget("state") != "disabled" else None)
        button.bind("<Leave>", lambda _: button.configure(bg=self.c[surface], fg=self.c[color]))
        def activate(_event):
            button.invoke()
            return "break"

        button.bind("<Return>", activate)
        return button

    def field(self, parent, variable):
        return tk.Entry(parent, textvariable=variable, bg=self.c["card"], fg=self.c["ink"],
                        insertbackground=self.c["ink"], selectbackground=self.c["accent"],
                        selectforeground=self.c["white"], font=("Segoe UI", 11), relief="flat",
                        bd=6, highlightthickness=1, highlightbackground=self.c["line"],
                        highlightcolor=self.c["accent"])

    def build(self):
        self.c = PALETTES[self.theme]
        style = ttk.Style(self.root)
        style.configure("Notes.TCombobox", padding=4)
        style.map("Notes.TCombobox", fieldbackground=[("readonly", self.c["card"])],
                  foreground=[("readonly", self.c["ink"])],
                  selectbackground=[("readonly", self.c["card"])],
                  selectforeground=[("readonly", self.c["ink"])])
        self.root.configure(bg=self.c["back"])
        self.sheet = tk.Frame(self.root, bg=self.c["paper"], highlightthickness=1,
                              highlightbackground=self.c["line"])
        self.sheet.pack(fill="both", expand=True, padx=14, pady=14)
        perforation = tk.Canvas(self.sheet, height=16, bg=self.c["paper"], highlightthickness=0)
        perforation.pack(fill="x")
        perforation.bind("<Configure>", lambda e: self.draw_perforation(perforation, e.width))

        area = tk.Frame(self.sheet, bg=self.c["paper"], padx=17, pady=10)
        area.pack(fill="both", expand=True)
        safety = tk.Frame(area, bg=self.c["paper"])
        safety.pack(fill="x", pady=(0, 4))
        self.button(safety, "⚠ Удалить приложение", self.uninstall_app,
                    surface="paper", color="accent", padx=5).pack(side="right")
        header = tk.Frame(area, bg=self.c["paper"])
        header.pack(fill="x", pady=(0, 14))
        header.columnconfigure(0, weight=1)
        title = self.label(header, "Заметки", family="Georgia", size=25)
        title.grid(row=0, column=0, sticky="w")
        settings = tk.Frame(header, bg=self.c["paper"])
        settings.grid(row=0, column=1, sticky="e")
        self.button(settings, "Автозапуск", self.startup_settings, padx=5).pack(side="left")
        self.button(settings, "Звук", self.sound_settings, padx=7).pack(side="left", padx=4)
        self.button(settings, "☾ Тёмная" if self.theme == "light" else "☀ Светлая",
                    self.switch_theme, padx=7).pack(side="left")

        def place_settings(event):
            narrow = event.width < title.winfo_reqwidth() + settings.winfo_reqwidth() + 8
            row = 1 if narrow else 0
            if int(settings.grid_info()["row"]) != row:
                if narrow:
                    settings.grid_configure(row=1, column=0, columnspan=2, sticky="w", pady=(4, 0))
                else:
                    settings.grid_configure(row=0, column=1, columnspan=1, sticky="e", pady=0)

        header.bind("<Configure>", place_settings)

        self.label(area, "Новое дело", color="muted", size=9).pack(anchor="w", pady=(0, 4))
        self.entry = self.field(area, self.draft)
        self.entry.pack(fill="x")
        self.entry.bind("<Return>", lambda _: self.add())
        controls = tk.Frame(area, bg=self.c["paper"])
        controls.pack(fill="x", pady=(8, 16))
        self.button(controls, "＋ Добавить", self.add, surface="accent", color="white").pack(side="left")
        self.button(controls, "◷ С напоминанием", self.new_reminder).pack(side="left", padx=7)

        self.tabs = tk.Frame(area, bg=self.c["paper"])
        self.tabs.pack(fill="x", pady=(0, 5))
        tk.Frame(area, bg=self.c["line"], height=1).pack(fill="x", pady=(0, 8))
        management = tk.Frame(area, bg=self.c["paper"])
        management.pack(side="bottom", fill="x", pady=(5, 0))
        self.button(management, "Выбрать для удаления", self.removal_picker,
                    padx=5).pack(side="left", padx=(0, 5))
        self.button(management, "Удалить все уведомления", self.remove_all_reminders,
                    color="accent", padx=5).pack(side="left")
        self.progress = self.label(area, "", color="muted", size=9)
        self.progress.pack(side="bottom", anchor="w", pady=(8, 0))
        viewport = tk.Frame(area, bg=self.c["paper"])
        viewport.pack(fill="both", expand=True)
        self.canvas = tk.Canvas(viewport, bg=self.c["paper"], highlightthickness=0, height=1, yscrollincrement=18)
        self.canvas.pack(fill="both", expand=True, padx=(0, 10))
        self.scrollbar = HoverScrollbar(viewport, self.canvas, self.c["paper"], self.c["scroll"])
        self.rows = tk.Frame(self.canvas, bg=self.c["paper"])
        self.rows_id = self.canvas.create_window(0, 0, window=self.rows, anchor="nw")
        self.rows.bind("<Configure>", lambda _: self.canvas.configure(scrollregion=self.canvas.bbox(self.rows_id)))
        self.canvas.bind("<Configure>", lambda e: self.canvas.itemconfigure(self.rows_id, width=e.width))
        self.canvas.bind("<MouseWheel>", self.wheel)
        self.rows.bind("<MouseWheel>", self.wheel)
        self.scrollbar.track.bind("<MouseWheel>", self.wheel)
        self.refresh()

    def draw_perforation(self, canvas, width):
        canvas.delete("all")
        canvas.create_line(14, 13, width - 14, 13, fill=self.c["line"], dash=(3, 4))
        for x in range(23, width - 12, 24):
            canvas.create_oval(x - 3, 2, x + 3, 8, fill=self.c["back"], outline=self.c["line"])

    def wheel(self, event):
        if event.delta:
            self.canvas.yview_scroll(-int(event.delta / 120), "units")

    def startup_settings(self):
        if getattr(self, "launcher_root", None):
            messagebox.showinfo("VibeWidgetsHub", "Автозапуск этой копии управляется в настройках VibeWidgetsHub.", parent=self.root)
            return
        dialog = tk.Toplevel(self.root)
        dialog.title("Автозапуск")
        dialog.transient(self.root)
        dialog.attributes("-topmost", True)
        dialog.resizable(False, False)
        panel = tk.Frame(dialog, bg=self.c["paper"], padx=14, pady=14)
        panel.pack(fill="both", expand=True)
        self.label(panel, "При входе в Windows", weight="bold").pack(anchor="w", pady=(0, 7))
        try:
            current = startup_mode()
        except OSError as error:
            messagebox.showerror("Автозапуск", str(error), parent=dialog)
            dialog.destroy()
            return
        mode = tk.StringVar(dialog, value=current)
        for value, caption in (("off", "Не запускать"), ("window", "Открыть окно «Заметки»"),
                               ("tray", "Запустить в фоне, со значком рядом с часами")):
            tk.Radiobutton(panel, text=caption, variable=mode, value=value, bg=self.c["paper"],
                           fg=self.c["ink"], selectcolor=self.c["card"],
                           activebackground=self.c["paper"], activeforeground=self.c["ink"],
                           font=("Segoe UI", 9)).pack(anchor="w", pady=2)
        controls = tk.Frame(panel, bg=self.c["paper"])
        controls.pack(fill="x", pady=(11, 0))

        def save():
            try:
                set_startup(mode.get(), self.settings.parent.parent / "app.py")
            except OSError as error:
                messagebox.showerror("Автозапуск", f"Не удалось сохранить настройку.\n{error}",
                                     parent=dialog)
                return
            dialog.destroy()

        def hide_now():
            dialog.destroy()
            self.hide_to_tray()

        self.button(controls, "Сохранить", save, surface="accent", color="white").pack(side="right")
        if self.hide_to_tray is not None:
            self.button(controls, "Свернуть в трей сейчас", hide_now, padx=5).pack(side="left")
        dialog.bind("<Escape>", lambda _: dialog.destroy())
        dialog.grab_set()

    def sound_settings(self):
        dialog = tk.Toplevel(self.root)
        dialog.title("Звук напоминаний")
        dialog.transient(self.root)
        dialog.attributes("-topmost", True)
        dialog.resizable(False, False)
        panel = tk.Frame(dialog, bg=self.c["paper"], padx=16, pady=14)
        panel.pack(fill="both", expand=True)
        sound = self.reminders.sound
        status = self.label(panel, "", weight="bold")
        status.pack(anchor="w")
        self.label(panel, "WAV или MP3, до 30 секунд", size=9, color="muted").pack(
            anchor="w", pady=(2, 12))
        controls = tk.Frame(panel, bg=self.c["paper"])
        controls.pack()
        tooltip = None
        busy = False
        results = Queue()

        def show_tip(_event):
            nonlocal tooltip
            if tooltip is not None or busy:
                return
            tooltip = tk.Toplevel(dialog)
            tooltip.overrideredirect(True)
            tooltip.attributes("-topmost", True)
            self.label(tooltip, "Для воспроизведения используется WAV. При загрузке MP3 "
                       "автоматически преобразуется в WAV.", surface="card", wraplength=265,
                       justify="left", padx=9, pady=7, highlightthickness=1,
                       highlightbackground=self.c["line"]).pack()
            tooltip.update_idletasks()
            x = min(info.winfo_rootx(), info.winfo_screenwidth() - tooltip.winfo_reqwidth() - 8)
            y = info.winfo_rooty() + info.winfo_height() + 4
            if y + tooltip.winfo_reqheight() > info.winfo_screenheight():
                y = info.winfo_rooty() - tooltip.winfo_reqheight() - 4
            tooltip.geometry(f"+{x}+{y}")

        def hide_tip(_event=None):
            nonlocal tooltip
            if tooltip is not None:
                tooltip.destroy()
                tooltip = None

        def refresh():
            custom = sound.custom_path.is_file()
            status.configure(text="Свой звук" if custom else "Стандартный сигнал")
            reset.configure(state="normal" if custom else "disabled")

        def perform(action, *args):
            try:
                action(*args)
            except (OSError, ValueError, RuntimeError) as error:
                messagebox.showerror("Звук напоминаний", str(error), parent=dialog)
                return
            refresh()

        def choose():
            nonlocal busy
            hide_tip()
            source = filedialog.askopenfilename(parent=dialog, title="Выберите звук",
                                                filetypes=[("Звук WAV или MP3", "*.wav *.mp3"),
                                                           ("Звук WAV", "*.wav"), ("Звук MP3", "*.mp3")])
            if not source:
                return
            busy = True
            is_mp3 = source.lower().endswith(".mp3")
            status.configure(text="Преобразуем MP3 в WAV…" if is_mp3 else "Загружаем WAV…")
            for button in (load, preview, reset, info):
                button.configure(state="disabled")

            def import_sound():
                try:
                    sound.choose(source)
                except (OSError, ValueError, RuntimeError) as error:
                    results.put(str(error))
                else:
                    results.put(None)

            def finish():
                nonlocal busy
                try:
                    error = results.get_nowait()
                except Empty:
                    dialog.after(75, finish)
                    return
                busy = False
                for button in (load, preview, info):
                    button.configure(state="normal")
                refresh()
                if error is not None:
                    messagebox.showerror("Звук напоминаний", error, parent=dialog)
                elif is_mp3:
                    status.configure(text="MP3 преобразован в WAV")

            Thread(target=import_sound, daemon=True).start()
            dialog.after(75, finish)

        load = self.button(controls, "Выбрать звук", choose, surface="accent", color="white")
        load.pack(side="left")
        info = self.button(controls, "!", lambda: show_tip(None), color="accent", padx=6)
        info.pack(side="left", padx=(3, 0))
        info.bind("<Enter>", show_tip, add="+")
        info.bind("<Leave>", hide_tip, add="+")
        info.bind("<FocusIn>", show_tip)
        info.bind("<FocusOut>", hide_tip)
        preview = self.button(controls, "Прослушать", lambda: perform(sound.preview))
        preview.pack(side="left", padx=5)
        reset = self.button(controls, "Сбросить", lambda: perform(sound.reset))
        reset.pack(side="left")
        refresh()
        def close():
            if not busy:
                dialog.destroy()

        dialog.protocol("WM_DELETE_WINDOW", close)
        dialog.bind("<Escape>", lambda _: close())
        dialog.grab_set()

    def switch_theme(self):
        theme = "dark" if self.theme == "light" else "light"
        try:
            temp = self.settings.with_suffix(".tmp")
            temp.write_text(json.dumps({"theme": theme}), encoding="utf-8")
            temp.replace(self.settings)
        except OSError as error:
            messagebox.showerror("Тема", f"Не удалось сохранить тему.\n{error}", parent=self.root)
            return
        position = self.canvas.yview()[0]
        self.theme = theme
        self.sheet.destroy()
        self.build()
        self.root.update_idletasks()
        self.canvas.yview_moveto(position)
        self.entry.focus_set()

    def add(self):
        text = self.draft.get().strip()
        if text:
            self.store.add(text)
            self.after_add()
        self.entry.focus_set()

    def after_add(self):
        self.draft.set("")
        if self.filter == "Готовые":
            self.filter = "Активные"
        self.refresh(reset=True)

    def new_reminder(self):
        self.editor()

    def select_filter(self, value):
        self.filter = value
        self.refresh(reset=True)

    def refresh(self, reset=False):
        position = self.canvas.yview()[0]
        for widget in (*self.tabs.winfo_children(), *self.rows.winfo_children()):
            widget.destroy()
        for name in FILTERS:
            selected = name == self.filter
            self.button(self.tabs, name, lambda value=name: self.select_filter(value),
                        surface="card" if selected else "paper",
                        color="accent" if selected else "muted").pack(side="left", padx=(0, 4))
        tasks = self.store.tasks
        self.stickers.sync(tasks)
        entries = [entry for task in tasks for entry in (task, *task.entries)]
        done = sum(entry.done for entry in entries)
        self.progress.configure(text=f"Выполнено {done} из {len(entries)}")
        visible = [task for task in tasks if self.filter == "Все"
                   or any(entry.done == (self.filter == "Готовые")
                          for entry in (task, *task.entries))]
        if not visible:
            empty = {"Все": "Добавьте первое дело", "Активные": "Нет активных дел",
                     "Готовые": "Здесь появятся выполненные дела"}
            self.label(self.rows, empty[self.filter], color="muted", pady=26).pack(fill="x")
        for task in visible:
            self.task_row(task)
        self.rows.update_idletasks()
        self.canvas.configure(scrollregion=self.canvas.bbox(self.rows_id))
        self.canvas.yview_moveto(0 if reset else position)

    def task_row(self, task):
        card = tk.Frame(self.rows, bg=self.c["card"], padx=7, pady=6)
        card.pack(fill="x", pady=(0, 6))
        header = tk.Frame(card, bg=self.c["card"])
        header.pack(fill="x")
        actions = tk.Frame(header, bg=self.c["card"])
        actions.pack(side="right", anchor="n")
        self.button(actions, "Править", lambda: self.editor(task), padx=5).pack(side="left")
        pinned = task.id in self.stickers.pinned
        self.button(actions, "Открепить" if pinned else "Закрепить",
                    lambda: self.toggle_pin(task.id), color="accent" if pinned else "muted",
                    padx=5).pack(side="left")
        self.button(actions, "×", lambda: self.delete(task), color="muted", padx=7).pack(side="left")
        title = self.label(header, task.title or "Блок", surface="card", size=10,
                           weight="bold", anchor="w", justify="left", wraplength=85)
        title.pack(side="left", fill="x", expand=True, padx=(4, 2))
        title.bind("<Configure>", lambda e: title.configure(wraplength=max(20, e.width - 3)))
        for index, entry in enumerate((task, *task.entries)):
            if index:
                tk.Frame(card, bg=self.c["line"], height=1).pack(fill="x", padx=4)
            self.block_entry(card, task, entry)
        self.bind_list_scroll(card)

    def bind_list_scroll(self, widget):
        widget.bind("<MouseWheel>", self.wheel)
        for child in widget.winfo_children():
            self.bind_list_scroll(child)

    def block_entry(self, card, task, item):
        row = tk.Frame(card, bg=self.c["card"], pady=3)
        row.pack(fill="x")
        self.button(row, "[✓]" if item.done else "[  ]", lambda: self.toggle_task(item),
                    color="accent", padx=5).pack(side="left", anchor="n")
        if item.drawing is not None:
            self.button(row, "▧ Открыть", lambda: self.open_drawing(task, item),
                        color="accent", padx=5).pack(side="right", anchor="n")
        body = tk.Frame(row, bg=self.c["card"])
        body.pack(side="left", fill="x", expand=True, padx=6)
        title = self.label(body, item.text, surface="card", color="muted" if item.done else "ink",
                           size=11, weight="overstrike" if item.done else "normal",
                           anchor="w", justify="left", wraplength=140)
        title.pack(fill="x", pady=(4, 0))
        if item.reminder:
            try:
                when = datetime.fromisoformat(item.reminder).strftime("%d.%m.%Y · %H:%M")
            except ValueError:
                when = item.reminder
            self.label(body, f"◷ {when}", surface="card", color="muted", size=8,
                       anchor="w").pack(fill="x")
        body.bind("<Configure>", lambda e: title.configure(wraplength=max(30, e.width - 2)))

    def open_drawing(self, task, item):
        def save(name, strokes):
            return self.save_change(self.store.save_entry, task, item, name, None, strokes)

        drawing_editor(self.root, save, item)

    def save_change(self, action, *args, **kwargs):
        try:
            action(*args, **kwargs)
        except OSError as error:
            messagebox.showerror("Заметки", f"Не удалось сохранить изменения.\n{error}", parent=self.root)
            return False
        self.refresh()
        return True

    def toggle_pin(self, task_id):
        try:
            self.stickers.toggle(task_id)
        except OSError as error:
            messagebox.showerror("Стикер", f"Не удалось изменить закрепление.\n{error}", parent=self.root)
            return
        self.refresh()


    def toggle_task(self, task):
        self.save_change(self.store.toggle_entry, task)

    def delete(self, task):
        if messagebox.askyesno("Удалить блок", "Удалить блок со всеми его записями?", parent=self.root):
            self.save_change(self.store.delete, task.id)

    def remove_all_reminders(self):
        reminders = {item.id for block in self.store.tasks
                     for item in (block, *block.entries) if item.reminder}
        if not reminders:
            messagebox.showinfo("Уведомления", "Нет напоминаний для удаления.", parent=self.root)
            return
        if messagebox.askyesno(
            "Удалить все уведомления",
            f"Снять все напоминания ({len(reminders)})? Текст, рисунки и блоки сохранятся.",
            parent=self.root,
        ):
            self.save_change(self.store.remove_selected, set(), reminders)

    def removal_picker(self):
        dialog = tk.Toplevel(self.root)
        dialog.title("Выборочное удаление")
        dialog.transient(self.root)
        dialog.attributes("-topmost", True)
        dialog.configure(bg=self.c["paper"])
        dialog.minsize(350, 275)
        panel = tk.Frame(dialog, bg=self.c["paper"], padx=16, pady=14)
        panel.pack(fill="both", expand=True)
        self.label(panel, "Можно выбрать несколько строк обычным щелчком.",
                   color="muted", wraplength=390, justify="left").pack(anchor="w", pady=(0, 8))
        frame = tk.Frame(panel, bg=self.c["paper"])
        frame.pack(fill="both", expand=True)
        items = tk.Listbox(frame, selectmode="multiple", exportselection=False, width=48,
                           height=12, bg=self.c["card"], fg=self.c["ink"], bd=0,
                           highlightthickness=1, highlightbackground=self.c["line"],
                           selectbackground=self.c["accent"], selectforeground=self.c["white"],
                           font=("Segoe UI", 10))
        items.pack(fill="both", expand=True, padx=(0, 12))
        scroll = HoverScrollbar(frame, items, self.c["paper"], self.c["scroll"])
        scroll.track.bind("<MouseWheel>", lambda e: items.yview_scroll(-int(e.delta / 120), "units"))
        choices = []
        for block in self.store.tasks:
            name = block.title or self.short_text(block.text)
            items.insert("end", f"Блок · {name}")
            choices.append(("block", block.id, block.id))
            for entry in (block, *block.entries):
                if entry.reminder:
                    items.insert("end", f"    ◷ {self.display_date(entry.reminder)} · {self.short_text(entry.text)}")
                    choices.append(("reminder", entry.id, block.id))
        details = self.label(panel, "", color="muted", wraplength=400, justify="left")
        details.pack(anchor="w", pady=(6, 0))
        bar = tk.Frame(panel, bg=self.c["paper"])
        bar.pack(fill="x", pady=(6, 0))

        def selection():
            picked = [choices[index] for index in items.curselection()]
            blocks = {key for kind, key, _ in picked if kind == "block"}
            reminders = {key for kind, key, parent in picked
                         if kind == "reminder" and parent not in blocks}
            return blocks, reminders

        def refresh_selection(_event=None):
            blocks, reminders = selection()
            if choices:
                details.configure(text=f"Блоков к удалению: {len(blocks)}. Напоминаний к снятию: {len(reminders)}.")
            else:
                details.configure(text="Нет блоков или напоминаний.")
            remove.configure(state="normal" if blocks or reminders else "disabled")

        def confirm():
            blocks, reminders = selection()
            if not blocks and not reminders:
                return
            lines = []
            if blocks:
                lines.append(f"Удалить блоки со всеми их записями: {len(blocks)}.")
            if reminders:
                lines.append(f"Снять напоминания в остальных блоках: {len(reminders)}. Записи сохранятся.")
            if messagebox.askyesno("Удалить выбранное", "\n".join(lines), parent=dialog):
                if self.save_change(self.store.remove_selected, blocks, reminders):
                    dialog.destroy()

        remove = self.button(bar, "Удалить выбранное", confirm, surface="accent", color="white")
        remove.pack(side="right")
        self.button(bar, "Отмена", dialog.destroy).pack(side="right", padx=6)
        items.bind("<<ListboxSelect>>", refresh_selection)
        dialog.bind("<Escape>", lambda _: dialog.destroy())
        refresh_selection()
        dialog.grab_set()
        items.focus_set()

    @staticmethod
    def short_text(text):
        text = " ".join(text.split())
        return text[:42] + "…" if len(text) > 42 else text

    @staticmethod
    def display_date(value):
        if not value:
            return ""
        try:
            return datetime.fromisoformat(value).strftime("%d.%m.%Y %H:%M")
        except ValueError:
            return value

    def editor(self, task=None):
        dialog = tk.Toplevel(self.root)
        dialog.title("Редактировать блок" if task else "Новый блок")
        dialog.transient(self.root)
        dialog.attributes("-topmost", True)
        dialog.configure(bg=self.c["paper"])
        dialog.resizable(False, False)
        panel = tk.Frame(dialog, bg=self.c["paper"], padx=18, pady=15)
        panel.pack(fill="both", expand=True)
        entries = (task, *task.entries) if task else ()
        original = [(item.text, self.display_date(item.reminder), item.drawing) for item in entries]
        default_time = (datetime.now() + timedelta(hours=1)).strftime("%d.%m.%Y %H:%M")
        drafts = list(original) if task else [(self.draft.get(), default_time, None)]
        selected = 0
        title = tk.StringVar(dialog, task.title if task else "")
        schedule = tk.StringVar(dialog)
        self.label(panel, "Название блока").pack(anchor="w")
        title_field = self.field(panel, title)
        title_field.configure(width=34)
        title_field.pack(fill="x", pady=(4, 12))

        if task:
            self.label(panel, "Запись для редактирования").pack(anchor="w")
            chooser = ttk.Combobox(panel, state="readonly", width=34, style="Notes.TCombobox",
                                   font=("Segoe UI", 10),
                                   values=[f"{i}. {self.short_text(item.text)}"
                                           for i, item in enumerate(entries, 1)])
            chooser.current(0)
            chooser.pack(fill="x", pady=(4, 12))

        self.label(panel, "Текст записи").pack(anchor="w")
        field = tk.Text(panel, width=32, height=3, wrap="word", undo=True,
                        font=("Segoe UI", 11), relief="flat", bd=6,
                        bg=self.c["card"], fg=self.c["ink"], insertbackground=self.c["ink"],
                        selectbackground=self.c["accent"], selectforeground=self.c["white"],
                        highlightthickness=1, highlightbackground=self.c["line"],
                        highlightcolor=self.c["accent"])
        field.pack(fill="x", pady=(4, 12))
        timing = tk.Frame(panel, bg=self.c["paper"])
        self.label(timing, "Напоминание · ДД.ММ.ГГГГ ЧЧ:ММ").pack(anchor="w")
        date_row = tk.Frame(timing, bg=self.c["paper"])
        date_row.pack(fill="x", pady=(4, 2))
        self.field(date_row, schedule).pack(side="left", fill="x", expand=True)
        self.button(date_row, "Календарь", lambda: pick_datetime(dialog, schedule),
                    padx=6).pack(side="right", padx=(6, 0))
        self.label(timing, "Пустое поле снимает напоминание", color="muted", size=9).pack(anchor="w")
        status = self.label(panel, "", color="accent", wraplength=320, justify="left")
        status.pack(anchor="w", pady=6)
        bar = tk.Frame(panel, bg=self.c["paper"])
        bar.pack(fill="x")

        def remember():
            drafts[selected] = (field.get("1.0", "end-1c"), schedule.get(), drafts[selected][2])

        def show():
            value, date, strokes = drafts[selected]
            field.delete("1.0", "end")
            field.insert("1.0", value)
            field.edit_reset()
            schedule.set(date)
            if strokes is None:
                edit_drawing.pack_forget()
                timing.pack(fill="x", before=status)
            else:
                timing.pack_forget()
                edit_drawing.pack(anchor="w", before=status)
            if task:
                chooser.current(selected)

        def switch(_event):
            nonlocal selected
            target = chooser.current()
            remember()
            selected = target
            status.configure(text="")
            show()

        def open_drawing():
            remember()
            index = selected
            value, date, strokes = drafts[index]

            def save_drawing(name, ink):
                drafts[index] = (name, date, ink)
                show()
                return True

            preview = drawing_editor(dialog, save_drawing,
                                     replace(entries[index], text=value, drawing=strokes))

            def restore_grab(event):
                if event.widget is preview and dialog.winfo_exists():
                    dialog.after_idle(dialog.grab_set)

            preview.bind("<Destroy>", restore_grab, add="+")

        def save():
            nonlocal selected
            remember()
            updates = {}
            for i, (value, date, strokes) in enumerate(drafts):
                if task and drafts[i] == original[i]:
                    continue
                error = ""
                reminder = None
                name = value.strip()
                if not name:
                    error = "Введите текст записи."
                elif task and date.strip() == original[i][1]:
                    reminder = entries[i].reminder
                elif date.strip():
                    try:
                        reminder = datetime.strptime(date.strip(), "%d.%m.%Y %H:%M").isoformat(
                            timespec="minutes")
                    except ValueError:
                        error = "Укажите дату и время в формате ДД.ММ.ГГГГ ЧЧ:ММ."
                if error:
                    selected = i
                    show()
                    status.configure(text=error)
                    return
                if task:
                    updates[entries[i].id] = (name, reminder, strokes)
            if task:
                saved = self.save_change(self.store.edit_block, task, title.get(), updates)
            else:
                saved = self.save_change(self.store.add, drafts[0][0], reminder, title=title.get())
                if saved:
                    self.after_add()
            if saved:
                dialog.destroy()

        edit_drawing = self.button(panel, "Открыть рисунок", open_drawing)
        if task:
            chooser.bind("<<ComboboxSelected>>", switch)
        self.button(bar, "Сохранить", save, surface="accent", color="white").pack(side="right")
        self.button(bar, "Отмена", dialog.destroy).pack(side="right", padx=6)
        dialog.bind("<Escape>", lambda _: dialog.destroy())
        dialog.bind("<Control-Return>", lambda _: save())
        show()
        dialog.grab_set()
        field.focus_set()


    def uninstall_app(self):
        if getattr(self, "launcher_root", None):
            messagebox.showinfo("VibeWidgetsHub", "Удалите виджет через VibeWidgetsHub → Установленные → Заметки → Удалить. Ваши записи сохранятся для повторной установки.", parent=self.root)
            return
        if not messagebox.askyesno(
            "Удалить Заметки",
            "Удалить приложение, все заметки, рисунки, настройки и свой звук?\n"
            "Автозапуск будет отключён. Это действие нельзя отменить.",
            parent=self.root,
        ):
            return
        try:
            uninstall(self.settings.parent.parent)
        except (OSError, ValueError) as error:
            messagebox.showerror("Удаление", str(error), parent=self.root)
            return
        self.close()

    def close(self):
        self.reminders.stop()
        self.root.destroy()
