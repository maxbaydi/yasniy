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

`yasn run dev/start` читает `yasn.toml` (или legacy `yasny.toml`) и поднимает backend.

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

## 12. `deps`

Управление зависимостями из секции `[dependencies]` в `yasn.toml`/`yasny.toml`.

Установка зависимостей:

```powershell
yasn deps
# или
yasn deps install
```

Установка с очисткой локального кэша от неактуальных зависимостей:

```powershell
yasn deps install --clean
```

Просмотр статуса:

```powershell
yasn deps list
```

Показать также транзитивные зависимости из lock-файла:

```powershell
yasn deps list --all
```

Поддерживаемые источники:

- `git+https://...repo.git#v1.2.3`
- `path:../relative/or/absolute/path`
- `../relative/or/absolute/path` (как shorthand для `path:`)

Особенности:

- `yasn deps install` устанавливает прямые и транзитивные зависимости.
- При конфликте двух транзитивных зависимостей с одинаковым именем, но разным источником/версией, команда завершится ошибкой.
- Lock-файл сохраняется в `.yasn/deps.lock.json`.

## 13. Конфиг `yasn.toml` для `dev/start`

```toml
[run]
backend = "backend/main.яс"
host = "127.0.0.1"
port = 8000
```

После этого:

```powershell
yasn run dev
```

поднимет backend на ЯСНЫЙ одной командой.

## 14. Коды возврата

- `0` — успех
- `1` — ошибка компиляции/выполнения/конфигурации
- `2` — ошибка аргументов CLI
