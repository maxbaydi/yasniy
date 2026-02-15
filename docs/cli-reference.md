---
layout: default
title: CLI справочник
---

# CLI справочник `yasn`

Показать встроенную справку:

```powershell
yasn --help
```

Общий формат:

```text
yasn <команда> [аргументы]
```

## Базовые команды (ежедневный цикл)

### `run`

Запуск `.яс` файла:

```powershell
yasn run app.яс
```

Режимы проекта из `yasn.toml`:

```powershell
yasn run dev
yasn run start
```

Короткие эквиваленты:

```powershell
yasn dev
yasn start
```

### `check`

Проверка исходника (синтаксис + модульный резолвинг + type checker):

```powershell
yasn check app.яс
```

### `test`

Запуск тестов:

```powershell
yasn test
yasn test tests
yasn test tests --pattern "*_test.яс"
yasn test --fail-fast --verbose
```

### `build` и `exec`

Компиляция в `.ybc` и запуск:

```powershell
yasn build app.яс -o app.ybc
yasn exec app.ybc
```

## Backend и web runtime

### `serve`

Запуск HTTP backend поверх файла ЯСНЫЙ:

```powershell
yasn serve backend/main.яс --host 127.0.0.1 --port 8000
```

Базовые endpoint'ы:
- `GET /health`
- `GET /functions`
- `GET /schema`
- `POST /call`

### `pack` и `run-app`

Упаковка в `.yapp`:

```powershell
yasn pack app.яс -o app.yapp
```

Запуск собранного приложения:

```powershell
yasn run-app app.yapp
```

Если в `.yapp` встроен UI (`--ui-dist`), web runtime поднимает:
- статические файлы UI;
- API под `/api/*` (`/api/functions`, `/api/schema`, `/api/call`).

## Установка приложения как команды

### `install-app`

```powershell
yasn install-app app.яс --name my_app
```

После установки команда `my_app` запускает соответствующий `.yapp`.

## Зависимости проекта

### `deps`

```powershell
yasn deps
yasn deps install
yasn deps install --clean
yasn deps list
yasn deps list --all
```

Поддерживаемые источники:
- `git+https://...repo.git#tag`
- `path:../local/path`
- `../local/path` (шорткат для `path:`)

Lock-файл: `.yasn/deps.lock.json`.

## Служебные команды

### `paths`

```powershell
yasn paths
yasn paths --short
```

### `version`

```powershell
yasn version
```

## Типовые сценарии

### Сценарий 1: «написал и запустил»

```powershell
yasn check app.яс
yasn run app.яс
```

### Сценарий 2: «прогнал тесты перед коммитом»

```powershell
yasn test
```

### Сценарий 3: «собрал приложение с UI»

```powershell
yasn pack backend/main.яс -o app.yapp --ui-dist ui/dist
yasn run-app app.yapp
```

## Коды возврата

- `0`: успешно
- `1`: ошибка выполнения/компиляции/тестов
- `2`: ошибка аргументов CLI

## Связанные документы

- [quickstart.md](quickstart.md)
- [packaging-and-run.md](packaging-and-run.md)
- [ui-contract.md](ui-contract.md)
