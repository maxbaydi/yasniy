---
layout: default
title: Публикация на GitHub Pages
---

# Публикация документации на GitHub Pages

Документация деплоится автоматически через GitHub Actions, без ручного `subtree`.

## 1. Что уже подготовлено

- Страницы документации в `docs/*.md` с взаимными ссылками.
- `docs/_config.yml` для Jekyll-конфигурации.
- `docs/index.md` как главная страница сайта.
- Workflow: `.github/workflows/deploy-docs.yml`.

## 2. Как работает автодеплой

- При `push` в `main` (если изменены файлы в `docs/**`) запускается workflow.
- Workflow собирает сайт Jekyll из `docs/` и деплоит его в GitHub Pages.
- Деплой можно запустить вручную через `workflow_dispatch`.

## 3. Настройка GitHub Pages

1. Откройте `Settings -> Pages`.
2. В `Build and deployment` выберите `GitHub Actions`.
3. Сохраните настройки и дождитесь первого успешного workflow.

После публикации сайт будет доступен по адресу:

```text
https://<owner>.github.io/<repo>/
```

## 4. Готовые ссылки на страницы

Подставьте ваши `<owner>` и `<repo>`:

- Главная: `https://<owner>.github.io/<repo>/`
- Установка: `https://<owner>.github.io/<repo>/installation.html`
- Быстрый старт: `https://<owner>.github.io/<repo>/quickstart.html`
- CLI: `https://<owner>.github.io/<repo>/cli-reference.html`
- UI контракт: `https://<owner>.github.io/<repo>/ui-contract.html`
- Архитектура: `https://<owner>.github.io/<repo>/architecture.html`

## 5. Проверка после деплоя

- Открывается главная `index`.
- Работают внутренние ссылки между страницами.
- Код-блоки и кириллица отображаются корректно.

## 6. Частые проблемы

- Сайт не обновился: подождите 1–3 минуты и обновите страницу без кэша.
- 404 на странице: проверьте, что в `Settings -> Pages` выбран `GitHub Actions`.
- Некорректная кодировка: убедитесь, что файлы сохранены в UTF-8.

## 7. Если нужен старый branch-based сценарий

Если по политике проекта требуется публикация только из ветки, можно вернуться к схеме `docs` branch. Но текущая конфигурация оптимизирована под автоматический деплой без ручных шагов.

## 8. Дополнительно: Yasniy Wiki

Помимо публикации в GitHub Pages, можно поддерживать страницы в Wiki:

```text
https://github.com/maxbaydi/yasniy/wiki
```

Практика синхронизации:
- исходник держать в `docs/*.md`;
- в Wiki переносить только пользовательские overview-страницы и навигацию;
- при обновлении CLI/API-контрактов сначала обновлять `docs/`, затем соответствующие страницы в Wiki.

Для автоматической синхронизации добавлен workflow:

```text
.github/workflows/sync-wiki.yml
```

Если Wiki пока пустая, запустите его вручную через `Actions -> Sync Docs to Wiki -> Run workflow`.
