#!/usr/bin/env python3
"""WatchForge UI static server (S21 produkcia) — Angular SPA + fallback na index.html.
Beží na porte 4200, servíruje dist/watchforgeclient. API je na localhost:5000
(UI volá cez API_BASE_URL http://localhost:5000 — CORS povolené).
"""
import http.server
import os
import sys
import urllib.request

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    "..", "..", "applications", "web", "WatchForge.UI", "dist", "watchforgeclient", "browser")
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 4200
API_UPSTREAM = os.environ.get("WF_API_UPSTREAM", "http://127.0.0.1:5000")

class SPAHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=ROOT, **kwargs)

    def translate_path(self, path):
        # SPA fallback: neexistujúca cesta → index.html
        full = super().translate_path(path)
        if os.path.isfile(full):
            return full
        return os.path.join(ROOT, "index.html")

    def do_GET(self):
        # Proxy /api/ → WatchForge API (žiadny CORS, funguje z ľubovoľného originu)
        if self.path.startswith("/api/"):
            self.proxy("GET")
            return
        super().do_GET()

    def do_POST(self):
        if self.path.startswith("/api/"):
            self.proxy("POST")
            return
        super().do_POST()

    def do_PUT(self):
        if self.path.startswith("/api/"):
            self.proxy("PUT")
            return
        super().do_PUT()

    def do_DELETE(self):
        if self.path.startswith("/api/"):
            self.proxy("DELETE")
            return
        super().do_DELETE()

    def proxy(self, method):
        """Prepošle požiadavku na WatchForge API a vráti odpoveď (streamovanie pre MJPEG)."""
        try:
            length = int(self.headers.get("Content-Length", 0) or 0)
            body = self.rfile.read(length) if length > 0 else None
            req = urllib.request.Request(
                API_UPSTREAM + self.path,
                data=body,
                method=method,
                headers={k: v for k, v in self.headers.items()
                         if k.lower() not in ("host", "connection", "accept-encoding")},
            )
            with urllib.request.urlopen(req, timeout=600) as resp:
                self.send_response(resp.status)
                for k, v in resp.headers.items():
                    if k.lower() in ("content-type", "content-length", "content-disposition", "content-range", "set-cookie"):
                        self.send_header(k, v)
                # MJPEG / video stream — bez Content-Length, chunked, streamujeme priebežne
                if resp.headers.get("Content-Type", "").startswith(("multipart/", "video/")):
                    self.send_header("Content-Type", resp.headers.get("Content-Type", "application/octet-stream"))
                    self.end_headers()
                    while True:
                        chunk = resp.read(64 * 1024)
                        if not chunk:
                            break
                        self.wfile.write(chunk)
                        self.wfile.flush()
                else:
                    data = resp.read()
                    self.send_header("Content-Length", str(len(data)))
                    self.end_headers()
                    self.wfile.write(data)
        except urllib.error.HTTPError as e:
            data = e.read()
            self.send_response(e.code)
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError) as e:
            # Klient (prehliadač) odpojil počas odpovede — bežné pri pollingu (nový frame
            # zruší starý request). Ticho skončiť, NIE posielať 502 — klient už nepočúva.
            sys.stderr.write("[web] client disconnected: %s\n" % e)
        except Exception as e:
            sys.stderr.write("[web] proxy error: %s\n" % e)
            try:
                self.send_response(502)
                self.send_header("Content-Type", "application/json")
                body = ('{"error":"proxy: %s"}' % e).encode()
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
            except Exception:
                pass

    def end_headers(self):
        # index.html: no-store — browser (hlavne Safari na mobile) musí vždy stiahnuť
        # čerstvý build s aktuálnymi hashed JS/CSS odkazmi; inak cacheol starý index.html
        # s odkazmi na staré bundly a live view ostane čierne.
        if self.path == "/" or self.path.endswith("index.html"):
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
        else:
            self.send_header("Cache-Control", "no-cache")
        super().end_headers()

    def log_message(self, format, *args):
        sys.stderr.write("[web] %s\n" % (format % args))

if __name__ == "__main__":
    if not os.path.isdir(ROOT):
        sys.stderr.write(f"UI dist nenájdený: {ROOT} (spusti bun run build najprv)\n")
        sys.exit(1)
    server = http.server.ThreadingHTTPServer(("0.0.0.0", PORT), SPAHandler)
    sys.stderr.write(f"WatchForge UI na http://0.0.0.0:{PORT} (root {ROOT}, api→{API_UPSTREAM})\n")
    server.serve_forever()
