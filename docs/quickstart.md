# Быстрый старт

Этот сценарий проходит полный путь: исходник -> проверка -> запуск -> байткод -> приложение.

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

Ожидаемый вывод:

```text
Проверка пройдена: ошибок не найдено.
```

## 3. Запуск

```powershell
yasn run hello.яс
```

## 4. Сборка байткода

```powershell
yasn build hello.яс -o hello.ybc
```

## 5. Запуск байткода

```powershell
yasn exec hello.ybc
```

## 6. Упаковка приложения

```powershell
yasn pack hello.яс -o hello.yapp --name hello_app
yasn run-app hello.yapp
```

## 7. Установка как консольной команды

```powershell
yasn install-app hello.яс --name hello
hello
```

## 8. Примеры в репозитории

```powershell
yasn run examples/тест.яс
yasn run examples/функции.яс
yasn run examples/модули.яс
yasn run examples/алиасы_и_namespace.яс
yasn run examples/типы_и_циклы.яс
```

## 9. Установка зависимостей проекта (если есть)

Если в `yasn.toml` указана секция `[dependencies]`, установите зависимости:

```powershell
yasn deps
```

После установки модули из `.yasn/deps` доступны для `подключить "..."`
без ручного добавления путей в `modules.paths`.

Проверить состояние (включая транзитивные зависимости из lock-файла):

```powershell
yasn deps list --all
```

## 10. Если глобальная команда не настроена

Запускайте через модуль Python:

```powershell
python -m yasn run hello.яс
```

## 11. Запуск backend одной командой

Создайте `yasn.toml` в корне проекта:

```toml
[run]
backend = "backend/main.яс"
host = "127.0.0.1"
port = 8000
```

Теперь можно запускать backend одной командой:

```powershell
yasn run dev
```
