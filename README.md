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
- UI runtime-контракт (`/functions`, `/schema`, `/call`) и упаковку UI через `--ui-dist`;
- тестовый раннер (`yasn test`);
- установку приложений как команд (`yasn install-app`).

## Требования

Для установки из исходников нужен .NET SDK 10.0+:

```powershell
dotnet --version
```

## Быстрый запуск из исходников

```powershell
dotnet run --project native/yasn-native/yasn-native.csproj -- run examples/витрина.яс
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
yasn run examples/витрина.яс
yasn test
```

Backend из `yasn.toml`:

```powershell
yasn run dev
```

## Расширение VS Code

Подсветка синтаксиса `.яс` в `extensions/vscode-yasniy`. Установка: скопировать папку в `%USERPROFILE%\.vscode\extensions\` (Windows) или `~/.vscode/extensions/` (Linux/macOS), затем перезапустить VS Code.

## UI-пакеты

Для фронтенда приложений: `packages/ui-sdk`, `packages/ui-kit`. Подключение через `file:../../packages/ui-sdk` в `package.json`. Подробнее: `packages/README.md`.

## Документация

- Индекс: `docs/index.md`
- Установка: `docs/installation.md`
- Быстрый старт: `docs/quickstart.md`
- Учебник: `docs/language-tutorial.md`
- Справочник языка: `docs/language-reference.md`
- CLI: `docs/cli-reference.md`
- Упаковка и запуск: `docs/packaging-and-run.md`
- UI-контракт и runtime: `docs/ui-contract.md`
- Архитектура: `docs/architecture.md`
- Troubleshooting: `docs/troubleshooting.md`
- Нативный toolchain: `docs/native-toolchain.md`
