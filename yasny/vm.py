from __future__ import annotations

from concurrent.futures import CancelledError, Future, ThreadPoolExecutor, TimeoutError
from dataclasses import dataclass
import threading
import time
from typing import Callable

from .bc import FunctionBC, ProgramBC
from .diagnostics import YasnyError


BuiltinFn = Callable[[list[object], list[object]], object]


@dataclass(slots=True)
class Frame:
    function: FunctionBC
    locals: list[object]
    ip: int = 0


@dataclass(slots=True)
class TaskHandle:
    task_id: int
    future: Future[object]


class VirtualMachine:
    def __init__(
        self,
        program: ProgramBC,
        path: str | None = None,
    ):
        self.program = program
        self.path = path
        self.globals: list[object] = []
        self._initialized = False
        self._state_lock = threading.RLock()
        self._task_lock = threading.Lock()
        self._executor: ThreadPoolExecutor | None = None
        self._task_counter = 0
        self.builtins: dict[str, BuiltinFn] = {
            "печать": self._builtin_print,
            "длина": self._builtin_len,
            "диапазон": self._builtin_range,
            "ввод": self._builtin_input,
            "пауза": self._builtin_sleep,
            "строка": self._builtin_to_string,
            "число": self._builtin_to_int,
            "запустить": self._builtin_spawn,
            "готово": self._builtin_done,
            "ожидать": self._builtin_wait,
            "ожидать_все": self._builtin_wait_all,
            "отменить": self._builtin_cancel,
        }

    def run(self) -> None:
        self.globals = [None] * int(self.program.global_count)
        self._execute_function(self.program.entry, [], self.globals)
        self._initialized = True

    def call_function(self, function_name: str, args: list[object] | None = None, reset_state: bool = True) -> object:
        call_args = list(args or [])
        with self._state_lock:
            if reset_state or not self._initialized:
                self.run()
            return self._call_function(function_name, call_args, self.globals)

    def invoke_host_function(
        self,
        function_name: str,
        args: list[object] | None = None,
        globals_store: list[object] | None = None,
    ) -> object:
        call_args = list(args or [])
        with self._state_lock:
            if globals_store is None:
                if not self._initialized:
                    self.run()
                globals_store = self.globals
            return self._call_function(function_name, call_args, globals_store)

    def _call_function(self, function_name: str, args: list[object], globals_store: list[object]) -> object:
        if function_name in self.builtins:
            return self.builtins[function_name](args, globals_store)
        if function_name not in self.program.functions:
            raise YasnyError(f"Неизвестная функция: {function_name}", path=self.path)
        return self._execute_function(self.program.functions[function_name], args, globals_store)

    def _execute_function(self, fn: FunctionBC, args: list[object], globals_store: list[object]) -> object:
        if len(args) != len(fn.params):
            raise YasnyError(
                f"Функция '{fn.name}' ожидает {len(fn.params)} аргументов, получено {len(args)}",
                path=self.path,
            )

        frame = Frame(function=fn, locals=[None] * fn.local_count, ip=0)
        for idx, value in enumerate(args):
            frame.locals[idx] = value

        stack: list[object] = []
        instructions = fn.instructions

        while frame.ip < len(instructions):
            ins = instructions[frame.ip]
            frame.ip += 1
            op = ins.op
            argv = ins.args

            if op == "CONST":
                stack.append(argv[0] if argv else None)
            elif op == "CONST_NULL":
                stack.append(None)
            elif op == "LOAD":
                slot = int(argv[0])
                stack.append(frame.locals[slot])
            elif op == "STORE":
                slot = int(argv[0])
                frame.locals[slot] = stack.pop()
            elif op == "GLOAD":
                slot = int(argv[0])
                stack.append(globals_store[slot])
            elif op == "GSTORE":
                slot = int(argv[0])
                globals_store[slot] = stack.pop()
            elif op == "POP":
                if stack:
                    stack.pop()
            elif op == "ADD":
                b, a = self._pop2(stack)
                stack.append(a + b)
            elif op == "SUB":
                b, a = self._pop2(stack)
                stack.append(a - b)
            elif op == "MUL":
                b, a = self._pop2(stack)
                stack.append(a * b)
            elif op == "DIV":
                b, a = self._pop2(stack)
                if isinstance(a, int) and isinstance(b, int):
                    stack.append(int(a / b))
                else:
                    stack.append(a / b)
            elif op == "MOD":
                b, a = self._pop2(stack)
                stack.append(a % b)
            elif op == "NEG":
                stack.append(-stack.pop())
            elif op == "NOT":
                stack.append(not stack.pop())
            elif op == "AND":
                b, a = self._pop2(stack)
                stack.append(bool(a) and bool(b))
            elif op == "OR":
                b, a = self._pop2(stack)
                stack.append(bool(a) or bool(b))
            elif op == "EQ":
                b, a = self._pop2(stack)
                stack.append(a == b)
            elif op == "NE":
                b, a = self._pop2(stack)
                stack.append(a != b)
            elif op == "LT":
                b, a = self._pop2(stack)
                stack.append(a < b)
            elif op == "LE":
                b, a = self._pop2(stack)
                stack.append(a <= b)
            elif op == "GT":
                b, a = self._pop2(stack)
                stack.append(a > b)
            elif op == "GE":
                b, a = self._pop2(stack)
                stack.append(a >= b)
            elif op == "JMP":
                frame.ip = int(argv[0])
            elif op == "JMP_FALSE":
                cond = stack.pop()
                if not bool(cond):
                    frame.ip = int(argv[0])
            elif op == "CALL":
                fn_name = str(argv[0])
                argc = int(argv[1])
                call_args = [stack.pop() for _ in range(argc)]
                call_args.reverse()
                result = self._call_function(fn_name, call_args, globals_store)
                stack.append(result)
            elif op == "RET":
                return stack.pop() if stack else None
            elif op == "MAKE_LIST":
                count = int(argv[0])
                items = [stack.pop() for _ in range(count)]
                items.reverse()
                stack.append(items)
            elif op == "MAKE_DICT":
                count = int(argv[0])
                raw = [stack.pop() for _ in range(count * 2)]
                raw.reverse()
                obj: dict[object, object] = {}
                for i in range(0, len(raw), 2):
                    obj[raw[i]] = raw[i + 1]
                stack.append(obj)
            elif op == "LIST_GET":
                idx = stack.pop()
                arr = stack.pop()
                stack.append(arr[idx])
            elif op == "INDEX_GET":
                idx = stack.pop()
                target = stack.pop()
                if isinstance(target, (list, str)):
                    stack.append(target[int(idx)])
                elif isinstance(target, dict):
                    stack.append(target[idx])
                else:
                    raise YasnyError(f"INDEX_GET не поддерживается для типа {type(target).__name__}", path=self.path)
            elif op == "INDEX_SET":
                value = stack.pop()
                idx = stack.pop()
                target = stack.pop()
                if isinstance(target, list):
                    target[int(idx)] = value
                elif isinstance(target, dict):
                    target[idx] = value
                else:
                    raise YasnyError(f"INDEX_SET не поддерживается для типа {type(target).__name__}", path=self.path)
                stack.append(value)
            elif op == "LEN":
                value = stack.pop()
                stack.append(len(value))
            elif op == "HALT":
                return None
            else:
                raise YasnyError(f"Неизвестная инструкция VM: {op}", path=self.path)

        return None

    def _builtin_print(self, args: list[object], _: list[object]) -> object:
        print(*[self._format_value(v) for v in args])
        return None

    def _builtin_len(self, args: list[object], _: list[object]) -> object:
        if len(args) != 1:
            raise YasnyError("длина(x) принимает ровно 1 аргумент", path=self.path)
        return len(args[0])

    def _builtin_range(self, args: list[object], _: list[object]) -> object:
        if len(args) != 2:
            raise YasnyError("диапазон(нач, конец) принимает 2 аргумента", path=self.path)
        start, end = args
        return list(range(int(start), int(end)))

    def _builtin_input(self, args: list[object], _: list[object]) -> object:
        if args:
            raise YasnyError("ввод() не принимает аргументы", path=self.path)
        return input()

    def _builtin_sleep(self, args: list[object], _: list[object]) -> object:
        if len(args) != 1:
            raise YasnyError("пауза(мс) принимает ровно 1 аргумент", path=self.path)
        delay_ms = self._coerce_non_negative_int(args[0], "пауза(мс)")
        time.sleep(delay_ms / 1000.0)
        return None

    def _builtin_to_string(self, args: list[object], _: list[object]) -> object:
        if len(args) != 1:
            raise YasnyError("строка(x) принимает ровно 1 аргумент", path=self.path)
        return str(self._format_value(args[0]))

    def _builtin_to_int(self, args: list[object], _: list[object]) -> object:
        if len(args) != 1:
            raise YasnyError("число(x) принимает ровно 1 аргумент", path=self.path)
        val = args[0]
        if isinstance(val, int):
            return val
        if isinstance(val, bool):
            return 1 if val else 0
        if isinstance(val, str):
            cleaned = val.strip()
            if not cleaned:
                return 0
            try:
                return int(cleaned)
            except ValueError:
                raise YasnyError(f"Невозможно преобразовать '{val}' в число", path=self.path)
        return int(val)

    def _builtin_spawn(self, args: list[object], globals_store: list[object]) -> object:
        if len(args) < 1:
            raise YasnyError("запустить(имя, ...args) требует минимум 1 аргумент", path=self.path)
        fn_name_raw = args[0]
        if not isinstance(fn_name_raw, str) or not fn_name_raw:
            raise YasnyError("Первый аргумент запустить(...) должен быть непустой Строка", path=self.path)
        fn_name = fn_name_raw
        if fn_name not in self.builtins and fn_name not in self.program.functions:
            raise YasnyError(f"Неизвестная функция: {fn_name}", path=self.path)

        call_args = list(args[1:])
        snapshot = self._clone_globals(globals_store)
        task_id = self._next_task_id()
        executor = self._get_executor()
        future = executor.submit(self._call_function, fn_name, call_args, snapshot)
        return TaskHandle(task_id=task_id, future=future)

    def _builtin_done(self, args: list[object], _: list[object]) -> object:
        if len(args) != 1:
            raise YasnyError("готово(задача) принимает ровно 1 аргумент", path=self.path)
        task = self._expect_task_handle(args[0], "готово(задача)")
        return task.future.done()

    def _builtin_wait(self, args: list[object], _: list[object]) -> object:
        if len(args) not in (1, 2):
            raise YasnyError("ожидать(задача[, таймаут_мс]) принимает 1 или 2 аргумента", path=self.path)
        task = self._expect_task_handle(args[0], "ожидать(задача[, таймаут_мс])")
        timeout_s: float | None = None
        if len(args) == 2:
            timeout_ms = self._coerce_non_negative_int(args[1], "ожидать(..., таймаут_мс)")
            timeout_s = timeout_ms / 1000.0
        return self._wait_task_result(task, timeout_s)

    def _builtin_wait_all(self, args: list[object], _: list[object]) -> object:
        if len(args) not in (1, 2):
            raise YasnyError(
                "ожидать_все(список_задач[, таймаут_мс]) принимает 1 или 2 аргумента",
                path=self.path,
            )
        raw_tasks = args[0]
        if not isinstance(raw_tasks, list):
            raise YasnyError("Первый аргумент ожидать_все(...) должен быть списком", path=self.path)
        timeout_s: float | None = None
        if len(args) == 2:
            timeout_ms = self._coerce_non_negative_int(args[1], "ожидать_все(..., таймаут_мс)")
            timeout_s = timeout_ms / 1000.0

        results: list[object] = []
        for value in raw_tasks:
            task = self._expect_task_handle(value, "ожидать_все(список_задач[, таймаут_мс])")
            results.append(self._wait_task_result(task, timeout_s))
        return results

    def _builtin_cancel(self, args: list[object], _: list[object]) -> object:
        if len(args) != 1:
            raise YasnyError("отменить(задача) принимает ровно 1 аргумент", path=self.path)
        task = self._expect_task_handle(args[0], "отменить(задача)")
        return task.future.cancel()

    def _pop2(self, stack: list[object]) -> tuple[object, object]:
        b = stack.pop()
        a = stack.pop()
        return b, a

    def _coerce_non_negative_int(self, value: object, context: str) -> int:
        if isinstance(value, bool) or not isinstance(value, int):
            raise YasnyError(f"{context}: ожидался Цел >= 0", path=self.path)
        if value < 0:
            raise YasnyError(f"{context}: ожидался Цел >= 0", path=self.path)
        return value

    def _expect_task_handle(self, value: object, context: str) -> TaskHandle:
        if isinstance(value, TaskHandle):
            return value
        raise YasnyError(f"{context}: ожидался объект типа Задача", path=self.path)

    def _wait_task_result(self, task: TaskHandle, timeout_s: float | None) -> object:
        try:
            return task.future.result(timeout=timeout_s)
        except CancelledError as exc:
            raise YasnyError(f"Задача #{task.task_id} была отменена", path=self.path) from exc
        except TimeoutError as exc:
            raise YasnyError(f"Истёк таймаут ожидания задачи #{task.task_id}", path=self.path) from exc
        except YasnyError:
            raise
        except Exception as exc:  # noqa: BLE001
            raise YasnyError(f"Задача #{task.task_id} завершилась с ошибкой: {exc}", path=self.path) from exc

    def _get_executor(self) -> ThreadPoolExecutor:
        with self._task_lock:
            if self._executor is None:
                self._executor = ThreadPoolExecutor(thread_name_prefix="yasn-task")
            return self._executor

    def _next_task_id(self) -> int:
        with self._task_lock:
            self._task_counter += 1
            return self._task_counter

    def _clone_globals(self, values: list[object]) -> list[object]:
        return [self._clone_value(value) for value in values]

    def _clone_value(self, value: object) -> object:
        if value is None or isinstance(value, (bool, int, float, str, TaskHandle)):
            return value
        if isinstance(value, list):
            return [self._clone_value(item) for item in value]
        if isinstance(value, dict):
            return {self._clone_value(key): self._clone_value(val) for key, val in value.items()}
        return value

    def _format_value(self, value: object) -> object:
        if value is True:
            return "истина"
        if value is False:
            return "ложь"
        if value is None:
            return "пусто"
        if isinstance(value, TaskHandle):
            if value.future.cancelled():
                status = "отменена"
            elif value.future.done():
                status = "готово"
            else:
                status = "выполняется"
            return f"<задача #{value.task_id}: {status}>"
        if isinstance(value, list):
            return "[" + ", ".join(str(self._format_value(v)) for v in value) + "]"
        if isinstance(value, dict):
            parts = [f"{self._format_value(k)}: {self._format_value(v)}" for k, v in value.items()]
            return "{ " + ", ".join(parts) + " }"
        return value

    def __del__(self) -> None:
        try:
            with self._task_lock:
                executor = self._executor
                self._executor = None
            if executor is not None:
                executor.shutdown(wait=False, cancel_futures=True)
        except Exception:  # noqa: BLE001
            pass
