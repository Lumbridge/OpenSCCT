"""Checks portable extraction modes/checksums and a writable desktop installation."""
import hashlib
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("package_launcher", ROOT / "tools/linux/package-launcher.py")
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class LauncherPackageTests(unittest.TestCase):
    def test_archive_preserves_executable_modes_and_exact_checksum(self):
        with tempfile.TemporaryDirectory() as temp:
            binary = Path(temp) / "launcher"
            header = bytearray(64)
            header[:6] = b"\x7fELF\x02\x01"
            header[18:20] = b"\x3e\x00"
            binary.write_bytes(header)
            archive, checksum = packager.package(binary, Path(temp) / "output", ROOT)
            with tarfile.open(archive) as tar:
                exe = tar.getmember("OpenSCCT-Launcher/OpenSCCT.Launcher-linux-x64")
                self.assertEqual(0o755, exe.mode)
                self.assertEqual(bytes(header), tar.extractfile(exe).read())
                self.assertEqual(0o755, tar.getmember("OpenSCCT-Launcher/install.sh").mode)
                self.assertEqual(0o644, tar.getmember("OpenSCCT-Launcher/openscct.svg").mode)
            self.assertEqual(hashlib.sha256(header).hexdigest() + "  OpenSCCT.Launcher-linux-x64\n", checksum.read_text())
            version = ET.parse(ROOT / "Directory.Build.props").findtext(".//LauncherVersion")
            self.assertEqual(f"OpenSCCT.Launcher-{version}-linux-x64.sha256", checksum.name)

    def test_rejects_a_windows_binary_before_writing_assets(self):
        with tempfile.TemporaryDirectory() as temp:
            binary = Path(temp) / "launcher"
            binary.write_bytes(b"MZ" + b"\0" * 100)
            with self.assertRaisesRegex(ValueError, "native Linux x86_64"):
                packager.package(binary, Path(temp) / "output", ROOT)
            self.assertFalse((Path(temp) / "output").exists())

    @unittest.skipUnless(os.name == "posix" and Path("/bin/sleep").exists(), "Linux desktop installer")
    def test_install_and_replace_running_binary_with_space_in_data_path(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            shutil.copy2(ROOT / "packaging/linux-launcher/install.sh", source)
            shutil.copy2(ROOT / "game/branding/icon.svg", source / "openscct.svg")
            shutil.copy2("/bin/sleep", source / "OpenSCCT.Launcher-linux-x64")
            home = root / "player home"
            home.mkdir()
            data = home / "user data"
            env = dict(os.environ, XDG_DATA_HOME=str(data), XDG_CONFIG_HOME=str(home / "config"))
            command = ["sh", str(source / "install.sh")]
            subprocess.run(command, check=True, env=env, capture_output=True)
            installed = data / "OpenSCCTLauncher/OpenSCCT.Launcher-linux-x64"
            running = subprocess.Popen([str(installed), "20"])
            try:
                subprocess.run(command, check=True, env=env, capture_output=True)
                self.assertTrue(os.access(installed, os.X_OK))
                self.assertEqual((source / installed.name).read_bytes(), installed.read_bytes())
                desktop = (data / "applications/io.github.Lumbridge.OpenSCCT.Launcher.desktop").read_text()
                self.assertIn(f'Exec="{installed}" %u\n', desktop)
                self.assertIn("MimeType=x-scheme-handler/openscct;", desktop)
            finally:
                running.terminate()
                running.wait(timeout=5)


if __name__ == "__main__":
    unittest.main()
