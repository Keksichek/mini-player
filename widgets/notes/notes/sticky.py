"""Paper stickers pinned above ordinary Windows windows."""
import json
from datetime import datetime, timedelta
import tkinter as tk
from tkinter import messagebox
from notes.date_picker import pick_datetime
from notes.drawing import WIDTH, HEIGHT, drawing_editor, paint
from notes.scrollbar import HoverScrollbar

PALETTES = (
    ("#FFF2AA", "#F2DD80", "#D5B95B"),  # yellow
    ("#FFD7E5", "#F5BCCF", "#CC8FA4"),  # pink
    ("#D7EEFF", "#BADEF7", "#86B5D4"),  # blue
    ("#DDF3D0", "#C5E6B4", "#90B780"),  # green
    ("#EBDDFA", "#DAC6F2", "#B399CB"),  # lilac
)
COLOR_NAMES = ('Жёлтый', 'Розовый', 'Голубой', 'Зелёный', 'Сиреневый')
INK = "#342A23"
MUTED = "#584A43"


class StickyNotes:
    def __init__(self, root, path, on_unpin, store, refresh):
        self.root = root
        self.store, self.refresh = store, refresh
        self.path = path
        self.on_unpin = on_unpin
        self.windows = {}
        self.suspended = False
        try:
            raw = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            raw = {}
        if isinstance(raw, list):
            raw = {key: 0 for key in raw if isinstance(key, str)}
        self.pinned = self.valid_colors(raw.get("pinned", raw) if isinstance(raw, dict) else {})
        self.colors = self.valid_colors(raw.get("colors") if isinstance(raw, dict) else {})
        self.colors.update(self.pinned)

    @staticmethod
    def valid_colors(raw):
        return {
            key: value for key, value in raw.items()
            if type(value) is int and 0 <= value < len(PALETTES)
        } if isinstance(raw, dict) else {}

    def save(self, pinned):
        block_ids = {block.id for block in self.store.tasks}
        pinned = {key: color for key, color in pinned.items() if key in block_ids}
        colors = {key: color for key, color in self.colors.items() if key in block_ids}
        colors.update(pinned)
        temporary = self.path.with_suffix(".tmp")
        temporary.write_text(json.dumps({"pinned": pinned, "colors": colors}, sort_keys=True),
                             encoding="utf-8")
        temporary.replace(self.path)
        self.pinned, self.colors = pinned, colors

    def palette_for(self, block_id):
        return PALETTES[self.colors.get(block_id, 0)]

    def toggle(self, task_id):
        pinned = self.pinned.copy()
        if task_id in pinned:
            del pinned[task_id]
        else:
            used = list(pinned.values())
            pinned[task_id] = self.colors.get(task_id, min(range(len(PALETTES)), key=used.count))
        self.save(pinned)

    def change_color(self, task_id, index):
        pinned = self.pinned.copy()
        pinned[task_id] = index
        sticker = self.windows[task_id]
        try:
            self.save(pinned)
        except OSError as error:
            messagebox.showerror("Стикер", f"Не удалось сохранить цвет.\n{error}",
                                 parent=sticker.window)
            return
        sticker.paint(index)

    def sync(self, tasks):
        wanted = {task.id: task for task in tasks if not self.suspended and task.id in self.pinned}
        for task_id in self.windows.keys() - wanted.keys():
            self.windows.pop(task_id).window.destroy()
        for task_id, task in wanted.items():
            if task_id not in self.windows:
                self.windows[task_id] = Sticker(
                    self.root, self.pinned[task_id], len(self.windows),
                    lambda color, key=task_id: self.change_color(key, color),
                    lambda key=task_id: self.on_unpin(key), self.store, self.refresh,
                )
            self.windows[task_id].update(task)


class Sticker:
    def __init__(self, root, color, index, change_color, unpin, store, refresh):
        self.store, self.refresh = store, refresh
        self.window = window = tk.Toplevel(root)
        window.withdraw()
        window.title("Стикер")
        window.overrideredirect(True)
        window.attributes("-toolwindow", True)
        window.attributes("-topmost", True)
        offset = index % 7
        window.geometry(f"258x238+{40 + 28 * offset}+{70 + 24 * offset}")
        self.sheet = tk.Frame(window, bd=0, highlightthickness=1)
        self.sheet.pack(fill="both", expand=True)
        self.header = tk.Frame(self.sheet, cursor="fleur", height=32)
        self.header.pack(fill="x")
        self.header.pack_propagate(False)
        self.preview = None
        self.signature = None

        def control(text, command):
            button = tk.Button(self.header, text=text, command=command, bd=0, relief="flat",
                               font=("Segoe UI", 9), padx=8, cursor="hand2", fg=INK)
            button.pack(side="right", fill="y", padx=2, pady=3)
            return button

        self.close_button = control("×", unpin)
        self.color_button = control("Цвет", self.choose_color)
        self.add_button = control("＋", self.add_menu)
        self.title = tk.Label(self.header, fg=INK, anchor="w", font=("Segoe UI", 9, "bold"),
                              cursor="fleur")
        self.title.pack(side="left", padx=(7, 0), fill="both", expand=True)
        self.change_color = change_color
        self.footer = tk.Frame(self.sheet, height=21)
        self.footer.pack(side="bottom", fill="x")
        self.footer.pack_propagate(False)
        self.fold = tk.Canvas(self.footer, width=21, height=21, highlightthickness=0,
                              cursor="size_nw_se")
        self.fold.pack(side="right")

        self.view = tk.Frame(self.sheet)
        self.view.pack(fill="both", expand=True, padx=(8, 4), pady=(6, 0))
        self.canvas = tk.Canvas(self.view, highlightthickness=0)
        self.canvas.pack(fill="both", expand=True, padx=(0, 10))
        self.rows = tk.Frame(self.canvas)
        self.rows_id = self.canvas.create_window(0, 0, window=self.rows, anchor="nw")
        self.rows.bind("<Configure>", self.layout_rows)
        self.canvas.bind("<Configure>", self.resize_rows)
        self.scrollbar = HoverScrollbar(self.view, self.canvas, PALETTES[color][0], PALETTES[color][2], window)

        for handle in (self.header, self.title):
            handle.bind("<ButtonPress-1>", self.start_drag)
            handle.bind("<B1-Motion>", self.move)
        self.fold.bind("<ButtonPress-1>", self.start_resize)
        self.fold.bind("<B1-Motion>", self.resize)
        window.bind("<MouseWheel>", self.wheel)
        window.protocol("WM_DELETE_WINDOW", unpin)
        self.paint(color)
        window.deiconify()

    def choose_color(self):
        menu = tk.Menu(self.window, tearoff=False)
        for index, (name, palette) in enumerate(zip(COLOR_NAMES, PALETTES)):
            menu.add_command(label=("✓ " if index == self.color else "    ") + name,
                             background=palette[0], foreground=INK,
                             command=lambda value=index: self.change_color(value))
        self.show_menu(menu, self.color_button)

    def add_menu(self):
        menu = tk.Menu(self.window, tearoff=False)
        menu.add_command(label="Текстовая запись", command=lambda: self.edit_text())
        menu.add_command(label="Напоминание", command=lambda: self.edit_text(scheduled=True))
        menu.add_command(label="Нарисовать заметку", command=lambda: self.open_drawing())
        self.show_menu(menu, self.add_button)

    @staticmethod
    def show_menu(menu, button):
        try:
            menu.tk_popup(button.winfo_rootx(), button.winfo_rooty() + button.winfo_height())
        finally:
            menu.grab_release()

    def paint(self, index):
        self.color = index
        self.paper, self.strip, self.edge = PALETTES[index]
        self.window.configure(bg=self.paper)
        self.sheet.configure(bg=self.paper, highlightbackground=self.edge,
                             highlightcolor=self.edge)
        self.header.configure(bg=self.strip)
        self.title.configure(bg=self.strip)
        for button in (self.close_button, self.color_button, self.add_button):
            button.configure(bg=self.strip, activebackground=self.paper)
        for widget in (self.footer, self.fold, self.view, self.canvas, self.rows):
            widget.configure(bg=self.paper)
        self.scrollbar.set_colors(self.paper, self.edge)
        self.fold.delete("all")
        self.fold.create_polygon(0, 21, 21, 0, 21, 21, fill=self.strip, outline=self.edge)
        if self.signature is not None:
            self.update(self.task, force=True)

    def layout_rows(self, _event=None):
        self.canvas.configure(scrollregion=self.canvas.bbox(self.rows_id))

    def resize_rows(self, event):
        self.canvas.itemconfigure(self.rows_id, width=event.width)

    def wheel(self, event):
        self.canvas.yview_scroll(-int(event.delta / 120) * 2, "units")
        return "break"

    def start_drag(self, event):
        self.drag = (event.x_root - self.window.winfo_x(), event.y_root - self.window.winfo_y())

    def move(self, event):
        dx, dy = self.drag
        self.window.geometry(f"+{event.x_root - dx}+{event.y_root - dy}")

    def start_resize(self, event):
        self.resize_from = (event.x_root, event.y_root, self.window.winfo_width(),
                            self.window.winfo_height())

    def resize(self, event):
        x, y, width, height = self.resize_from
        self.window.geometry(f"{max(180, width + event.x_root - x)}x{max(130, height + event.y_root - y)}")

    def update(self, task, force=False):
        self.task = task
        self.window.title(task.title or "Стикер")
        self.title.configure(text=task.title)
        entries = [task, *task.entries]
        signature = [(entry.id, entry.text, entry.done, entry.reminder, id(entry.drawing))
                     for entry in entries]
        if not force and signature == self.signature:
            return
        self.signature = signature
        position = self.canvas.yview()[0]
        self.hide_preview()
        for child in self.rows.winfo_children():
            child.destroy()
        for index, entry in enumerate(entries):
            if index:
                tk.Frame(self.rows, height=1, bg=self.strip).pack(fill="x", padx=4)
            self.entry_row(entry, primary=index == 0)
        self.rows.update_idletasks()
        self.layout_rows()
        self.canvas.yview_moveto(position)

    def entry_row(self, entry, primary):
        row = tk.Frame(self.rows, bg=self.paper, pady=4)
        row.pack(fill="x")
        check = tk.Button(row, text="☑" if entry.done else "☐", fg=MUTED, bg=self.paper,
                          activebackground=self.strip, bd=0, cursor="hand2", font=("Segoe UI", 12),
                          command=lambda: self.apply(self.store.toggle_entry, entry))
        check.pack(side="left", anchor="n")
        if entry.drawing is None:
            open_entry = lambda: self.edit_text(entry, scheduled=bool(entry.reminder))
            display = "✎"
        else:
            open_entry = lambda: self.open_drawing(entry)
            display = "▧"
        action = tk.Button(row, text="⋯", bg=self.paper, fg=MUTED, bd=0, cursor="hand2")
        if not primary:
            action.pack(side="right", anchor="n")
            def options():
                menu = tk.Menu(row, tearoff=False)
                menu.add_command(label="Изменить", command=open_entry)
                menu.add_command(label="Удалить запись", command=lambda: self.delete_entry(entry))
                self.show_menu(menu, action)
            action.configure(command=options)
        else:
            action.destroy()
        if entry.drawing is not None:
            icon = tk.Button(row, text=display, command=open_entry, bg=self.strip, fg=INK,
                             bd=0, padx=4, cursor="hand2")
            icon.pack(side="right", anchor="n", padx=3)
            icon.bind("<Enter>", lambda _: self.show_preview(icon, entry))
            icon.bind("<Leave>", lambda _: self.hide_preview())

        body = tk.Frame(row, bg=self.paper)
        body.pack(side="left", fill="x", expand=True, padx=4)
        font_style = "overstrike" if entry.done else "normal"
        label = tk.Label(body, text=entry.text, fg=MUTED if entry.done else INK,
                         bg=self.paper, anchor="w", justify="left", wraplength=120,
                         font=("Segoe Print", 10, font_style))
        label.pack(fill="x")
        body.bind("<Configure>", lambda event: label.configure(wraplength=max(30, event.width - 4)))
        if entry.reminder:
            try:
                date = datetime.fromisoformat(entry.reminder).strftime("%d.%m.%Y · %H:%M")
            except ValueError:
                date = entry.reminder
            tk.Label(body, text=f"◷ {date}", fg=MUTED, bg=self.paper,
                     font=("Segoe UI", 8)).pack(anchor="w")
        if primary:
            label.bind("<Double-Button-1>", lambda _: open_entry())

    def apply(self, action, *args):
        try:
            action(*args)
        except OSError as error:
            messagebox.showerror("Стикер", f"Не удалось сохранить запись.\n{error}", parent=self.window)
            return False
        self.refresh()
        return True

    def delete_entry(self, entry):
        if messagebox.askyesno("Стикер", "Удалить эту запись?", parent=self.window):
            self.apply(self.store.remove_entry, self.task, entry)

    def edit_text(self, entry=None, scheduled=False):
        dialog = tk.Toplevel(self.window)
        dialog.title("Напоминание" if scheduled else "Текстовая запись")
        dialog.attributes("-topmost", True)
        panel = tk.Frame(dialog, padx=14, pady=12)
        panel.pack(fill="both", expand=True)
        text = tk.Text(panel, width=32, height=4, wrap="word", font=("Segoe UI", 10))
        text.pack(fill="both", expand=True)
        text.insert("1.0", entry.text if entry else "")
        date = tk.StringVar(dialog)
        if scheduled:
            tk.Label(panel, text="Дата и время · ДД.ММ.ГГГГ ЧЧ:ММ").pack(anchor="w", pady=(7, 0))
            try:
                initial = (datetime.fromisoformat(entry.reminder) if entry and entry.reminder
                           else datetime.now() + timedelta(hours=1))
                date.set(initial.strftime("%d.%m.%Y %H:%M"))
            except ValueError:
                date.set(entry.reminder)
            timing = tk.Frame(panel)
            timing.pack(fill="x")
            tk.Entry(timing, textvariable=date).pack(side="left", fill="x", expand=True)
            tk.Button(timing, text="Календарь", command=lambda: pick_datetime(dialog, date)).pack(
                side="right", padx=(5, 0))
        status = tk.Label(panel, fg="#8F3227")
        status.pack(anchor="w")

        def save():
            value = text.get("1.0", "end-1c").strip()
            if not value:
                status.configure(text="Введите текст.")
                return
            try:
                reminder = datetime.strptime(date.get().strip(), "%d.%m.%Y %H:%M").isoformat(
                    timespec="minutes") if scheduled and date.get().strip() else None
            except ValueError:
                status.configure(text="Проверьте формат даты и времени.")
                return
            if scheduled and not reminder and not entry:
                status.configure(text="Укажите дату и время.")
                return
            if entry is self.task:
                saved = self.apply(self.store.update, self.task.id, value, reminder)
            else:
                saved = self.apply(self.store.save_entry, self.task, entry, value, reminder)
            if saved:
                dialog.destroy()

        tk.Button(panel, text="Сохранить", command=save).pack(anchor="e", pady=(5, 0))
        dialog.bind("<Escape>", lambda _: dialog.destroy())
        dialog.grab_set()
        text.focus_set()

    def open_drawing(self, entry=None):
        self.hide_preview()
        drawing_editor(self.window,
                       lambda name, strokes: self.apply(self.store.save_entry, self.task,
                                                       entry, name, None, strokes), entry)

    def show_preview(self, icon, entry):
        self.hide_preview()
        preview = tk.Toplevel(self.window)
        preview.overrideredirect(True)
        preview.attributes("-topmost", True)
        scale = 0.45
        width, height = int(WIDTH * scale), int(HEIGHT * scale)
        canvas = tk.Canvas(preview, width=width, height=height, bg="white",
                           highlightthickness=1, highlightbackground=self.edge)
        canvas.pack()
        x = min(icon.winfo_rootx(), icon.winfo_screenwidth() - width - 4)
        y = icon.winfo_rooty() + icon.winfo_height() + 5
        if y + height > icon.winfo_screenheight():
            y = icon.winfo_rooty() - height - 5
        preview.geometry(f"+{x}+{y}")
        paint(canvas, entry.drawing, scale)
        self.preview = preview

    def hide_preview(self):
        if self.preview is not None:
            self.preview.destroy()
            self.preview = None
