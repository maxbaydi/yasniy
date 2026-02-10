# ЯСНЫЙ (`.яс`)

ЯСНЫЙ — строготипизированный язык с русскоязычным синтаксисом и отступами в стиле Python.

Репозиторий содержит:

- компилятор и типизатор;
- модульную систему (`подключить`, `из ... подключить`, `как`, `экспорт`);
- зависимости проекта через `yasn.toml` (`yasn deps`, включая транзитивные и lock-файл);
- генерацию байткода (`.ybc`);
- виртуальную машину;
- асинхронные задачи VM (`асинхронная функция`, `ждать`, `отменить`);
- backend-ядро (`yasny/backend_core.py`);
- HTTP-сервер (`yasn serve`);
- упаковку приложений (`.yapp`) и установку консольных команд.

## Быстрый запуск

```powershell
python -m yasn run examples/тест.яс
python -m yasn deps
python -m yasn deps list --all
```

## Глобальная установка команды `yasn`

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install-global.ps1
yasn --help
```

## Документация

- Индекс: `docs/index.md`
- Установка: `docs/installation.md`
- Быстрый старт: `docs/quickstart.md`
- Обучение языку: `docs/language-tutorial.md`
- Формальный справочник: `docs/language-reference.md`
- CLI: `docs/cli-reference.md`
- Упаковка и запуск: `docs/packaging-and-run.md`
- Troubleshooting: `docs/troubleshooting.md`
- Архитектура: `docs/architecture.md`

## Примеры

```powershell
yasn run examples/тест.яс
yasn run examples/функции.яс
yasn run examples/модули.яс
yasn run examples/алиасы_и_namespace.яс
yasn run examples/типы_и_циклы.яс
yasn run examples/асинхронность.яс
```

## Backend сервер

```toml
# yasn.toml
[run]
backend = "backend/main.яс"
host = "127.0.0.1"
port = 8000
```

```powershell
yasn run dev
# или
yasn serve backend/main.яс --host 127.0.0.1 --port 8000
```
