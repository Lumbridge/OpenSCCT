import hashlib
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "deploy"))
import publish_decomp_progress as publish


class PublishProgressTests(unittest.TestCase):
    def test_stale_source_report_fails_before_publication(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            tools = root / "decomp/tools"
            tools.mkdir(parents=True)
            for source in ("progress.py", "images.py"):
                shutil.copyfile(publish.ROOT / "decomp/tools" / source, tools / source)
            path = root / publish.REPORT
            path.parent.mkdir(parents=True)
            path.write_text("{}\n", encoding="utf-8")
            with self.assertRaises(subprocess.CalledProcessError):
                publish.checked_report(root)

    def test_stale_predecessor_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            subprocess.run(["git", "init", "-q", tmp], check=True)
            path = root / publish.REPORT
            path.parent.mkdir(parents=True)
            path.write_bytes(b"first")
            subprocess.run(["git", "add", publish.REPORT], cwd=root, check=True)
            subprocess.run(["git", "-c", "user.name=Test", "-c", "user.email=test@example.com",
                            "commit", "-qm", "first"], cwd=root, check=True)
            publish.check_predecessor(b"first", root)
            with self.assertRaisesRegex(ValueError, "outside this checkout"):
                publish.check_predecessor(b"later", root)

    @unittest.skipUnless(sys.platform == "linux", "the production SSH installer targets Linux")
    def test_atomic_install_and_concurrent_update(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "project-progress.json"

            def install(new, previous):
                args = [sys.executable, "-c", publish.INSTALL, str(path),
                        hashlib.sha256(previous).hexdigest(), hashlib.sha256(new).hexdigest()]
                return subprocess.run(args, input=new, capture_output=True)

            self.assertEqual(install(b"first", b"").returncode, 0)
            self.assertEqual(install(b"next", b"first").returncode, 0)
            self.assertNotEqual(install(b"stale", b"first").returncode, 0)
            self.assertEqual(path.read_bytes(), b"next")
            self.assertEqual(install(b"next", b"next").returncode, 0)
