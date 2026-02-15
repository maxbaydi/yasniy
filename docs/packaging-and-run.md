---
layout: default
title: Упаковка, установка и запуск приложений
---

# Упаковка, установка и запуск приложений

## 1. Артефакты

- `.яс` — исходный код
- `.ybc` — байткод
- `.yapp` — контейнер приложения (метаданные + байткод)

## 2. Когда использовать `.ybc`

Если хотите разделить компиляцию и выполнение:

```powershell
yasn build app.яс -o app.ybc
yasn exec app.ybc
```

## 3. Когда использовать `.yapp`

Если нужен переносимый контейнер приложения:

```powershell
yasn pack app.яс -o app.yapp --name app_name
yasn run-app app.yapp
```

`pack` выполняет полный pipeline, включая модульный резолвинг.

Если `--name` не указан, `pack`/`install-app` берут метаданные из `yasn.toml`:

```toml
[app]
name = "calculator-yasn"
displayName = "Калькулятор"
description = "Тестовый калькулятор на языке Ясный"
version = "0.6.0"
publisher = "Max Bay"
```

`name` используется как техническое имя (в т.ч. для команды `install-app`), `displayName` — как отображаемое имя приложения.
`version` валидируется и поддерживает:
- число: `1`, `1.2`
- semver-строку: `"1.2.3"`

## 4. Установка как команды

```powershell
yasn install-app app.яс --name app_name
```

Windows:

- `%LOCALAPPDATA%\yasn\apps\app_name.yapp`
- `%LOCALAPPDATA%\yasn\bin\app_name.cmd`
- `%LOCALAPPDATA%\yasn\bin\app_name` (Git Bash/MSYS2)

Linux/macOS:

- `~/.yasn/apps/app_name.yapp`
- `~/.yasn/bin/app_name`

Launcher использует `yasn run-app`.

## 5. Обновление приложения

Повторно выполните `install-app` с тем же `--name`.

## 6. Удаление приложения

Удалите вручную файлы launcher и `.yapp` из каталогов выше.

## 7. PATH для пользовательских команд

Путь launcher-каталога:

```powershell
yasn paths --short
```

Добавьте этот путь в PATH, если команда `app_name` не находится.

## 8. UI bundle (`--ui-dist`) and web runtime

Embed built frontend assets into `.yapp`:

```powershell
yasn pack app.яс -o app.yapp --ui-dist ui/dist
```

Run packaged app in web runtime mode:

```powershell
yasn run-app app.yapp --host 127.0.0.1 --port 8080
```

In this mode:

- static UI files are served from embedded dist;
- backend kernel API is available under `/api/*`;
- stable UI endpoints are `/api/functions`, `/api/schema`, `/api/call`.

Install command with embedded UI:

```powershell
yasn install-app app.яс --name app_name --ui-dist ui/dist
```
