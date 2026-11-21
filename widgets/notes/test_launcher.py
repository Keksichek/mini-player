import ctypes
import json
import tempfile
import unittest
from pathlib import Path
from notes.launcher import context, HubConnection, instance_name

class HubTests(unittest.TestCase):
    def test_context(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            app = root / 'Widgets' / 'notes'
            app.mkdir(parents=True)
            (root / 'launcher-install.json').write_text(json.dumps({'ProductId':'MiniPlayer.WidgetLauncher'}))
            self.assertEqual(context([], app), (root, root / 'Data' / 'notes'))
            self.assertEqual(context(['--launcher-root', str(root)], app)[1], root / 'Data' / 'notes')
            with self.assertRaises(ValueError):
                context(['--launcher-root', str(root), '--data-dir', str(root / 'elsewhere')], app)
            with self.assertRaises(ValueError):
                context(['--launcher-root', str(root)], root)
            with self.assertRaises(ValueError):
                context(['--data-dir', str(root)], root)
            self.assertEqual(context([], root), (None, root / 'data'))

    def test_real_named_events(self):
        with tempfile.TemporaryDirectory() as folder:
            owner = HubConnection(Path(folder))
            class Loop:
                callback = None
                def after(self, delay, callback): self.callback = callback
            loop = Loop()
            received = []
            try:
                self.assertTrue(owner.owned)
                owner.bind(loop, lambda: received.append('show'), lambda: received.append('stop'))
                self.assertEqual(owner.kernel.WaitForSingleObject(owner.events['.Ready'], 0), 0)
                owner.show_existing()
                loop.callback()
                self.assertEqual(received, ['show'])
                owner.kernel.SetEvent(owner.events['.Stop'])
                loop.callback()
                self.assertEqual(received, ['show', 'stop'])
                self.assertEqual(instance_name(folder), instance_name(str(folder).upper()))
            finally:
                owner.close()

if __name__ == '__main__': unittest.main()
