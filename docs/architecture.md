---
layout: default
title: Архитектура компилятора и рантайма
---

# Архитектура компилятора и рантайма

## 1. Pipeline

`source(.яс)` -> `lexer` -> `parser(AST)` -> `module resolver` -> `type checker` -> `compiler(bytecode)` -> `VM`

Команды `yasn run/check/build/pack/test` используют этот pipeline.

## 2. Ключевые модули

- `native/yasn-native/Core/Lexer.cs` — токенизация и отступы.
- `native/yasn-native/Core/Parser.cs` — построение AST.
- `native/yasn-native/Core/ModuleResolver.cs` — импорты/экспорты/алиасы/namespace.
- `native/yasn-native/Core/TypeChecker.cs` — статическая проверка типов.
- `native/yasn-native/Core/Compiler.cs` — генерация байткода.
- `native/yasn-native/Runtime/VirtualMachine.cs` — исполнение байткода и builtin-stdlib.
- `native/yasn-native/Bytecode/Codec.cs` — формат `.ybc`.
- `native/yasn-native/Bytecode/AppBundle.cs` — формат `.yapp`.
- `native/yasn-native/Deps/DependencyManager.cs` — `yasn deps` и lock-file.
- `native/yasn-native/Server/BackendServer.cs` — HTTP backend.
- `native/yasn-native/Runner/ProjectRunner.cs` — `dev/start`.
- `native/yasn-native/Runner/TestRunner.cs` — `yasn test`.
- `native/yasn-native/App/AppInstaller.cs` — установка CLI-приложений.
- `native/yasn-native/Program.cs` — командная строка.

## 3. Type checker

`TypeChecker` выполняет:

- контроль объявлений и областей видимости;
- совместимость присваиваний и `return`;
- валидацию сигнатур функций и `main`;
- проверку индексации/коллекций/условий;
- контроль аргументов функций и builtin-вызовов.

Проверка выполняется до генерации байткода и останавливает сборку при ошибках.

## 4. Модульная линковка

`ModuleResolver` работает до type checker и компиляции:

1. находит корень проекта через `yasn.toml`;
2. резолвит `подключить`/`из ... подключить`;
3. применяет правила `экспорт`;
4. разворачивает alias/namespace-ссылки;
5. защищает от циклических импортов;
6. подключает `.yasn/deps`.

## 5. VM и стандартная библиотека

VM предоставляет:

- базовые функции коллекций/строк/чисел;
- async task API (`запустить`, `готово`, `ожидать`, `ожидать_все`, `отменить`);
- file API (`файл_читать`, `файл_записать`, `файл_существует`, `файл_удалить`);
- JSON API (`json_разобрать`, `json_строка`);
- HTTP API (`http_get`, `http_post`);
- тестовые утверждения (`утверждать`, `утверждать_равно`, `провал`).

## 6. Backend слой

`yasn serve` / `yasn run dev` / `yasn start` поднимают HTTP API:

- `GET /health`
- `GET /functions`
- `GET /schema`
- `POST /call`

`BackendKernel` компилирует и исполняет функции на том же pipeline.

`yasn run-app` при наличии встроенного UI (`--ui-dist`) поднимает app runtime:

- отдает статику фронтенда из `.yapp`;
- проксирует API через `/api/*` (`/api/functions`, `/api/schema`, `/api/call`).

## 7. Артефакты

- `.ybc` — байткод программы;
- `.yapp` — контейнер приложения (метаданные + байткод + опциональный UI dist).

## 8. Ограничения

- VM-интерпретация (без native-code backend);
- нет пользовательских классов/структур;
- нет `try/catch` как синтаксической конструкции;
- нет официального удалённого package registry.
