# YASN Fullstack Example

Этот пример показывает связку:

- backend на ЯСНОМ (`examples/backend/main.яс`)
- frontend на HTML/JS (`examples/frontend/index.html`)
- модульную структуру backend (`examples/backend/lib/*.яс`)

## Быстрый запуск одной командой

Из каталога `examples`:

```powershell
yasn run dev
```

После запуска:

- backend: `http://127.0.0.1:8123`
- frontend: `http://127.0.0.1:5173`

## Ручной запуск в двух терминалах

Терминал 1:

```powershell
yasn serve backend/main.яс --host 127.0.0.1 --port 8123
```

Терминал 2:

```powershell
cd frontend
python -m http.server 5173 --bind 127.0.0.1
```

## Экспортированные функции backend

- `здоровье() -> Строка`
- `функции_api() -> Список[Строка]`
- `список_товаров() -> Список[Словарь[Строка, Строка]]`
- `текст_промо(код: Строка) -> Строка`
- `ошибка_заказа(код: Цел) -> Строка`
- `рассчитать_заказ(id: Строка, количество: Цел, город: Строка, промо: Строка) -> Словарь[Строка, Цел]`

## Пример API вызова (PowerShell)

```powershell
$body = @{
  function = "рассчитать_заказ"
  args = @("coffee", 2, "Москва", "SALE10")
  reset_state = $true
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
  -Uri "http://127.0.0.1:8123/call" `
  -ContentType "application/json; charset=utf-8" `
  -Body $body
```
