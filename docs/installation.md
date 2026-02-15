---
layout: default
title: Установка
---

# Установка `yasn`

## Быстрый выбор

- Если вы на Windows и хотите самый простой путь: используйте установщик из Releases.
- Если вы на Linux/macOS: используйте `scripts/install-global.sh`.
- Если не хотите глобальную установку: запускайте через `dotnet run` из исходников.

## Windows (рекомендуется)

1. Скачайте `Yasn-Setup-x.x.x.exe`:
   - https://github.com/maxbaydi/yasniy/releases
2. Запустите установщик.
3. Откройте новый терминал.
4. Проверьте установку:

```powershell
yasn --help
yasn version
```

Что делает установщик:
- добавляет `yasn` в PATH;
- размещает бинарники в `%LOCALAPPDATA%\Yasn\bin`;
- копирует `ui-sdk`/`ui-kit` в `%LOCALAPPDATA%\Yasn\packages\`.

## Linux/macOS

```bash
bash scripts/install-global.sh
yasn --help
yasn version
```

Если команда не находится, добавьте каталог с бинарником в PATH (например, `~/.local/bin`).

## Запуск из исходников (без установки)

Требуется `.NET SDK 10.0+`.

```powershell
dotnet run --project native/yasn-native/yasn-native.csproj -- --help
```

Запуск файла:

```powershell
dotnet run --project native/yasn-native/yasn-native.csproj -- run examples/витрина.яс
```

## UI-пакеты после установки

При установке через Windows installer доступны локальные пакеты UI:
- `%LOCALAPPDATA%/Yasn/packages/ui-sdk`
- `%LOCALAPPDATA%/Yasn/packages/ui-kit`

Пример подключения в `package.json`:

```json
{
  "dependencies": {
    "@yasn/ui-sdk": "file:%LOCALAPPDATA%/Yasn/packages/ui-sdk",
    "@yasn/ui-kit": "file:%LOCALAPPDATA%/Yasn/packages/ui-kit"
  }
}
```

## Что делать дальше

- Первый запуск за 5 минут: [quickstart.md](quickstart.md)
- Полный список команд: [cli-reference.md](cli-reference.md)
- Типовые проблемы: [troubleshooting.md](troubleshooting.md)
