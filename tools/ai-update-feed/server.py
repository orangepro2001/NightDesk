#!/usr/bin/env python3
"""NightDesk Android update feed.

Streams the signed APK and matching manifest for the Android client.
Tailscale Serve keeps the endpoint tailnet-only; this process itself
accepts no credentials and only listens on loopback.
"""
import hashlib
import html
import json
import os
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

ROOT = Path(os.environ.get("NIGHTDESK_FEED_DIR", Path(__file__).resolve().parent))
PORT = int(os.environ.get("NIGHTDESK_FEED_PORT", "17768"))
MANIFEST_PATH = ROOT / "manifest.json"
PACKAGE_NAME = "dev.nightdesk.android"

_cache = {"key": None, "sha": None}


def load_manifest():
    try:
        m = json.loads(MANIFEST_PATH.read_text("utf-8"))
        int(m["versionCode"]); int(m["size"])
        f"{m['versionName']}"
        m["sha256"].lower(); m["certificateSha256"].lower()
        return m
    except Exception:
        return None


def expected_apk_path(m, code):
    return ROOT / f"nightdesk-{code}.apk"


def verified_apk(m):
    code = int(m["versionCode"])
    path = expected_apk_path(m, code)
    if not path.is_file():
        return None
    st = path.stat()
    key = (str(path), st.st_mtime_ns, st.st_size)
    if _cache["key"] != key:
        digest = hashlib.sha256()
        with path.open("rb") as fh:
            for chunk in iter(lambda: fh.read(1024 * 1024), b""):
                digest.update(chunk)
        _cache["key"] = key
        _cache["sha"] = digest.hexdigest()
    if st.st_size != int(m["size"]) or _cache["sha"] != str(m["sha256"]).lower():
        return None
    return path


def apk_http_path(code):
    return f"/api/v1/android-apk?versionCode={code}"

INDEX_HTML = """<!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
<title>NightDesk Android 更新源</title>
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>
body{{font-family:system-ui,sans-serif;background:#f3f5f1;color:#17302c;margin:0;padding:24px}}
main{{max-width:640px;margin:0 auto;background:#fff;border-radius:14px;padding:22px 24px;box-shadow:0 2px 14px rgba(0,0,0,.06)}}
h1{{font-size:22px;margin:0 0 4px}} small{{color:#66736f}}
table{{width:100%;border-collapse:collapse;margin-top:14px}}
td{{padding:6px 0;vertical-align:top}}
td.k{{width:110px;color:#66736f}}
code{{background:#eef2ee;border-radius:6px;padding:2px 6px;font-size:13px;word-break:break-all}}
a{{color:#0d6b60}}
div.tip{{margin-top:16px;border-top:1px solid #e3e8e3;padding-top:12px;color:#66736f;font-size:13px;line-height:1.6}}
</style></head><body><main>
<h1>NightDesk Android 更新源</h1><small>由 Tailscale Serve 在专网内代理，不向公网开放</small>
<table>
<tr><td class="k">版本</td><td><strong>{version}</strong>（versionCode {code}）</td></tr>
<tr><td class="k">大小</td><td>{size:,} 字节</td></tr>
<tr><td class="k">SHA-256</td><td><code>{sha}</code></td></tr>
<tr><td class="k">说明</td><td>{notes}</td></tr>
<tr><td class="k">下载</td><td><a href="/nightdesk/latest.apk">最新 APK（nightdesk-{version}.apk）</a></td></tr>
<tr><td class="k">清单</td><td><a href="/api/v1/android-update">/api/v1/android-update</a></td></tr>
</table>
<div class="tip">NightDesk Android 客户端自动检查此地址的 manifest；下载后按包名、版本、minSdk、大小、SHA-256、签名指纹校验并交由系统安装器确认，不会静默安装。</div>
</main></body></html>
"""


class Handler(BaseHTTPRequestHandler):
    server_version = "NightDeskFeed/1"
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
        for k, v in (extra or {}).items():
            if k not in ("cache",):
                self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def _error(self, code, message):
        self._send(code, json.dumps({"error": message}), "application/json; charset=utf-8")

    def _serve_apk(self, m, filename, code_qs):
        if code_qs is not None and code_qs != int(m["versionCode"]):
            self._error(409, f"清单已是 versionCode {int(m['versionCode'])}，请重新读取 /api/v1/android-update")
            return
        path = verified_apk(m)
        if path is None:
            self._error(503, "APK 与清单不一致或缺失，更新源暂不可用")
            return
        body = path.read_bytes()
        try:
            self._send(200, body, "application/vnd.android.package-archive", {
                "cache": "public, max-age=3600",
                "Content-Disposition": f'attachment; filename="{filename(int(m["versionCode"]))}"',
                "Last-Modified": time.strftime("%a, %d %b %Y %H:%M:%S GMT", time.gmtime(path.stat().st_mtime)),
            })
        except BrokenPipeError:
            pass

    def do_GET(self):
        try:
            self._handle(head=False)
        except BrokenPipeError:
            pass

    def do_HEAD(self):
        self._handle(head=True)

    def _handle(self, head):
        parse = urlparse(self.path)
        path = parse.path
        qs = {k: v[-1] for k, v in parse_qs(parse.query).items()}
        m = load_manifest()

        if path in ("/", "/nightdesk", "/nightdesk/"):
            if m is None:
                self._send(204, b"")
                return
            code = int(m["versionCode"])
            page = INDEX_HTML.format(
                version=html.escape(m["versionName"]), code=code,
                size=int(m["size"]), sha=m["sha256"],
                notes=html.escape(m.get("notes", "")))
            self._send(200, page, "text/html; charset=utf-8", {"cache": "no-store"})
            return

        if path == "/healthz":
            code = 200 if m is not None else 503
            self._send(code, "ok" if m is not None else "no manifest", "text/plain; charset=utf-8")
            return

        if path in ("/nightdesk/latest.apk", "/latest.apk"):
            if m is None:
                self._send(204, b""); return
            self._serve_apk(m, lambda c: f"nightdesk-{m['versionName']}.apk", None)
            return

        if (path.startswith("/nightdesk/nightdesk-") or path.startswith("/nightdesk-")) and path.endswith(".apk"):
            if m is None:
                self._send(204, b""); return
            stem = path.split("/nightdesk-", 1)[-1][:-len(".apk")]
            target_apk = ROOT / f"nightdesk-{stem}.apk"
            if not target_apk.is_file():
                self._error(404, "没有该版本的 APK")
                return
            body = target_apk.read_bytes()
            self._send(200, body, "application/vnd.android.package-archive",
                       {"cache": "public, max-age=3600",
                        "Content-Disposition": f'attachment; filename="{target_apk.name}"'})
            return

        if path == "/api/v1/android-update":
            if m is None:
                self._send(204, b"")
                return
            self._send(200, json.dumps(m), "application/json; charset=utf-8")
            return

        if path == "/api/v1/android-apk":
            if m is None:
                self._send(204, b"")
                return
            raw = qs.get("versionCode")
            code_qs = int(raw) if raw and raw.isdigit() else None
            self._serve_apk(m, lambda c: f"nightdesk-{m['versionName']}.apk", code_qs)
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