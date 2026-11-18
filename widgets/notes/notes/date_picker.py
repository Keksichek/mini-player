"""A date picker shared by the main window and stickers."""
import calendar
from datetime import date, datetime, timedelta
import tkinter as tk

FORMAT = "%d.%m.%Y %H:%M"
MONTHS = ("Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август",
          "Сентябрь", "Октябрь", "Ноябрь", "Декабрь")


def pick_datetime(parent, target):
    try:
        initial = datetime.strptime(target.get().strip(), FORMAT)
    except ValueError:
        initial = datetime.now() + timedelta(hours=1)
    selected = initial.date()
    year, month = selected.year, selected.month
    dialog = tk.Toplevel(parent)
    dialog.title("Дата и время")
    dialog.transient(parent)
    dialog.attributes("-topmost", True)
    dialog.resizable(False, False)
    previous_grab = parent.grab_current()
    panel = tk.Frame(dialog, bg="#FFF9ED", padx=14, pady=12)
    panel.pack()
    nav = tk.Frame(panel, bg="#FFF9ED")
    nav.pack(fill="x")
    caption = tk.Label(nav, bg="#FFF9ED", fg="#3C3027", font=("Segoe UI", 11, "bold"))
    caption.pack(side="left", fill="x", expand=True)
    days = tk.Frame(panel, bg="#FFF9ED")
    days.pack(fill="x", pady=7)
    time_row = tk.Frame(panel, bg="#FFF9ED")
    time_row.pack(fill="x")
    hour = tk.StringVar(dialog, f"{initial.hour:02d}")
    minute = tk.StringVar(dialog, f"{initial.minute:02d}")
    tk.Label(time_row, text="Время", bg="#FFF9ED").pack(side="left", padx=(0, 9))
    tk.Spinbox(time_row, from_=0, to=23, format="%02.0f", wrap=True, width=4,
               textvariable=hour, justify="center").pack(side="left")
    tk.Label(time_row, text=":", bg="#FFF9ED").pack(side="left")
    tk.Spinbox(time_row, from_=0, to=59, format="%02.0f", wrap=True, width=4,
               textvariable=minute, justify="center").pack(side="left")
    status = tk.Label(panel, bg="#FFF9ED", fg="#983B29")
    status.pack(anchor="w", pady=(3, 0))

    def show():
        caption.configure(text=f"{MONTHS[month - 1]} {year}")
        for widget in days.winfo_children():
            widget.destroy()
        for column, text in enumerate(("Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс")):
            tk.Label(days, text=text, width=4, bg="#FFF9ED", fg="#675649").grid(row=0, column=column)
        for row, week in enumerate(calendar.monthcalendar(year, month), start=1):
            for column, day in enumerate(week):
                if day:
                    value = date(year, month, day)
                    tk.Button(days, text=str(day), width=3, relief="flat", cursor="hand2",
                              bg="#F0CE85" if value == selected else "#F5ECDD",
                              command=lambda day_value=value: select(day_value)).grid(
                                  row=row, column=column, padx=1, pady=1)

    def select(value):
        nonlocal selected, year, month
        selected, year, month = value, value.year, value.month
        show()

    def move(delta):
        nonlocal year, month
        index = year * 12 + month - 1 + delta
        if 12 <= index < 10000 * 12:
            year, month_index = divmod(index, 12)
            month = month_index + 1
            show()

    def close():
        dialog.destroy()
        if previous_grab is not None and previous_grab.winfo_exists():
            previous_grab.grab_set()

    def apply():
        try:
            value = datetime(selected.year, selected.month, selected.day,
                             int(hour.get()), int(minute.get()))
        except ValueError:
            status.configure(text="Укажите часы от 0 до 23 и минуты от 0 до 59.")
            return
        target.set(value.strftime(FORMAT))
        close()

    buttons = tk.Frame(nav, bg="#FFF9ED")
    buttons.pack(side="right")
    tk.Button(buttons, text="‹", width=3, command=lambda: move(-1)).pack(side="left")
    tk.Button(buttons, text="›", width=3, command=lambda: move(1)).pack(side="left")
    footer = tk.Frame(panel, bg="#FFF9ED")
    footer.pack(fill="x")
    tk.Button(footer, text="Сегодня", command=lambda: select(date.today())).pack(side="left")
    tk.Button(footer, text="Выбрать", command=apply).pack(side="right")
    dialog.bind("<Escape>", lambda _: close())
    dialog.protocol("WM_DELETE_WINDOW", close)
    show()
    dialog.grab_set()
    dialog.focus_set()
    return dialog
