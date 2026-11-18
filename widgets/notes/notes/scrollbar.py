"""An unobtrusive, draggable scrollbar for a Tk canvas."""
import tkinter as tk


class HoverScrollbar:
    def __init__(self, container, canvas, background, thumb_color, hover_target=None):
        self.owner, self.canvas = container, canvas
        self.hover_target = hover_target or container
        self.hovering, self.drag = False, None
        self.track = tk.Canvas(container, width=8, highlightthickness=0, cursor="hand2")
        self.set_colors(background, thumb_color)
        self.track.bind("<Configure>", self.draw)
        self.track.bind("<ButtonPress-1>", self.start)
        self.track.bind("<B1-Motion>", self.move)
        self.track.bind("<ButtonRelease-1>", self.end)
        self.hover_target.bind("<Enter>", self.enter, add="+")
        self.hover_target.bind("<Leave>", self.leave, add="+")
        canvas.configure(yscrollcommand=lambda *_: self.draw())

    def set_colors(self, background, thumb_color):
        self.track.configure(bg=background)
        self.color = thumb_color
        self.draw()

    def enter(self, _event):
        self.hovering = True
        self.draw()

    def leave(self, _event):
        self.track.after_idle(self.check_pointer)

    def check_pointer(self):
        if not self.track.winfo_exists():
            return
        target = self.hover_target
        x, y = target.winfo_pointerxy()
        left, top = target.winfo_rootx(), target.winfo_rooty()
        self.hovering = left <= x < left + target.winfo_width() and top <= y < top + target.winfo_height()
        self.draw()

    def thumb(self):
        height = self.track.winfo_height()
        first, last = self.canvas.yview()
        length = min(height, max(21, height * (last - first)))
        top = (height - length) * first / max(0.0001, 1 - (last - first))
        return top, length

    def draw(self, _event=None):
        first, last = self.canvas.yview()
        if (not self.hovering and self.drag is None) or last - first >= 0.999:
            self.track.place_forget()
            return
        self.track.place(relx=1, x=-1, anchor="ne", relheight=1)
        top, length = self.thumb()
        self.track.delete("thumb")
        if length >= 6:
            self.track.create_line(4, top + 3, 4, top + length - 3, width=6,
                                   capstyle="round", fill=self.color, tags="thumb")

    def start(self, event):
        top, length = self.thumb()
        self.drag = event.y - top if top <= event.y <= top + length else length / 2
        self.move(event)

    def move(self, event):
        if self.drag is None:
            return
        _, length = self.thumb()
        first, last = self.canvas.yview()
        fraction = (event.y - self.drag) / max(1, self.track.winfo_height() - length)
        self.canvas.yview_moveto(max(0, min(1, fraction)) * (1 - (last - first)))

    def end(self, _event):
        self.drag = None
        self.check_pointer()
