---
layout: default
title: Быстрый старт
---

# Быстрый старт

Цель: за 5–10 минут пройти путь от исходника до запуска приложения.

## 0. Убедитесь, что `yasn` установлен

```powershell
yasn --help
```

Если команда не найдена: [installation.md](installation.md).

## 1. Создайте программу

Файл `привет.яс`:

```text
функция main() -> Пусто:
    печать("Привет, ЯСНЫЙ!")
    вернуть пусто
```

## 2. Проверьте код

```powershell
yasn check привет.яс
```

Аналогия: как `tsc --noEmit` или `mypy` — проверка до запуска.

## 3. Запустите

```powershell
yasn run привет.яс
```

Аналогия: как `python app.py` / `go run main.go`.

## 4. Добавьте и запустите тесты

Файл `tests/привет_test.яс`:

```text
функция main() -> Пусто:
    утверждать_равно(2 + 2, 4)
    вернуть пусто
```

Запуск:

```powershell
yasn test
```

## 5. Соберите артефакт

```powershell
yasn build привет.яс -o привет.ybc
yasn exec привет.ybc
```

## 6. Упакуйте приложение

```powershell
yasn pack привет.яс -o привет.yapp --name привет
yasn run-app привет.yapp
```

Это уже собранное приложение, которое можно переносить и запускать как единый артефакт.

## 7. Запуск backend из `yasn.toml`

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

## 8. Добавление React UI (опционально)

1. Соберите frontend в `ui/dist`.
2. Упакуйте backend + UI:

```powershell
yasn pack backend/main.яс -o app.yapp --ui-dist ui/dist
```

3. Запустите приложение:

```powershell
yasn run-app app.yapp --port 8080
```

UI в `run-app` режиме должен ходить в API через `/api/*`:
- `GET /api/functions`
- `GET /api/schema`
- `POST /api/call`

## Куда дальше

- Учебник: [language-tutorial.md](language-tutorial.md)
- CLI: [cli-reference.md](cli-reference.md)
- Контракт UI: [ui-contract.md](ui-contract.md)
