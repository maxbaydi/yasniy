# ЯСНЫЙ (`.яс`)

ЯСНЫЙ — строготипизированный язык с русскоязычным синтаксисом и отступами в стиле Python.

Репозиторий содержит:

- компилятор и типизатор;
- модульную систему (`подключить`, `из ... подключить`, `как`, `экспорт`);
- генерацию байткода (`.ybc`);
- виртуальную машину;
- упаковку приложений (`.yapp`) и установку консольных команд.

## Быстрый запуск

```powershell
python -m yasn run examples/тест.яс
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
# fullstack demo:
cd examples
yasn run dev
```

## Backend + Frontend одной командой

```toml
# yasn.toml
[run]
backend = "backend/main.яс"
host = "127.0.0.1"
port = 8000

[run.dev]
frontend = "npm run dev"
frontend_cwd = "frontend"
```

```powershell
yasn run dev
# или
yasn start
```

