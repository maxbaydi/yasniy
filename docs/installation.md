# Установка `yasn`

Ниже описан нативный способ установки компилятора/рантайма ЯСНЫЙ без Python.

## 1. Требования

Для сборки из исходников нужен .NET SDK 10.0+.

Проверка:

```powershell
dotnet --version
```

## 2. Windows (рекомендуемый путь)

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install-global.ps1
```

Скрипт:

- публикует self-contained бинарник `yasn.exe`;
- копирует его в `%LOCALAPPDATA%\yasn\bin`;
- добавляет этот каталог в User PATH (если отсутствует);
- обновляет PATH в текущей сессии.

Проверка:

```powershell
yasn --help
yasn version
```

## 3. Linux/macOS

```bash
bash scripts/install-global.sh
```

Скрипт:

- публикует self-contained бинарник для текущей ОС/архитектуры;
- устанавливает его в `~/.local/share/yasn/toolchain/<runtime>/yasn`;
- создаёт/обновляет `~/.local/bin/yasn`.

Проверка:

```bash
yasn --help
yasn version
```

Если `yasn` не найден, добавьте `~/.local/bin` в `PATH`.

## 4. Ручная публикация

Windows x64:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-native.ps1 -Runtime win-x64
```

После публикации бинарник находится в:

`native/yasn-native/bin/Release/net10.0/win-x64/publish/yasn.exe`

Linux x64:

```bash
dotnet publish native/yasn-native/yasn-native.csproj \
  -c Release -r linux-x64 --self-contained true \
  /p:PublishSingleFile=true /p:PublishTrimmed=false
```

macOS arm64:

```bash
dotnet publish native/yasn-native/yasn-native.csproj \
  -c Release -r osx-arm64 --self-contained true \
  /p:PublishSingleFile=true /p:PublishTrimmed=false
```

## 5. Запуск без глобальной установки

```powershell
dotnet run --project native/yasn-native/yasn-native.csproj -- run examples/тест.яс
```
