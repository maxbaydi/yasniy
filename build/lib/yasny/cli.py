from __future__ import annotations

import argparse
import re
from pathlib import Path

from .app_bundle import (
    compile_to_bundle,
    decode_bundle_to_program,
    install_app_bundle,
    user_apps_dir,
    user_bin_dir,
)
from .bc import decode_program, encode_program
from .diagnostics import YasnyError
from .pipeline import check_program, compile_source, load_program, run_program
from .project_runner import run_mode
from .server import serve_backend


MODE_NAMES = {"dev", "start"}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="yasn",
        description="Компилятор и VM для языка ЯСНЫЙ (.яс)",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    run_cmd = sub.add_parser(
        "run",
        help="Запустить .яс или режим проекта (dev/start)",
    )
    run_cmd.add_argument("target", help="Путь к .яс файлу или режим: dev/start")
    run_cmd.add_argument("--backend", help="Backend entrypoint для режима dev/start")
    run_cmd.add_argument("--host", help="Хост backend-сервера для режима dev/start")
    run_cmd.add_argument("--port", type=int, help="Порт backend-сервера для режима dev/start")

    dev_cmd = sub.add_parser("dev", help="Запустить проект в dev режиме (backend + опционально frontend)")
    dev_cmd.add_argument("--backend", help="Backend entrypoint")
    dev_cmd.add_argument("--host", help="Хост backend-сервера")
    dev_cmd.add_argument("--port", type=int, help="Порт backend-сервера")

    start_cmd = sub.add_parser("start", help="Запустить проект в start режиме")
    start_cmd.add_argument("--backend", help="Backend entrypoint")
    start_cmd.add_argument("--host", help="Хост backend-сервера")
    start_cmd.add_argument("--port", type=int, help="Порт backend-сервера")

    serve_cmd = sub.add_parser("serve", help="Запустить HTTP backend поверх .яс")
    serve_cmd.add_argument("source", help="Путь к backend .яс файлу")
    serve_cmd.add_argument("--host", default="127.0.0.1", help="Хост (по умолчанию 127.0.0.1)")
    serve_cmd.add_argument("--port", type=int, default=8000, help="Порт (по умолчанию 8000)")

    check_cmd = sub.add_parser("check", help="Проверить синтаксис и типы .яс")
    check_cmd.add_argument("source", help="Путь к .яс файлу")

    build_cmd = sub.add_parser("build", help="Собрать .яс в .ybc")
    build_cmd.add_argument("source", help="Путь к .яс файлу")
    build_cmd.add_argument("-o", "--output", help="Путь к выходному .ybc")

    exec_cmd = sub.add_parser("exec", help="Выполнить .ybc")
    exec_cmd.add_argument("bytecode", help="Путь к .ybc файлу")

    pack_cmd = sub.add_parser("pack", help="Упаковать .яс в .yapp")
    pack_cmd.add_argument("source", help="Путь к .яс файлу")
    pack_cmd.add_argument("-o", "--output", help="Путь к выходному .yapp")
    pack_cmd.add_argument("--name", help="Имя приложения (по умолчанию имя файла)")

    run_app_cmd = sub.add_parser("run-app", help="Запустить .yapp")
    run_app_cmd.add_argument("app", help="Путь к .yapp файлу")

    install_app_cmd = sub.add_parser(
        "install-app",
        help="Установить .яс как консольное приложение в профиль пользователя",
    )
    install_app_cmd.add_argument("source", help="Путь к .яс файлу")
    install_app_cmd.add_argument("--name", help="Имя консольной команды")

    paths_cmd = sub.add_parser("paths", help="Показать пользовательские каталоги yasn")
    paths_cmd.add_argument(
        "--short",
        action="store_true",
        help="Печатать только каталог bin (удобно для PATH)",
    )

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        if args.command == "run":
            if args.target in MODE_NAMES:
                return run_mode(
                    mode=args.target,
                    backend=args.backend,
                    host=args.host,
                    port=args.port,
                )
            source_path = Path(args.target)
            source = source_path.read_text(encoding="utf-8")
            bytecode = compile_source(source, path=str(source_path))
            run_program(bytecode, path=str(source_path))
            return 0

        if args.command == "dev":
            return run_mode(mode="dev", backend=args.backend, host=args.host, port=args.port)

        if args.command == "start":
            return run_mode(mode="start", backend=args.backend, host=args.host, port=args.port)

        if args.command == "serve":
            serve_backend(source_path=args.source, host=args.host, port=args.port)
            return 0

        if args.command == "check":
            source_path = Path(args.source)
            source = source_path.read_text(encoding="utf-8")
            program = load_program(source, path=str(source_path))
            check_program(program, path=str(source_path))
            print("Проверка пройдена: ошибок не найдено.")
            return 0

        if args.command == "build":
            source_path = Path(args.source)
            source = source_path.read_text(encoding="utf-8")
            bytecode = compile_source(source, path=str(source_path))
            output_path = Path(args.output) if args.output else source_path.with_suffix(".ybc")
            output_path.write_bytes(encode_program(bytecode))
            print(f"Байткод сохранён: {output_path}")
            return 0

        if args.command == "exec":
            bytecode_path = Path(args.bytecode)
            data = bytecode_path.read_bytes()
            program = decode_program(data, path=str(bytecode_path))
            run_program(program, path=str(bytecode_path))
            return 0

        if args.command == "pack":
            source_path = Path(args.source)
            source = source_path.read_text(encoding="utf-8")
            program = compile_source(source, path=str(source_path))
            app_name = args.name or source_path.stem
            bundle = compile_to_bundle(program, app_name=app_name)
            output_path = Path(args.output) if args.output else source_path.with_suffix(".yapp")
            output_path.write_bytes(bundle)
            print(f"Приложение упаковано: {output_path}")
            return 0

        if args.command == "run-app":
            app_path = Path(args.app)
            bundle_raw = app_path.read_bytes()
            bundle, program = decode_bundle_to_program(bundle_raw, path=str(app_path))
            run_program(program, path=str(app_path))
            print(f"[yasn] Приложение '{bundle.name}' завершено.")
            return 0

        if args.command == "install-app":
            source_path = Path(args.source)
            source = source_path.read_text(encoding="utf-8")
            program = compile_source(source, path=str(source_path))
            name_raw = args.name or source_path.stem
            cmd_name = _sanitize_command_name(name_raw)
            bundle = compile_to_bundle(program, app_name=cmd_name)
            app_path, cmd_path = install_app_bundle(cmd_name, bundle)
            print(f"Приложение установлено: {app_path}")
            print(f"Лаунчер создан: {cmd_path}")
            print(f"Добавьте в PATH каталог: {user_bin_dir()}")
            return 0

        if args.command == "paths":
            if args.short:
                print(user_bin_dir())
                return 0
            print(f"apps: {user_apps_dir()}")
            print(f"bin:  {user_bin_dir()}")
            return 0

        parser.error("Неизвестная команда")
        return 2
    except YasnyError as exc:
        print(exc)
        return 1
    except FileNotFoundError as exc:
        print(f"ошибка: файл не найден: {exc.filename}")
        return 1
    except Exception as exc:  # noqa: BLE001
        print(f"ошибка выполнения: {exc}")
        return 1


def _sanitize_command_name(value: str) -> str:
    name = value.strip()
    if not name:
        raise YasnyError("Имя команды не может быть пустым")
    name = re.sub(r"\s+", "_", name)
    name = re.sub(r"[^\w\-]", "_", name, flags=re.UNICODE)
    if not name:
        raise YasnyError("После нормализации имя команды оказалось пустым")
    return name


if __name__ == "__main__":
    raise SystemExit(main())
