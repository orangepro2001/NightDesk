#!/usr/bin/env python3
import hashlib
import io
import json
import os
import tempfile
import threading
import unittest
import urllib.request
import zipfile
from pathlib import Path


class FeedTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory()
        root = Path(cls.temp.name)
        os.environ["NIGHTDESK_FEED_DIR"] = str(root)
        os.environ["NIGHTDESK_FEED_PORT"] = "0"
        import server
        cls.server_module = server
        cls.root = root

        apk = b"fake-apk" * 100
        (root / "nightdesk-8.apk").write_bytes(apk)
        (root / "manifest.json").write_text(json.dumps({
            "schemaVersion": 1,
            "packageName": "dev.nightdesk.android",
            "versionCode": 8,
            "versionName": "1.2.0",
            "minSdk": 29,
            "size": len(apk),
            "sha256": hashlib.sha256(apk).hexdigest(),
            "certificateSha256": "a" * 64,
            "apkPath": "/nightdesk/latest.apk",
            "notes": "android test",
        }), "utf-8")

        release = {
            "schemaVersion": 1,
            "packageName": "dev.nightdesk.windows",
            "version": "1.6.0",
            "versionCode": 10600,
            "files": [
                {"name": "NightDesk.exe", "size": 16, "sha256": hashlib.sha256(b"NightDeskWindows").hexdigest()},
                {"name": "nx-host.exe", "size": 8, "sha256": hashlib.sha256(b"nx-host!").hexdigest()},
            ],
        }
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("release.json", json.dumps(release))
            archive.writestr("NightDesk.exe", b"NightDeskWindows")
            archive.writestr("nx-host.exe", b"nx-host!")
        package = buffer.getvalue()
        windows_dir = root / "windows"
        windows_dir.mkdir()
        (windows_dir / "nightdesk-windows-1.6.0.zip").write_bytes(package)
        (root / "windows-manifest.json").write_text(json.dumps({
            "schemaVersion": 1,
            "packageName": "dev.nightdesk.windows",
            "version": "1.6.0",
            "versionCode": 10600,
            "arch": "win-x64",
            "size": len(package),
            "sha256": hashlib.sha256(package).hexdigest(),
            "packagePath": "/nightdesk/windows/latest.zip",
            "notes": "windows test",
        }), "utf-8")

        cls.httpd = server.ThreadingHTTPServer(("127.0.0.1", 0), server.Handler)
        cls.port = cls.httpd.server_address[1]
        cls.thread = threading.Thread(target=cls.httpd.serve_forever, daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        cls.httpd.shutdown()
        cls.httpd.server_close()
        cls.temp.cleanup()

    def get(self, path):
        with urllib.request.urlopen(f"http://127.0.0.1:{self.port}{path}", timeout=5) as response:
            return response.status, response.headers, response.read()

    def test_health_reports_both_feeds(self):
        status, _, body = self.get("/healthz")
        self.assertEqual(200, status)
        self.assertIn(b"android=ok", body)
        self.assertIn(b"windows=ok", body)

    def test_windows_manifest_and_package(self):
        manifest = json.loads(self.get("/nightdesk/api/v1/windows-update")[2])
        self.assertEqual("1.6.0", manifest["version"])
        status, headers, body = self.get("/nightdesk/windows/latest.zip")
        self.assertEqual(200, status)
        self.assertEqual("application/zip", headers["Content-Type"])
        self.assertEqual(manifest["size"], len(body))
        self.assertEqual(manifest["sha256"], hashlib.sha256(body).hexdigest())

    def test_windows_proxy_stripped_path(self):
        status, _, body = self.get("/windows/latest.zip")
        self.assertEqual(200, status)
        manifest = json.loads(self.get("/api/v1/windows-update")[2])
        self.assertEqual(manifest["sha256"], hashlib.sha256(body).hexdigest())

    def test_android_routes_remain_compatible(self):
        manifest = json.loads(self.get("/api/v1/android-update")[2])
        self.assertEqual(8, manifest["versionCode"])
        status, _, body = self.get("/nightdesk/latest.apk")
        self.assertEqual(200, status)
        self.assertEqual(b"fake-apk" * 100, body)

    def test_index_contains_both_products(self):
        status, _, body = self.get("/nightdesk")
        self.assertEqual(200, status)
        self.assertIn("Android 客户端", body.decode("utf-8"))
        self.assertIn("Windows 桌面端", body.decode("utf-8"))


if __name__ == "__main__":
    unittest.main()
