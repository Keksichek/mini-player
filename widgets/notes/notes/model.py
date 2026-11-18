"""Task model and UTF-8 JSON persistence."""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
from datetime import datetime
import json
from pathlib import Path
from typing import Any
import uuid


@dataclass
class Task:
    id: str
    text: str
    done: bool = False
    reminder: str | None = None
    notified: bool = False
    entries: list[Task] = field(default_factory=list)
    drawing: list[dict] | None = None
    title: str = ""

    def __post_init__(self):
        self.entries = [Task(**item) if isinstance(item, dict) else item for item in self.entries]

    @classmethod
    def create(cls, text: str, reminder: str | None = None) -> "Task":
        return cls(uuid.uuid4().hex, text.strip(), False, reminder, False)


class NotesStore:
    def __init__(self, path: Path) -> None:
        self.path = path
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.tasks: list[Task] = []
        self.load()

    def load(self) -> None:
        if not self.path.exists():
            self.save()
            return
        try:
            payload: Any = json.loads(self.path.read_text(encoding="utf-8"))
            self.tasks = [Task(**item) for item in payload if isinstance(item, dict) and item.get("text")]
        except (json.JSONDecodeError, OSError, TypeError, ValueError):
            # Keep the original file for recovery; begin with an empty in-memory list.
            self.tasks = []

    def save(self) -> None:
        temporary = self.path.with_suffix(".tmp")
        temporary.write_text(
            json.dumps([asdict(task) for task in self.tasks], ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        temporary.replace(self.path)

    def save_entry(self, parent: Task, entry: Task | None, text: str,
                   reminder: str | None, drawing: list[dict] | None = None) -> None:
        replacement = Task.create(text, reminder)
        replacement.drawing = drawing
        previous = parent.entries
        if entry is None:
            parent.entries = [*previous, replacement]
        else:
            replacement.id = entry.id
            replacement.done = entry.done
            replacement.notified = entry.notified and entry.reminder == reminder
            parent.entries = [replacement if item.id == entry.id else item for item in previous]
        try:
            self.save()
        except OSError:
            parent.entries = previous
            raise

    def remove_entry(self, parent: Task, entry: Task) -> None:
        previous = parent.entries
        parent.entries = [item for item in previous if item.id != entry.id]
        try:
            self.save()
        except OSError:
            parent.entries = previous
            raise

    def toggle_entry(self, entry: Task) -> None:
        entry.done = not entry.done
        try:
            self.save()
        except OSError:
            entry.done = not entry.done
            raise

    def add(self, text: str, reminder: str | None = None, title: str = "") -> Task:
        task = Task.create(text, reminder)
        task.title = title.strip()
        self.tasks.insert(0, task)
        try:
            self.save()
        except OSError:
            self.tasks.pop(0)
            raise
        return task

    def update(self, task_id: str, text: str, reminder: str | None, *, title: str | None = None) -> None:
        task = self.get(task_id)
        previous = task.text, task.reminder, task.notified, task.title
        if task.text != text.strip() or task.reminder != reminder:
            task.notified = False
        task.text = text.strip()
        task.reminder = reminder
        if title is not None:
            task.title = title.strip()
        try:
            self.save()
        except OSError:
            task.text, task.reminder, task.notified, task.title = previous
            raise

    def toggle(self, task_id: str) -> None:
        self.toggle_entry(self.get(task_id))

    def delete(self, task_id: str) -> None:
        self.remove_selected({task_id}, set())

    def remove_selected(self, blocks: set[str], reminders: set[str]) -> None:
        previous = self.tasks
        self.tasks = [task for task in previous if task.id not in blocks]
        cleared = []
        for block in self.tasks:
            for entry in (block, *block.entries):
                if entry.id in reminders and entry.reminder:
                    cleared.append((entry, entry.reminder, entry.notified))
                    entry.reminder = None
                    entry.notified = False
        try:
            self.save()
        except OSError:
            self.tasks = previous
            for entry, reminder, notified in cleared:
                entry.reminder, entry.notified = reminder, notified
            raise

    def edit_block(self, block: Task, title: str, edits: dict) -> None:
        previous_title = block.title
        snapshots = []
        for entry in (block, *block.entries):
            if entry.id not in edits:
                continue
            text, reminder, drawing = edits[entry.id]
            snapshots.append((entry, entry.text, entry.reminder, entry.drawing, entry.notified))
            if entry.reminder != reminder:
                entry.notified = False
            entry.text, entry.reminder, entry.drawing = text, reminder, drawing
        block.title = title.strip()
        try:
            self.save()
        except OSError:
            block.title = previous_title
            for entry, text, reminder, drawing, notified in snapshots:
                entry.text, entry.reminder, entry.drawing, entry.notified = text, reminder, drawing, notified
            raise

    def get(self, task_id: str) -> Task:
        return next(task for task in self.tasks if task.id == task_id)

    def due(self, now: datetime | None = None) -> list[Task]:
        now = now or datetime.now()
        due_tasks: list[Task] = []
        for task in (entry for sheet in self.tasks for entry in (sheet, *sheet.entries)):
            if task.done or task.notified or not task.reminder:
                continue
            try:
                if datetime.fromisoformat(task.reminder) <= now:
                    task.notified = True
                    due_tasks.append(task)
            except ValueError:
                continue
        if due_tasks:
            try:
                self.save()
            except OSError:
                for task in due_tasks:
                    task.notified = False
                raise
        return due_tasks
