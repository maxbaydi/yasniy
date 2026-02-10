from __future__ import annotations

import json
import traceback
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from .backend_core import BackendKernel
from .diagnostics import YasnError


def serve_backend(source_path: str, host: str = "127.0.0.1", port: int = 8000) -> None:
    backend = BackendKernel.from_file(source_path)
    source = str(Path(source_path).resolve())

    class Handler(BaseHTTPRequestHandler):
        server_version = "yasn/0.2"

        def do_OPTIONS(self) -> None:  # noqa: N802
            self._send_cors_preflight()

        def do_GET(self) -> None:  # noqa: N802
            self._dispatch()

        def do_POST(self) -> None:  # noqa: N802
            self._dispatch()

        def _dispatch(self) -> None:
            try:
                path = self.path.split("?", 1)[0]

                if path == "/health":
                    self._send_ok({"status": "ok", "source": source})
                    return

                if path == "/functions":
                    self._send_ok({"functions": backend.list_functions()})
                    return

                if path == "/call" and self.command == "POST":
                    body = self._read_json()
                    fn_name = body.get("function")
                    args = body.get("args", [])
                    reset_state = body.get("reset_state", False)

                    if not isinstance(fn_name, str) or not fn_name.strip():
                        self._send_error(400, "invalid_request", "Field 'function' must be a non-empty string")
                        return
                    if not isinstance(args, list):
                        self._send_error(400, "invalid_request", "Field 'args' must be a list")
                        return
                    if not isinstance(reset_state, bool):
                        self._send_error(400, "invalid_request", "Field 'reset_state' must be boolean")
                        return

                    result = backend.call(fn_name, args=args, reset_state=reset_state)
                    self._send_ok({"result": result})
                    return

                self._send_error(404, "not_found", f"Route not found: {path}")
            except YasnError as exc:
                self._send_error(500, "runtime_error", str(exc))
            except Exception as exc:  # noqa: BLE001
                traceback.print_exc()
                self._send_error(500, "handler_crash", f"Unhandled request error: {exc}")

        def _send_ok(self, data: object) -> None:
            payload = {"ok": True, "data": data}
            self._send_json(200, payload)

        def _send_error(self, status: int, code: str, message: str) -> None:
            payload = {"ok": False, "error": {"code": code, "message": message}}
            self._send_json(status, payload)

        def _send_json(self, status: int, payload: object) -> None:
            raw = json.dumps(payload, ensure_ascii=False, default=str).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(raw)))
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Request-Id")
            self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            self.end_headers()
            if self.command != "HEAD" and status != 204:
                self.wfile.write(raw)

        def _send_cors_preflight(self) -> None:
            self.send_response(204)
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Request-Id")
            self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            self.send_header("Content-Length", "0")
            self.end_headers()

        def _read_json(self) -> dict:
            raw_len = self.headers.get("Content-Length", "0")
            try:
                length = int(raw_len)
            except ValueError:
                length = 0
            body = self.rfile.read(length) if length > 0 else b""
            if not body:
                return {}
            return json.loads(body)

        def log_message(self, _fmt: str, *_args: object) -> None:
            return

    server = ThreadingHTTPServer((host, int(port)), Handler)
    print(f"[yasn] Backend started: http://{host}:{port}")
    print(f"[yasn] Source: {source}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[yasn] Stopping backend...")
    finally:
        server.server_close()
