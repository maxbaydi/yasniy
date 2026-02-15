---
layout: default
title: Документация ЯСНЫЙ
---

# Документация ЯСНЫЙ

ЯСНЫЙ — нативный язык и toolchain для разработки на русском языке.

Эта документация собрана так, чтобы по ней было удобно идти и начинающему, и опытному разработчику:
- новичку: быстро понять, установить и запустить;
- профессионалу: быстро найти точные контракты, команды и архитектурные детали.

## С чего начать

### Маршрут для новичка (15–30 минут)

1. [Установка](installation.md)
2. [Быстрый старт](quickstart.md)
3. [Учебник по языку](language-tutorial.md)
4. [Troubleshooting](troubleshooting.md)

### Маршрут для опытного разработчика

1. [CLI-справочник](cli-reference.md)
2. [Справочник языка](language-reference.md)
3. [UI контракт](ui-contract.md)
4. [Архитектура](architecture.md)
5. [Нативный toolchain](native-toolchain.md)

## Карта документации

- [installation.md](installation.md): установка на Windows, Linux/macOS и запуск из исходников.
- [quickstart.md](quickstart.md): путь «файл -> проверка -> запуск -> тест -> упаковка».
- [language-tutorial.md](language-tutorial.md): практическое обучение языку.
- [language-reference.md](language-reference.md): формальный справочник синтаксиса и конструкций.
- [cli-reference.md](cli-reference.md): все команды `yasn` с примерами.
- [packaging-and-run.md](packaging-and-run.md): артефакты `.ybc`/`.yapp`, запуск и установка как команды.
- [ui-contract.md](ui-contract.md): стабильный backend/UI контракт (`/functions`, `/schema`, `/call`).
- [architecture.md](architecture.md): устройство компилятора, VM и runtime.
- [native-toolchain.md](native-toolchain.md): детали нативной реализации.
- [troubleshooting.md](troubleshooting.md): типовые проблемы и решения.

## Аналогии с привычными экосистемами

- `yasn run app.яс` — похоже на `python app.py` или `go run main.go`.
- `yasn check app.яс` — аналог статической проверки (`tsc --noEmit`, `mypy`).
- `yasn test` — аналог `pytest`/`go test`.
- `yasn pack app.яс -o app.yapp` — аналог сборки приложения в переносимый bundle.
- `yasn run-app app.yapp` — запуск уже собранного приложения.

## Публикация документации как GitHub Pages

Готовый гайд: [github-pages.md](github-pages.md)

Там описано:
- как публиковать из ветки `docs`;
- как подготовить структуру страниц;
- какие ссылки использовать после публикации.
