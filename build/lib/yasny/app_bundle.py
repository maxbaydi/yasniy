from __future__ import annotations

import json
import os
import struct
from dataclasses import dataclass
from pathlib import Path

from .bc import decode_program, encode_program
from .diagnostics import YasnyError


APP_MAGIC = b"YASNYAP1"
APP_VERSION = 1


@dataclass(slots=True)
class AppBundle:
    name: str
    version: int
    bytecode: bytes


def create_bundle(name: str, bytecode: bytes) -> bytes:
    meta = {"name": name, "version": APP_VERSION}
    meta_raw = json.dumps(meta, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    return (
        APP_MAGIC
        + struct.pack("<I", len(meta_raw))
        + meta_raw
        + struct.pack("<I", len(bytecode))
        + bytecode
    )


def read_bundle(blob: bytes, path: str | None = None) -> AppBundle:
    if len(blob) < len(APP_MAGIC) + 8:
        raise YasnyError("Файл приложения слишком короткий", path=path)
    if blob[: len(APP_MAGIC)] != APP_MAGIC:
        raise YasnyError("Некорректная сигнатура файла приложения", path=path)

    offset = len(APP_MAGIC)
    (meta_len,) = struct.unpack("<I", blob[offset : offset + 4])
    offset += 4
    if offset + meta_len + 4 > len(blob):
        raise YasnyError("Повреждён заголовок метаданных приложения", path=path)

    meta_raw = blob[offset : offset + meta_len]
    offset += meta_len
    try:
        meta = json.loads(meta_raw.decode("utf-8"))
    except Exception as exc:  # noqa: BLE001
        raise YasnyError(f"Не удалось разобрать метаданные приложения: {exc}", path=path) from exc

    (bytecode_len,) = struct.unpack("<I", blob[offset : offset + 4])
    offset += 4
    if offset + bytecode_len != len(blob):
        raise YasnyError("Некорректная длина байткода в приложении", path=path)

    name = str(meta.get("name", "app"))
    version = int(meta.get("version", 0))
    if version != APP_VERSION:
        raise YasnyError(
            f"Неподдерживаемая версия формата приложения: {version}, ожидается {APP_VERSION}",
            path=path,
        )

    return AppBundle(name=name, version=version, bytecode=blob[offset : offset + bytecode_len])


def decode_bundle_to_program(blob: bytes, path: str | None = None):
    bundle = read_bundle(blob, path=path)
    return bundle, decode_program(bundle.bytecode, path=path)


def user_home_dir() -> Path:
    appdata = os.environ.get("APPDATA")
    if appdata:
        return Path(appdata) / "yasn"
    return Path.home() / ".yasn"


def user_apps_dir() -> Path:
    return user_home_dir() / "apps"


def user_bin_dir() -> Path:
    return user_home_dir() / "bin"


def install_app_bundle(name: str, bundle_bytes: bytes) -> tuple[Path, Path]:
    apps_dir = user_apps_dir()
    bin_dir = user_bin_dir()
    apps_dir.mkdir(parents=True, exist_ok=True)
    bin_dir.mkdir(parents=True, exist_ok=True)

    app_path = apps_dir / f"{name}.yapp"
    cmd_path = bin_dir / f"{name}.cmd"

    app_path.write_bytes(bundle_bytes)
    cmd_path.write_text(_make_windows_cmd_launcher(name), encoding="utf-8")
    return app_path, cmd_path


def _make_windows_cmd_launcher(name: str) -> str:
    return (
        "@echo off\r\n"
        "setlocal\r\n"
        "set APP=%~dp0..\\apps\\"
        + name
        + ".yapp\r\n"
        "yasn run-app \"%APP%\" %*\r\n"
        "if %ERRORLEVEL% EQU 9009 py -m yasn run-app \"%APP%\" %*\r\n"
        "if %ERRORLEVEL% EQU 9009 py -m yasny run-app \"%APP%\" %*\r\n"
        "if %ERRORLEVEL% EQU 9009 python -m yasny run-app \"%APP%\" %*\r\n"
    )


def compile_to_bundle(program, app_name: str) -> bytes:
    bytecode = encode_program(program)
    return create_bundle(app_name, bytecode)
