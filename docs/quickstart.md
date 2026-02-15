привет

приветри

привет# Быстрый старт

Сценарий проходит путь: исходник -> проверка -> запуск -> тесты -> байткод -> приложение.

## 1. Создайте программу

`привет.яс`:

```text
функция main() -> Пусто:
    печать("Привет, ЯСНЫЙ!")
    вернуть пусто
```

## 2. Проверка

```powershell
yasn check привет.яс
```

## 3. Запуск

```powershell
yasn run привет.яс
```

## 4. Добавьте простой тест

`tests/привет_тест.яс`:

```text
функция main() -> Пусто:
    утверждать_равно(2 + 2, 4)
    вернуть пусто
```

Запуск тестов:

```powershell
yasn test
```

## 5. Сборка байткода

```powershell
yasn build привет.яс -o привет.ybc
```

## 6. Запуск байткода

```powershell
yasn exec привет.ybc
```

## 7. Упаковка приложения

```powershell
yasn pack привет.яс -o привет.yapp --name привет_приложение
yasn run-app привет.yapp
```

## 8. Установка как команды

```powershell
yasn install-app привет.яс --name привет
привет
```

## 9. Примеры из репозитория

```powershell
yasn run examples/тест.яс
yasn run examples/функции.яс
yasn run examples/модули.яс
yasn run examples/алиасы_и_namespace.яс
yasn run examples/асинхронность.яс
yasn test
```

## 10. Зависимости проекта

Если в `yasn.toml` есть `[dependencies]`:

```powershell
yasn deps
yasn deps list --all
```

## 11. Backend одной командой

`yasn.toml`:

```toml
[run]
backend = "backend/main.яс"
host = "127.0.0.1"
port = 8000
```

Запуск:

```powershell
yasn run dev
```

## 12. Запуск без глобальной установки

```powershell
dotnet run --project native/yasn-native/yasn-native.csproj -- run привет.яс
```

## 13. UI bundle and auto-generated forms

1. Build your frontend to `ui/dist`.
2. Pack app with embedded UI:

```powershell
yasn pack backend/main.яс -o app.yapp --ui-dist ui/dist
```

3. Run packaged app:

```powershell
yasn run-app app.yapp --port 8080
```

UI should call only:

- `GET /api/functions`
- `GET /api/schema`
- `POST /api/call`

See `docs/ui-contract.md` for response format and SDK usage.
