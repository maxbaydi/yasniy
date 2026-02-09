from __future__ import annotations

import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from .diagnostics import YasnyError
from .host_api import YasnyBackend


def serve_backend(source_path: str, host: str = "127.0.0.1", port: int = 8000) -> None:
    backend = YasnyBackend.from_file(source_path)
    source = str(Path(source_path).resolve())

    class Handler(BaseHTTPRequestHandler):
        server_version = "yasn/0.1"

        def _send_json(self, status: int, payload: dict[str, object]) -> None:
            body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Access-Control-Allow-Headers", "Content-Type")
            self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            self.end_headers()
            self.wfile.write(body)

        def _read_json(self) -> dict[str, object]:
            raw_len = self.headers.get("Content-Length", "0")
            try:
                length = int(raw_len)
            except ValueError:
                raise YasnyError("Некорректный Content-Length")
            raw = self.rfile.read(length) if length > 0 else b"{}"
            try:
                obj = json.loads(raw.decode("utf-8"))
            except Exception as exc:  # noqa: BLE001
                raise YasnyError(f"Некорректный JSON: {exc}") from exc
            if not isinstance(obj, dict):
                raise YasnyError("JSON body должен быть объектом")
            return obj

        def _path(self) -> str:
            return self.path.split("?", 1)[0]

        def do_OPTIONS(self) -> None:  # noqa: N802
            self._send_json(204, {})

        def do_GET(self) -> None:  # noqa: N802
            try:
                p = self._path()
                if p == "/health":
                    self._send_json(
                        200,
                        {
                            "ok": True,
                            "backend": source,
                        },
                    )
                    return
                if p == "/functions":
                    functions = sorted(backend.vm.program.functions.keys())
                    self._send_json(200, {"ok": True, "functions": functions})
                    return
                self._send_json(404, {"ok": False, "error": "Маршрут не найден"})
            except Exception as exc:  # noqa: BLE001
                self._send_json(500, {"ok": False, "error": f"Внутренняя ошибка: {exc}"})

        def do_POST(self) -> None:  # noqa: N802
            p = self._path()
            if p != "/call":
                self._send_json(404, {"ok": False, "error": "Маршрут не найден"})
                return
            try:
                body = self._read_json()
                fn = body.get("function")
                args = body.get("args", [])
                reset_state = body.get("reset_state", False)

                if not isinstance(fn, str) or not fn:
                    raise YasnyError("Поле 'function' должно быть непустой строкой")
                if not isinstance(args, list):
                    raise YasnyError("Поле 'args' должно быть списком")
                if not isinstance(reset_state, bool):
                    raise YasnyError("Поле 'reset_state' должно быть Лог")

                result = backend.call(fn, args=args, reset_state=reset_state)
                self._send_json(200, {"ok": True, "result": result})
            except YasnyError as exc:
                self._send_json(400, {"ok": False, "error": str(exc)})
            except Exception as exc:  # noqa: BLE001
                self._send_json(500, {"ok": False, "error": f"Внутренняя ошибка: {exc}"})

        def log_message(self, fmt: str, *args: object) -> None:
            try:
                client = str(self.client_address[0])
            except Exception:  # noqa: BLE001
                client = "-"
            try:
                message = fmt % args
            except Exception:  # noqa: BLE001
                message = fmt
            print(f"[yasn-http] {client} - {message}")

    server = ThreadingHTTPServer((host, int(port)), Handler)
    print(f"[yasn] Backend started: http://{host}:{port}")
    print(f"[yasn] Source: {source}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[yasn] Stopping backend...")
    finally:
        server.server_close()
