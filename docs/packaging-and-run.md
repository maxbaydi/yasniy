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

## 4. Установка как команды

```powershell
yasn install-app app.яс --name app_name
```

Windows:

- `%APPDATA%\yasn\apps\app_name.yapp`
- `%APPDATA%\yasn\bin\app_name.cmd`

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
