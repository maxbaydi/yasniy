# ЯСНЫЙ (`.яс`)

ЯСНЫЙ — самостоятельный язык с собственным компилятором, байткодом и VM.
Текущий toolchain полностью нативный (`native/yasn-native`, C#/.NET), без Python-зависимости.

Репозиторий включает:

- нативный компилятор и VM;
- модульную систему (`подключить`, `из ... подключить`, `как`, `экспорт`);
- нативный type checker;
- менеджер зависимостей проекта (`yasn deps`) с lock-файлом;
- форматы артефактов `.ybc` и `.yapp`;
- backend-режим (`yasn serve`, `yasn run dev/start`);
- тестовый раннер (`yasn test`);
- установку приложений как команд (`yasn install-app`).

## Быстрый запуск из исходников

```powershell
dotnet run --project native/yasn-native/yasn-native.csproj -- run examples/тест.яс
dotnet run --project native/yasn-native/yasn-native.csproj -- test
```

## Глобальная установка `yasn`

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install-global.ps1
yasn --help
```

Linux/macOS:

```bash
bash scripts/install-global.sh
yasn --help
```

## Примеры

```powershell
yasn run examples/тест.яс
yasn run examples/функции.яс
yasn run examples/модули.яс
yasn run examples/асинхронность.яс
yasn test
```

Backend из `yasn.toml`:

```powershell
yasn run dev
```

## Документация

- Индекс: `docs/index.md`
- Установка: `docs/installation.md`
- Быстрый старт: `docs/quickstart.md`
- Учебник: `docs/language-tutorial.md`
- Справочник языка: `docs/language-reference.md`
- CLI: `docs/cli-reference.md`
- Упаковка и запуск: `docs/packaging-and-run.md`
- Архитектура: `docs/architecture.md`
- Troubleshooting: `docs/troubleshooting.md`
- Нативный toolchain: `docs/native-toolchain.md`
