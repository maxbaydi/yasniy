# ЯСНЫЙ (`.яс`)

ЯСНЫЙ — нативный язык и toolchain для разработки на русском языке: компиляция, типизация, тесты, backend/runtime и упаковка приложений.

Ключевая идея проекта: писать бизнес-логику на русском синтаксисе и быстро доводить её до готового приложения. Для UI есть нативная интеграция с React (через встроенный web runtime и стабильный API-контракт).

## Для кого этот репозиторий

- Для новичков: быстрый вход, минимальный набор команд, понятный путь «написал -> запустил -> упаковал».
- Для профессионалов: полный нативный pipeline, модульная система, типизация, тест-раннер, backend-режим и контрактный UI runtime.

## Аналогии с привычными экосистемами

- `yasn run app.яс` ≈ `python app.py` / `go run main.go`
- `yasn check app.яс` ≈ `tsc --noEmit` / `mypy` / «static check»
- `yasn test` ≈ `pytest` / `go test`
- `yasn build app.яс -o app.ybc` ≈ компиляция в исполняемый артефакт
- `yasn pack app.яс -o app.yapp` ≈ «собрать приложение в единый bundle»
- `yasn run-app app.yapp` ≈ «запуск собранного приложения»
- `yasn run dev` ≈ backend/dev-server из конфигурации проекта

## Что умеет ЯСНЫЙ

- Нативный компилятор и VM (`native/yasn-native`, C#/.NET)
- Полный pipeline: lexer -> parser -> resolver -> type checker -> compiler -> VM
- Модули и экспорты (`подключить`, `из ... подключить`, `как`, `экспорт`)
- Проверка типов на этапе компиляции
- Тестовый раннер (`yasn test`)
- Артефакты `.ybc` и `.yapp`
- Backend-режим (`yasn serve`, `yasn run dev/start`)
- Стабильный UI-контракт: `/functions`, `/schema`, `/call`
- Встраиваемый React UI в `.yapp` через `--ui-dist`
- Установка приложений как пользовательских команд (`yasn install-app`)

## Установка

### Windows (рекомендуется)

1. Скачайте установщик из релизов: `Yasn-Setup-x.x.x.exe`
   - https://github.com/yasniy/yasniy/releases
2. Запустите установку.
3. Откройте новый терминал и проверьте:

```powershell
yasn --help
```

Установщик добавляет `yasn` в PATH и кладет UI-пакеты в `%LOCALAPPDATA%\Yasn\packages\`.

### Linux/macOS

```bash
bash scripts/install-global.sh
yasn --help
```

### Без глобальной установки (из исходников)

Требуется `.NET SDK 10.0+`.

```powershell
dotnet run --project native/yasn-native/yasn-native.csproj -- --help
```

## Быстрый старт (2-3 минуты)

Создайте файл `привет.яс`:

```text
функция main() -> Пусто:
    печать("Привет, ЯСНЫЙ!")
    вернуть пусто
```

Проверьте и запустите:

```powershell
yasn check привет.яс
yasn run привет.яс
```

Запустите тесты проекта:

```powershell
yasn test
```

Соберите и запустите приложение-артефакт:

```powershell
yasn pack привет.яс -o привет.yapp --name привет
yasn run-app привет.yapp
```

## Типовой workflow разработки

### 1) Локальная разработка backend

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

### 2) Добавление UI на React

1. Соберите frontend в `ui/dist`.
2. Упакуйте backend + UI в `.yapp`:

```powershell
yasn pack backend/main.яс -o app.yapp --ui-dist ui/dist
```

3. Запустите собранное приложение:

```powershell
yasn run-app app.yapp --host 127.0.0.1 --port 8080
```

В `run-app` режиме UI обращается к API через `/api/*`:

- `GET /api/functions`
- `GET /api/schema`
- `POST /api/call`

## Основные команды

```powershell
yasn run <file.яс>
yasn check <file.яс>
yasn test
yasn build <file.яс> -o out.ybc
yasn exec out.ybc
yasn pack <file.яс> -o app.yapp
yasn run-app app.yapp
yasn install-app <file.яс> --name my_app
yasn deps
yasn version
```

## VS Code

Расширение для подсветки `.яс`: `extensions/vscode-yasniy`.

Локальная установка:

- Windows: `%USERPROFILE%\.vscode\extensions\`
- Linux/macOS: `~/.vscode/extensions/`

## Документация

- Индекс: `docs/index.md`
- Установка: `docs/installation.md`
- Быстрый старт: `docs/quickstart.md`
- Учебник: `docs/language-tutorial.md`
- Справочник языка: `docs/language-reference.md`
- CLI: `docs/cli-reference.md`
- Упаковка и запуск: `docs/packaging-and-run.md`
- UI контракт и runtime: `docs/ui-contract.md`
- Архитектура: `docs/architecture.md`
- Troubleshooting: `docs/troubleshooting.md`
- Нативный toolchain: `docs/native-toolchain.md`
- Публикация docs на GitHub Pages: `docs/github-pages.md`

---

Если вы хотите быстро посмотреть «живой» пример с UI, откройте `taskboard-app` и запустите его как `.yapp`.
