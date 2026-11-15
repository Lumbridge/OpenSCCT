"""Loopback-only prototype host, with local browser console capture."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import threading

parser = argparse.ArgumentParser()
parser.add_argument("--directory", type=Path, required=True)
parser.add_argument("--port", type=int, default=8877)
parser.add_argument("--evidence", type=Path, default=Path("local/browser-evidence"))
args = parser.parse_args()
args.evidence.mkdir(parents=True, exist_ok=True)
lock = threading.Lock()

class Handler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, ".wasm": "application/wasm"}

    def do_POST(self):
        if self.path != "/__log":
            self.send_error(404)
            return
        size = int(self.headers.get("Content-Length", 0))
        if size > 65536:
            self.send_error(413)
            return
        record = json.loads(self.rfile.read(size))
        with lock, (args.evidence / "console.ndjson").open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(record) + "\n")
        self.send_response(204)
        self.end_headers()

    def log_message(self, format, *values):
        if self.command != "POST":
            super().log_message(format, *values)

print(f"OpenSCCT prototype: http://127.0.0.1:{args.port}/", flush=True)
ThreadingHTTPServer(("127.0.0.1", args.port), partial(Handler, directory=str(args.directory.resolve()))).serve_forever()
