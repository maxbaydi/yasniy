# CLI справочник `yasn`

Показать встроенную справку:

```powershell
yasn --help
```

Общий вид:

```text
yasn <команда> [аргументы]
```

## `run`

Запуск `.яс` файла:

```powershell
yasn run app.яс
```

Запуск проектных режимов:

```powershell
yasn run dev
yasn run start
```

Опции для `dev/start`:

```text
--backend <path>
--host <host>
--port <port>
```

## `dev` и `start`

Короткие команды, эквивалентные `yasn run dev` и `yasn run start`:

```powershell
yasn dev --backend backend/main.яс --port 8000
yasn start
```

## `serve`

Запуск HTTP backend поверх файла ЯСНЫЙ:

```powershell
yasn serve backend/main.яс --host 127.0.0.1 --port 8000
```

HTTP API:

- `GET /health`
- `GET /functions`
- `POST /call`

Пример запроса `POST /call`:

```json
{
  "function": "сумма",
  "args": [2, 3],
  "reset_state": true
}
```

## `check`

Проверка исходника (синтаксис + модульный резолвинг + type checker):

```powershell
yasn check app.яс
```

## `test`

Запуск тестов `.яс`:

```powershell
yasn test
yasn test tests
yasn test tests --pattern "*_test.яс"
yasn test --fail-fast --verbose
```

По умолчанию ищет шаблоны:

- `*_test.яс`
- `*.test.яс`

Если передан файл, запускается только он.

## `build`

Компиляция в `.ybc`:

```powershell
yasn build app.яс
yasn build app.яс -o out.ybc
```

## `exec`

Запуск `.ybc`:

```powershell
yasn exec out.ybc
```

## `pack`

Упаковка в `.yapp`:

```powershell
yasn pack app.яс
yasn pack app.яс -o app.yapp
yasn pack app.яс --name my_app
```

Без `--name` имя берётся из `yasn.toml` (`[app].name`, затем `[app].displayName`, затем имя файла).

## `run-app`

Запуск `.yapp`:

```powershell
yasn run-app app.yapp
```

## `install-app`

Установка `.яс` приложения как пользовательской команды:

```powershell
yasn install-app app.яс --name my_app
```

Без `--name` команда берётся из `yasn.toml` (`[app].name`, затем `[app].displayName`, затем имя файла).

Windows создаёт:

- `%LOCALAPPDATA%\yasn\apps\my_app.yapp`
- `%LOCALAPPDATA%\yasn\bin\my_app.cmd`
- `%LOCALAPPDATA%\yasn\bin\my_app` (Git Bash/MSYS2)

Linux/macOS создаёт:

- `~/.yasn/apps/my_app.yapp`
- `~/.yasn/bin/my_app`

## `paths`

Показ рабочих каталогов:

```powershell
yasn paths
yasn paths --short
```

## `deps`

Управление секцией `[dependencies]` из `yasn.toml`.

Установка зависимостей:

```powershell
yasn deps
yasn deps install
yasn deps install --clean
```

Проверка статуса:

```powershell
yasn deps list
yasn deps list --all
```

Поддерживаемые источники:

- `git+https://...repo.git#v1.2.3`
- `path:../relative/or/absolute/path`
- `../relative/or/absolute/path` (шорткат для `path:`)

Lock-файл хранится в `.yasn/deps.lock.json`.

## `version`

```powershell
yasn version
```

## Коды возврата

- `0` — успех
- `1` — ошибка выполнения/компиляции/тестов
- `2` — ошибка аргументов CLI

## UI Contract Addendum (2026-02-11)

Backend/UI contract endpoints:

- `GET /functions`
- `GET /schema`
- `POST /call`

`/schema` returns function signatures and types for UI auto-form generation.

### `pack` with UI

```powershell
yasn pack app.яс -o app.yapp --ui-dist ui/dist
```

### `run-app` web runtime mode

If `.yapp` includes UI assets, `run-app` serves static files and API under `/api/*`.

```powershell
yasn run-app app.yapp --host 127.0.0.1 --port 8080
```

### `install-app` with UI

```powershell
yasn install-app app.яс --name my_app --ui-dist ui/dist
```
