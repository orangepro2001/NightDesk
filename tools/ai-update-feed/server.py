#!/usr/bin/env python3
"""NightDesk update feed for Android APKs and Windows ZIP packages.

Tailscale Serve keeps the endpoint tailnet-only. This process itself accepts no
credentials and only listens on loopback. Manifests are switched atomically by
the publisher scripts; binary files are immutable.
"""
import hashlib
import html
import json
import os
import re
import shutil
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

ROOT = Path(os.environ.get("NIGHTDESK_FEED_DIR", Path(__file__).resolve().parent))
PORT = int(os.environ.get("NIGHTDESK_FEED_PORT", "17768"))
ANDROID_MANIFEST_PATH = ROOT / "manifest.json"
WINDOWS_MANIFEST_PATH = ROOT / "windows-manifest.json"
ANDROID_PACKAGE_NAME = "dev.nightdesk.android"
WINDOWS_PACKAGE_NAME = "dev.nightdesk.windows"
MAX_ANDROID_SIZE = 100 * 1024 * 1024
MAX_WINDOWS_SIZE = 300 * 1024 * 1024
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+$")

_android_cache = {"key": None, "sha": None}
_windows_cache = {"key": None, "sha": None}


def load_android_manifest():
    try:
        m = json.loads(ANDROID_MANIFEST_PATH.read_text("utf-8"))
        int(m["versionCode"]); int(m["size"])
        if m["packageName"] != ANDROID_PACKAGE_NAME:
            return None
        if int(m["size"]) <= 0 or int(m["size"]) > MAX_ANDROID_SIZE:
            return None
        if len(str(m["sha256"])) != 64 or len(str(m["certificateSha256"])) != 64:
            return None
        return m
    except Exception:
        return None


def load_windows_manifest():
    try:
        m = json.loads(WINDOWS_MANIFEST_PATH.read_text("utf-8"))
        if int(m["schemaVersion"]) != 1 or m["packageName"] != WINDOWS_PACKAGE_NAME:
            return None
        if not VERSION_RE.match(str(m["version"])):
            return None
        if int(m["versionCode"]) <= 0 or m["arch"] != "win-x64":
            return None
        if int(m["size"]) <= 0 or int(m["size"]) > MAX_WINDOWS_SIZE:
            return None
        if len(str(m["sha256"])) != 64 or m["packagePath"] != "/nightdesk/windows/latest.zip":
            return None
        return m
    except Exception:
        return None


def expected_apk_path(m):
    return ROOT / f"nightdesk-{int(m['versionCode'])}.apk"


def expected_windows_path(m):
    return ROOT / "windows" / f"nightdesk-windows-{m['version']}.zip"


def verified_file(path, size, sha256, cache):
    if not path.is_file():
        return None
    st = path.stat()
    key = (str(path), st.st_mtime_ns, st.st_size)
    if cache["key"] != key:
        digest = hashlib.sha256()
        with path.open("rb") as fh:
            for chunk in iter(lambda: fh.read(1024 * 1024), b""):
                digest.update(chunk)
        cache["key"] = key
        cache["sha"] = digest.hexdigest()
    if st.st_size != int(size) or cache["sha"] != str(sha256).lower():
        return None
    return path


def verified_apk(m):
    return verified_file(expected_apk_path(m), m["size"], m["sha256"], _android_cache)


def verified_windows_package(m):
    return verified_file(expected_windows_path(m), m["size"], m["sha256"], _windows_cache)


STYLE = """
body{font-family:system-ui,-apple-system,"Segoe UI",sans-serif;background:#f1f4ef;color:#17302c;margin:0;padding:24px}
main{max-width:860px;margin:0 auto}
h1{font-size:25px;margin:0 0 4px} .sub{color:#66736f;margin-bottom:18px}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(330px,1fr));gap:16px}
section{background:#fff;border-radius:16px;padding:22px;box-shadow:0 3px 18px rgba(0,0,0,.07)}
h2{font-size:18px;margin:0 0 10px} table{width:100%;border-collapse:collapse}
td{padding:7px 0;vertical-align:top} td.k{width:100px;color:#66736f}
code{background:#eef2ee;border-radius:6px;padding:2px 6px;font-size:12px;word-break:break-all}
a{color:#0d6b60;font-weight:600} .tip{margin-top:14px;border-top:1px solid #e3e8e3;padding-top:12px;color:#66736f;font-size:13px;line-height:1.6}
.unavailable{color:#9a4f32}
"""


def render_card(title, manifest, download_label, download_path, manifest_path, package_note):
    if manifest is None:
        return f'<section><h2>{html.escape(title)}</h2><p class="unavailable">清单或发布包暂不可用</p></section>'
    version = html.escape(str(manifest.get("versionName", manifest.get("version", ""))))
    code = manifest.get("versionCode", "")
    size = int(manifest["size"])
    sha = html.escape(str(manifest["sha256"]))
    notes = html.escape(str(manifest.get("notes", "")))
    return f'''<section>
<h2>{html.escape(title)}</h2>
<table>
<tr><td class="k">版本</td><td><strong>{version}</strong>（{html.escape(str(code))}）</td></tr>
<tr><td class="k">大小</td><td>{size:,} 字节</td></tr>
<tr><td class="k">SHA-256</td><td><code>{sha}</code></td></tr>
<tr><td class="k">说明</td><td>{notes}</td></tr>
<tr><td class="k">下载</td><td><a href="{download_path}">{html.escape(download_label)}</a></td></tr>
<tr><td class="k">清单</td><td><a href="{manifest_path}">{manifest_path}</a></td></tr>
</table>
<div class="tip">{html.escape(package_note)}</div>
</section>'''


def render_index(android, windows):
    android_card = render_card(
        "Android 客户端", android, "最新 APK", "/nightdesk/latest.apk",
        "/api/v1/android-update",
        "Android 客户端自动检查更新；下载后校验包名、版本、大小、SHA-256 和签名，再交由系统安装器确认。")
    windows_name = f"nightdesk-windows-{windows.get('version', 'latest')}.zip" if windows else "Windows ZIP"
    windows_card = render_card(
        "Windows 桌面端", windows, windows_name, "/nightdesk/windows/latest.zip",
        "/nightdesk/api/v1/windows-update",
        "Windows 客户端在启动时和每 6 小时自动检查并下载；安装前需确认，替换两个 EXE 后会重新启动。")
    return f'''<!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
<title>NightDesk 更新源</title><meta name="viewport" content="width=device-width,initial-scale=1">
<style>{STYLE}</style></head><body><main>
<h1>NightDesk 更新源</h1><div class="sub">Tailscale Serve 专网代理，不向公网开放</div>
<div class="cards">{android_card}{windows_card}</div>
</main></body></html>'''


class Handler(BaseHTTPRequestHandler):
    server_version = "NightDeskFeed/2"
    protocol_version = "HTTP/1.1"

    def _send(self, code, body, ctype="text/plain; charset=utf-8", extra=None):
        extra = extra or {}
        if isinstance(body, str):
            body = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("Cache-Control", extra.get("cache", "no-store"))
        for k, v in extra.items():
            if k != "cache":
                self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def _serve_file(self, path, ctype, filename):
        try:
            st = path.stat()
            self.send_response(200)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(st.st_size))
            self.send_header("Content-Disposition", f'attachment; filename="{filename}"')
            self.send_header("Cache-Control", "public, max-age=3600")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Last-Modified", time.strftime("%a, %d %b %Y %H:%M:%S GMT", time.gmtime(st.st_mtime)))
            self.end_headers()
            if self.command == "HEAD":
                return
            with path.open("rb") as source:
                shutil.copyfileobj(source, self.wfile, length=1024 * 1024)
        except BrokenPipeError:
            pass

    def _error(self, code, message):
        self._send(code, json.dumps({"error": message}, ensure_ascii=False), "application/json; charset=utf-8")

    def _android_apk(self, m, code_qs=None):
        if code_qs is not None and code_qs != int(m["versionCode"]):
            self._error(409, f"清单已是 versionCode {int(m['versionCode'])}，请重新读取 Android 更新清单")
            return
        path = verified_apk(m)
        if path is None:
            self._error(503, "APK 与清单不一致或缺失")
            return
        self._serve_file(path, "application/vnd.android.package-archive", f"nightdesk-{m['versionName']}.apk")

    def _windows_package(self, m):
        path = verified_windows_package(m)
        if path is None:
            self._error(503, "Windows ZIP 与清单不一致或缺失")
            return
        self._serve_file(path, "application/zip", path.name)

    def do_GET(self):
        try:
            self._handle()
        except BrokenPipeError:
            pass

    def do_HEAD(self):
        self._handle()

    def _handle(self):
        parsed = urlparse(self.path)
        path = parsed.path
        qs = {k: v[-1] for k, v in parse_qs(parsed.query).items()}
        android = load_android_manifest()
        windows = load_windows_manifest()

        if path in ("/", "/nightdesk", "/nightdesk/"):
            page = render_index(android, windows)
            self._send(200, page, "text/html; charset=utf-8")
            return

        if path == "/healthz":
            healthy = android is not None and windows is not None
            state = f"android={'ok' if android else 'missing'} windows={'ok' if windows else 'missing'}"
            self._send(200 if healthy else 503, ("ok " if healthy else "degraded ") + state, "text/plain; charset=utf-8")
            return

        if path in ("/nightdesk/latest.apk", "/latest.apk"):
            if android is None:
                self._error(503, "Android 更新清单不可用")
            else:
                self._android_apk(android)
            return

        if (path.startswith("/nightdesk/nightdesk-") or path.startswith("/nightdesk-")) and path.endswith(".apk"):
            if android is None:
                self._error(503, "Android 更新清单不可用")
                return
            stem = path.split("/nightdesk-", 1)[-1][:-len(".apk")]
            target = ROOT / f"nightdesk-{stem}.apk"
            if not target.is_file():
                self._error(404, "没有该版本的 APK")
            else:
                self._serve_file(target, "application/vnd.android.package-archive", target.name)
            return

        if path in ("/api/v1/android-update", "/nightdesk/api/v1/android-update"):
            if android is None:
                self._error(503, "Android 更新清单不可用")
            else:
                self._send(200, json.dumps(android), "application/json; charset=utf-8")
            return

        if path in ("/api/v1/android-apk", "/nightdesk/api/v1/android-apk"):
            if android is None:
                self._error(503, "Android 更新清单不可用")
                return
            raw = qs.get("versionCode")
            code = int(raw) if raw and raw.isdigit() else None
            self._android_apk(android, code)
            return

        if path in ("/api/v1/windows-update", "/nightdesk/api/v1/windows-update"):
            if windows is None:
                self._error(503, "Windows 更新清单不可用")
            else:
                self._send(200, json.dumps(windows), "application/json; charset=utf-8")
            return

        if path in ("/nightdesk/windows/latest.zip", "/windows/latest.zip"):
            if windows is None:
                self._error(503, "Windows 更新清单不可用")
            else:
                self._windows_package(windows)
            return

        if (path.startswith("/nightdesk/windows/nightdesk-windows-") or path.startswith("/windows/nightdesk-windows-")) and path.endswith(".zip"):
            version = path.rsplit("nightdesk-windows-", 1)[-1][:-len(".zip")]
            if not VERSION_RE.match(version):
                self._error(400, "Windows 版本号无效")
                return
            target = ROOT / "windows" / f"nightdesk-windows-{version}.zip"
            if not target.is_file():
                self._error(404, "没有该版本的 Windows 包")
            else:
                self._serve_file(target, "application/zip", target.name)
            return

        if path in ("/api/v1/windows-package", "/nightdesk/api/v1/windows-package"):
            if windows is None:
                self._error(503, "Windows 更新清单不可用")
                return
            requested = qs.get("version", windows["version"])
            if requested != windows["version"]:
                self._error(409, f"清单已是 Windows {windows['version']}，请重新读取更新清单")
                return
            self._windows_package(windows)
            return

        self._error(404, "未知路径")

    def log_message(self, fmt, *args):
        sys.stderr.write(f"{self.address_string()} {fmt % args}\n")
        sys.stderr.flush()


def main():
    ThreadingHTTPServer.allow_reuse_address = True
    server = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    sys.stderr.write(f"nightdesk-update-feed listening on 127.0.0.1:{PORT} root={ROOT}\n")
    sys.stderr.flush()
    server.serve_forever()


if __name__ == "__main__":
    main()
