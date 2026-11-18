"""Editor and preview for hand-drawn entries."""
import tkinter as tk
from tkinter import messagebox

WIDTH, HEIGHT = 400, 260
INKS = ("#24232A", "#3157BD", "#B5324B", "#237452", "#824DAD")


def paint(canvas, strokes, scale=1):
    canvas.delete("ink")
    for stroke in strokes:
        coords = [point * scale for point in stroke["points"]]
        width = max(1, stroke["width"] * scale)
        if len(coords) == 2:
            x, y = coords
            radius = width / 2
            canvas.create_oval(x-radius, y-radius, x+radius, y+radius,
                               fill=stroke["color"], outline="", tags="ink")
        else:
            canvas.create_line(*coords, fill=stroke["color"], width=width,
                               capstyle="round", joinstyle="round", tags="ink")


def drawing_editor(parent, save, entry=None):
    window = tk.Toplevel(parent)
    window.title("Рисунок")
    window.attributes("-topmost", True)
    window.resizable(False, False)
    panel = tk.Frame(window, bg="#F1ECE6", padx=10, pady=10)
    panel.pack()
    bar = tk.Frame(panel, bg="#F1ECE6")
    bar.pack(fill="x", pady=(0, 8))
    canvas = tk.Canvas(panel, width=WIDTH, height=HEIGHT, bg="white", bd=0,
                       highlightthickness=0, cursor="crosshair")
    canvas.pack()
    strokes = list(entry.drawing) if entry else []
    redo = []
    current = None
    color, size = INKS[0], 3
    swatches = []
    name = tk.StringVar(window, value=entry.text if entry else "Рисунок")
    footer = tk.Frame(panel, bg="#F1ECE6")
    footer.pack(fill="x", pady=(8, 0))
    tk.Entry(footer, textvariable=name, font=("Segoe UI", 10)).pack(side="left", fill="x", expand=True)

    def select(value, width, active):
        nonlocal color, size
        color, size = value, width
        for widget in swatches:
            widget.configure(relief="sunken" if widget is active else "raised")

    def start(event):
        nonlocal current
        current = {"color": color, "width": size, "points": [event.x, event.y]}
        paint(canvas, [*strokes, current])

    def extend(event):
        if current is None:
            return
        points = current["points"]
        x, y = max(0, min(WIDTH, event.x)), max(0, min(HEIGHT, event.y))
        canvas.create_line(*points[-2:], x, y, fill=color, width=size,
                           capstyle="round", tags="ink")
        points.extend((x, y))

    def finish(_event):
        nonlocal current
        if current is not None:
            strokes.append(current)
            redo.clear()
            current = None
            update()

    def undo():
        if strokes:
            redo.append(strokes.pop())
            update()

    def restore():
        if redo:
            strokes.append(redo.pop())
            update()

    def update():
        paint(canvas, strokes)
        back.configure(state="normal" if strokes else "disabled")
        forward.configure(state="normal" if redo else "disabled")

    def commit():
        if not any(stroke["color"] != "#FFFFFF" for stroke in strokes):
            messagebox.showinfo("Рисунок", "Добавьте штрих на полотно.", parent=window)
            return
        if save(name.get().strip() or "Рисунок", strokes):
            window.destroy()

    for ink in INKS:
        button = tk.Button(bar, bg=ink, activebackground=ink, width=2, cursor="hand2")
        button.configure(command=lambda value=ink, widget=button: select(value, 3, widget))
        button.pack(side="left", padx=(0, 3))
        swatches.append(button)
    eraser = tk.Button(bar, text="Ластик", cursor="hand2")
    eraser.configure(command=lambda: select("#FFFFFF", 15, eraser))
    eraser.pack(side="left", padx=4)
    swatches.append(eraser)
    select(INKS[0], 3, swatches[0])
    forward = tk.Button(bar, text="↷ Вернуть", command=restore)
    forward.pack(side="right")
    back = tk.Button(bar, text="↶ Отменить", command=undo)
    back.pack(side="right", padx=3)
    tk.Button(footer, text="Сохранить", command=commit, padx=8).pack(side="right", padx=(8, 0))

    canvas.bind("<ButtonPress-1>", start)
    canvas.bind("<B1-Motion>", extend)
    canvas.bind("<ButtonRelease-1>", finish)
    window.bind("<Control-z>", lambda _: undo())
    window.bind("<Control-y>", lambda _: restore())
    update()
    window.grab_set()
    window.focus_set()
    return window
