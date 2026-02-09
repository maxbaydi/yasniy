# Установка компилятора ЯСНЫЙ

Ниже описаны рабочие варианты установки команды `yasn`.

## 1. Требования

- Python 3.11+
- pip

Проверка:

```powershell
python --version
python -m pip --version
```

## 2. Рекомендуемо для Windows

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install-global.ps1
```

Скрипт:

- устанавливает пакет (`pip --user`)
- добавляет Python user `Scripts` в USER PATH
- добавляет этот путь в текущую сессию

Проверка:

```powershell
yasn --help
```

## 3. Режим разработки

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install-global.ps1 -Editable
```

## 4. Установка через pipx

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install-global.ps1 -Pipx
```

## 5. Ручная установка

```powershell
python -m pip install --user .
```

Если `yasn` не найден:

```powershell
python -c "import sysconfig; print(sysconfig.get_path('scripts', scheme='nt_user'))"
```

Добавьте выведенный каталог в PATH.

## 6. macOS/Linux

```bash
python3 -m pip install --user .
python3 -m yasn --help
```

## 7. Проверка после установки

```powershell
yasn --help
yasn paths
```

## 8. Fallback без PATH

Даже без глобальной команды можно запускать так:

```powershell
python -m yasn --help
python -m yasn run examples/тест.яс
```

