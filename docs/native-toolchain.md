---
layout: default
title: Нативный toolchain YASN
---

# Нативный toolchain YASN

Этот документ описывает самостоятельный нативный стек `native/yasn-native`.

## Реализовано

- lexer/parser/module-resolver/type-checker/compiler/VM;
- модульная система (`подключить`, `из`, `как`, `экспорт`);
- менеджер зависимостей `yasn deps` с lock-файлом;
- async task builtins (`запустить`, `готово`, `ожидать`, `ожидать_все`, `отменить`);
- расширенная stdlib: файлы, JSON, HTTP, время, случайные числа;
- тестовый раннер `yasn test` и assert builtins;
- backend HTTP API (`serve`, `run dev`, `start`);
- форматы `.ybc` и `.yapp`;
- установка приложений как пользовательских команд (`install-app`);
- self-contained публикация для Windows/Linux/macOS.

## Сборка

```powershell
dotnet build native/yasn-native/yasn-native.csproj -c Release
```

## Запуск из исходников

```powershell
dotnet run --project native/yasn-native/yasn-native.csproj -- run backend/main.яс
```

## Публикация self-contained

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-native.ps1 -Runtime win-x64
```

Также поддерживаются `linux-x64`, `osx-arm64`.

## Команды CLI

```text
run <file.яс|dev|start>
dev
start
serve <backend.яс>
check <file.яс>
test [path|file]
build <file.яс>
exec <file.ybc>
pack <file.яс>
run-app <file.yapp>
install-app <file.яс>
paths
deps [install|list]
```
