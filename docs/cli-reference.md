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

Windows создаёт:

- `%APPDATA%\yasn\apps\my_app.yapp`
- `%APPDATA%\yasn\bin\my_app.cmd`

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
