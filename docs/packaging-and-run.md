# Упаковка, установка и запуск приложений

## 1. Артефакты

- `.яс` — исходный код
- `.ybc` — байткод
- `.yapp` — контейнер приложения (метаданные + байткод)

## 2. Когда использовать `.ybc`

Используйте `.ybc`, если хотите отделить компиляцию от выполнения:

```powershell
yasn build app.яс -o app.ybc
yasn exec app.ybc
```

## 3. Когда использовать `.yapp`

Используйте `.yapp`, если нужен единый переносимый файл приложения:

```powershell
yasn pack app.яс -o app.yapp --name app_name
yasn run-app app.yapp
```

`pack` выполняет полный compile pipeline, включая модульный резолвинг.

## 4. Установка как команды

```powershell
yasn install-app app.яс --name app_name
```

Создаются:

- `%APPDATA%\yasn\apps\app_name.yapp`
- `%APPDATA%\yasn\bin\app_name.cmd`

Launcher использует `yasn run-app ...` с fallback на `py -m yasn`, затем `python -m yasn`.

## 5. Обновление приложения

Повторно выполните `install-app` с тем же `--name`.

## 6. Удаление

Удалите вручную:

- `%APPDATA%\yasn\apps\app_name.yapp`
- `%APPDATA%\yasn\bin\app_name.cmd`

## 7. PATH для пользовательских команд

Путь до каталога launcher:

```powershell
yasn paths --short
```

Добавьте его в PATH, если команда `app_name` не распознается.

