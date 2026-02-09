# CLI справочник `yasn`

Проверить полный список:

```powershell
yasn --help
```

Fallback без PATH:

```powershell
python -m yasn --help
```

## Общий синтаксис

```text
yasn <команда> [аргументы]
```

## 1. `run`

Два режима:

1. Запуск файла `.яс`:

```powershell
yasn run app.яс
```

2. Запуск проектного режима:

```powershell
yasn run dev
yasn run start
```

`yasn run dev/start` читает `yasn.toml` (или legacy `yasny.toml`) и поднимает backend + опционально frontend.

## 2. `dev`

Прямой shortcut для режима разработки:

```powershell
yasn dev
```

То же самое, что `yasn run dev`.

## 3. `start`

Прямой shortcut для start-режима:

```powershell
yasn start
```

То же самое, что `yasn run start`.

## 4. `serve`

Запуск HTTP backend поверх ЯСНЫЙ файла:

```powershell
yasn serve backend/main.яс --host 127.0.0.1 --port 8000
```

HTTP API:

- `GET /health`
- `GET /functions`
- `POST /call`

Пример запроса:

```json
{
  "function": "сумма",
  "args": [2, 3],
  "reset_state": true
}
```

## 5. `check`

Проверка синтаксиса и типов:

```powershell
yasn check app.яс
```

## 6. `build`

Компиляция в `.ybc`:

```powershell
yasn build app.яс
yasn build app.яс -o out.ybc
```

## 7. `exec`

Запуск `.ybc`:

```powershell
yasn exec out.ybc
```

## 8. `pack`

Упаковка `.яс` в `.yapp`:

```powershell
yasn pack app.яс
yasn pack app.яс -o app.yapp
yasn pack app.яс --name my_app
```

## 9. `run-app`

Запуск `.yapp`:

```powershell
yasn run-app app.yapp
```

## 10. `install-app`

Установка программы как пользовательской команды:

```powershell
yasn install-app app.яс --name my_app
```

Создает:

- `%APPDATA%\yasn\apps\my_app.yapp`
- `%APPDATA%\yasn\bin\my_app.cmd`

## 11. `paths`

Показ внутренних каталогов:

```powershell
yasn paths
yasn paths --short
```

## 12. Конфиг `yasn.toml` для `dev/start`

```toml
[run]
backend = "backend/main.яс"
host = "127.0.0.1"
port = 8000

[run.dev]
frontend = "npm run dev"
frontend_cwd = "frontend"

[run.start]
frontend = "npm start"
frontend_cwd = "frontend"
```

После этого:

```powershell
yasn run dev
```

поднимет backend на ЯСНЫЙ и frontend dev-сервер одной командой.

## 13. Коды возврата

- `0` — успех
- `1` — ошибка компиляции/выполнения/конфигурации
- `2` — ошибка аргументов CLI
