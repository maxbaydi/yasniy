# task.md

## Цель задачи
- Исправить CI/CD релизов: после сборки в GitHub Releases должен появляться installer-артефакт.

## Пошаговый план
- [x] Проверить текущий workflow релиза и найти причины "пустых" релизов.
- [x] Внести правки в `.github/workflows/release.yml` для надежной публикации артефактов.
- [x] Добавить диагностические шаги (проверка наличия exe перед upload в release).
- [ ] Проверить изменения и выполнить минимальную проверку по AGENTS.
- [ ] Обновить итог в `task.md` и удалить `task.md`.

## Рабочий контекст
- Менять только релизный pipeline и связанные точечные места.
- Не ломать существующую сборку installer.
- Если файл installer не найден, workflow должен падать явно, а не создавать "пустой" релиз.

## Текущий этап
- Проверяю дифф `release.yml` и запускаю минимальные проверки (`dotnet build`, `dotnet run -- test`).

## Выполнено
- В `.github/workflows/release.yml` добавлено:
  - `permissions: contents: write` для публикации релиза через `GITHUB_TOKEN`.
  - шаг `Verify installer output`:
    - проверяет наличие `installer/dist/Yasn-Setup-*.exe`;
    - печатает диагностику, если файл не найден;
    - сохраняет точный путь в `steps.installer.outputs.asset_path`.
  - `Create Release` теперь использует точный путь из output и `fail_on_unmatched_files: true`.
