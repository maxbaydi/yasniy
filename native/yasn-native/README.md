# YASN Native Toolchain (.NET)

`native/yasn-native` is a standalone compiler + VM implementation.
No Python runtime is required.

## Build

```powershell
dotnet build native/yasn-native/yasn-native.csproj -c Release
```

## Run from sources

```powershell
dotnet run --project native/yasn-native/yasn-native.csproj -- run backend/main.яс
```

## Commands

```text
run <file.яс|dev|start> [--backend ...] [--host ...] [--port ...]
dev [--backend ...] [--host ...] [--port ...]
start [--backend ...] [--host ...] [--port ...]
serve <backend.яс> [--host 127.0.0.1] [--port 8000]
check <file.яс>
test [path|file] [--pattern *_test.яс] [--fail-fast] [--verbose]
build <file.яс> [-o out.ybc]
exec <file.ybc>
pack <file.яс> [-o out.yapp] [--name app]
run-app <file.yapp>
install-app <file.яс> [--name app]
paths [--short]
deps [install|list] [--clean] [--all]
```

## Publish as self-contained binary

### Windows x64

```powershell
dotnet publish native/yasn-native/yasn-native.csproj \
  -c Release -r win-x64 --self-contained true \
  /p:PublishSingleFile=true /p:PublishTrimmed=false
```

### Linux x64

```powershell
dotnet publish native/yasn-native/yasn-native.csproj \
  -c Release -r linux-x64 --self-contained true \
  /p:PublishSingleFile=true /p:PublishTrimmed=false
```

### macOS ARM64

```powershell
dotnet publish native/yasn-native/yasn-native.csproj \
  -c Release -r osx-arm64 --self-contained true \
  /p:PublishSingleFile=true /p:PublishTrimmed=false
```

## Implementation status

- Native language pipeline: lexer -> parser -> module resolver -> type checker -> compiler -> VM.
- Native module imports/exports and aliases.
- Native dependency manager (`yasn deps`).
- Async task runtime with cancellation/timeouts.
- Builtin stdlib: collections, files, JSON, HTTP, asserts.
- Native test runner (`yasn test`).
- Native HTTP backend server (`yasn serve`).
