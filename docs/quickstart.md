# Быстрый старт

Сценарий проходит путь: исходник -> проверка -> запуск -> тесты -> байткод -> приложение.

## 1. Создайте программу

`hello.яс`:

```text
функция main() -> Пусто:
    печать("Привет, ЯСНЫЙ!")
    вернуть пусто
```

## 2. Проверка

```powershell
yasn check hello.яс
```

## 3. Запуск

```powershell
yasn run hello.яс
```

## 4. Добавьте простой тест

`tests/hello_test.яс`:

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
yasn build hello.яс -o hello.ybc
```

## 6. Запуск байткода

```powershell
yasn exec hello.ybc
```

## 7. Упаковка приложения

```powershell
yasn pack hello.яс -o hello.yapp --name hello_app
yasn run-app hello.yapp
```

## 8. Установка как команды

```powershell
yasn install-app hello.яс --name hello
hello
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
dotnet run --project native/yasn-native/yasn-native.csproj -- run hello.яс
```
